import AppKit
import ServiceManagement
import ApplicationServices
import UniformTypeIdentifiers
import Darwin
import Carbon

func verifySmoke(_ condition: @autoclosure () -> Bool, _ message: String = "UI verification failed", file: String = #fileID, line: Int = #line) {
    guard condition() else {
        FileHandle.standardError.write(Data("FAIL: \(file):\(line): \(message)\n".utf8))
        exit(1)
    }
}

func saveSmokePreview(_ view: NSView, to path: String) {
    view.layoutSubtreeIfNeeded()
    guard let bitmap = view.bitmapImageRepForCachingDisplay(in: view.bounds) else {
        verifySmoke(false, "Cannot capture native view"); return
    }
    view.cacheDisplay(in: view.bounds, to: bitmap)
    let preview = NSImage(size: view.bounds.size)
    preview.lockFocus()
    NSColor.windowBackgroundColor.setFill(); view.bounds.fill()
    let foreground = NSImage(size: view.bounds.size)
    foreground.addRepresentation(bitmap)
    foreground.draw(in: view.bounds, from: .zero, operation: .sourceOver, fraction: 1)
    preview.unlockFocus()
    guard let tiff = preview.tiffRepresentation, let rendered = NSBitmapImageRep(data: tiff),
          let data = rendered.representation(using: .png, properties: [:]) else {
        verifySmoke(false, "Cannot encode native view"); return
    }
    do { try data.write(to: URL(fileURLWithPath: path)) }
    catch { verifySmoke(false, "Cannot write native view: \(error)") }
}

private final class MouseRegistrationView: NSView {
    var received: ((Int) -> Void)?
    var wheelReceived: ((UInt16) -> Void)?
    var acceptsAllButtons = false
    var released: (() -> Void)?
    var cancelled: (() -> Void)?
    var note = "ここにポインタを置き、登録するサイドボタンを1回押してください。" { didSet { needsDisplay = true } }
    override var acceptsFirstResponder: Bool { true }
    override func draw(_ dirtyRect: NSRect) {
        NSColor.controlBackgroundColor.setFill()
        NSBezierPath(roundedRect:bounds,xRadius:8,yRadius:8).fill()
        (note as NSString).draw(in:bounds.insetBy(dx:14,dy:18),withAttributes:[.font:NSFont.systemFont(ofSize:13),.foregroundColor:NSColor.labelColor])
    }
    override func scrollWheel(with event: NSEvent) {
        guard acceptsAllButtons, let code = MouseLayoutInput.wheel(event) else { return }
        wheelReceived?(code)
    }
    override func otherMouseDown(with event: NSEvent) {
        guard acceptsAllButtons || event.buttonNumber >= 3 else { note = "中央ボタンではなく、進む／戻るに使うサイドボタンを押してください。"; return }
        received?(event.buttonNumber)
    }
    override func otherMouseUp(with event: NSEvent) { released?() }
    override func mouseUp(with event: NSEvent) { released?() }
    override func rightMouseUp(with event: NSEvent) { released?() }
    override func mouseDown(with event: NSEvent) { window?.makeFirstResponder(self); if acceptsAllButtons { received?(0) } }
    override func keyDown(with event: NSEvent) { if event.keyCode == 53 { cancelled?(); return }; note = "キー入力として検出：\(Keys.name(event.keyCode))（コード \(event.keyCode)）。ELECOM等の変換をOFFにして、再度ボタンを押してください。"; }
    override func rightMouseDown(with event: NSEvent) { if acceptsAllButtons { received?(1); return }; note = "右クリックではなく、登録するサイドボタンを押してください。" }
}

final class AppDelegate: NSObject, NSApplicationDelegate, NSWindowDelegate, NSTableViewDataSource, NSTableViewDelegate {
    private var window: NSWindow!
    private var workspace: WorkspaceController!
    private let table = NSTableView()
    private var recentStatus: [(Date,String)] = []
    private var resourceIssues: [String:String] = [:]
    private func resourceKey(_ kind: ActionType, _ value: String) -> String { kind.rawValue + ":" + value }
    private func issue(for mapping: Mapping) -> String? {
        let value = mapping.action == .url ? mapping.payload : mapping.application
        if [.file,.launch].contains(mapping.action), !FileManager.default.fileExists(atPath:value) { return "登録先が見つかりません。「変更」から現在の場所を選び直してください。" }
        return resourceIssues[resourceKey(mapping.action,value)]
    }
    private func inspectAction(_ mapping: Mapping) {
        let alert = NSAlert(); alert.messageText = mapping.targetDescription(layout:configuration.keyboardLayout)
        alert.informativeText = issue(for:mapping) ?? "この操作の失敗は記録されていません。詳しい状況は画面下の動作履歴から確認できます。"
        alert.addButton(withTitle:"閉じる"); showWindow(); alert.beginSheetModal(for:window)
    }

    private let status = NSTextField(wrappingLabelWithString: "停止中：設定後に「有効にする」を押してください。")
    private let toggle = NSButton(title: "有効にする", target: nil, action: nil)
    private let layer = NSPopUpButton()
    private let input = NSPopUpButton()
    private let action = NSPopUpButton()
    private let output = NSPopUpButton()
    private let space = StudioToggle(title: "Spaceレイヤーを使う", target: nil, action: nil)
    private let application = NSTextField(labelWithString: "アプリ未選択")
    private let chooseApp = NSButton(title: "アプリを選ぶ…", target: nil, action: nil)
    private let payloadControls = NSStackView()
    private let payloadSummary = NSTextField(labelWithString: "未設定")
    private var payload = ""
    private let modifierButtons = ["⌘ Command", "⌃ Control", "⌥ Option", "⇧ Shift"].map {
        StudioToggle(title: $0, target: nil, action: nil)
    }
    private let modifierValues: [Modifiers] = [.command, .control, .option, .shift]
    private var configuration = Configuration()
    private var applicationPath = ""
    private var loadFailed = false
    private var instanceLock: InstanceLock?
    private var typingPriorityToggle: NSButton?
    private var reverseScrollToggle: NSButton?
    private var statusItem: NSStatusItem!
    private let archiveAutomation = ArchiveAutomation()
    private let archiveEnabled = StudioToggle(title:"指定フォルダーの圧縮ファイルを自動解凍する",target:nil,action:nil)
    private let archiveTrash = StudioToggle(title:"解凍成功後に元の圧縮ファイルをゴミ箱へ移す",target:nil,action:nil)
    private let archiveWatch = NSTextField(wrappingLabelWithString:"")
    private let archiveDestination = NSTextField(wrappingLabelWithString:"")
    private let archiveStatus = NSTextField(wrappingLabelWithString:"自動解凍：OFF")
    private let disabledApps = NSPopUpButton()
    private let engine = InputEngine()
    private lazy var keyboardCleaner = KeyboardCleanerPanel()
    private let deck = DeckController()
    private var additionalDecks: [String:DeckController] = [:]
    private let profilePicker = NSPopUpButton()
    private let manualPicker = NSPopUpButton()
    private let automatic = StudioToggle(title: "アプリに合わせて自動切替", target: nil, action: nil)
    private let runtimeLabel = NSTextField(labelWithString: "実行中：標準")
    private let profileApps = NSTextField(labelWithString: "対象アプリ：なし")
    private var editedProfileID = "default"
    private var editableControls: [NSControl] = []
    private lazy var clipboardHistory = ClipboardHistoryController()
    private let profilePagePicker = NSPopUpButton()
    private let profileList = ProfileListView()
    private let profileApplications = ProfileApplicationsView()
    private var profileApplicationsHeight: NSLayoutConstraint?
    private weak var profileAssociateRow: NSView?
    private let newProfileName = NSTextField(string: "")
    private let profileSummary = NSTextField(wrappingLabelWithString: "")
    private var profileSpecificControls: [NSControl] = []
    private var editedMappings: [Mapping] { configuration.mappings(for: editedProfileID) }
    private var profileIDs: [String] { ["default"] + configuration.profiles.map { $0.id } }
    private let keypad = DeckController()
    private var keypadExtended = false
    private let loginToggle = StudioToggle(title:"Macへのログイン時にRELYRを起動",target:nil,action:nil)
    private let resumeToggle = StudioToggle(title:"起動時に入力を有効にする",target:nil,action:nil)
    private let loginStatus = NSTextField(wrappingLabelWithString:"")
    private var lastRequested = false
    private var uninstalling = false
    private var suppressRuntimeSave = false
    private var runtimeURL: URL { store.url.deletingLastPathComponent().appendingPathComponent("runtime-state.json") }
    private func saveRuntimeIntent() {
        guard instanceLock != nil, FileManager.default.fileExists(atPath:store.url.deletingLastPathComponent().path), !uninstalling, !smoke, !loadFailed, !suppressRuntimeSave, lastRequested != engine.requested else { return }
        do {
            try JSONEncoder().encode(engine.requested).write(to:runtimeURL,options:.atomic)
            lastRequested = engine.requested
        } catch { status.stringValue = "起動時の状態を保存できません：\(error.localizedDescription)" }
    }
    private let smoke = CommandLine.arguments.contains("--ui-smoke-test")
    private lazy var store: ConfigurationStore = {
        if smoke, CommandLine.arguments.count > 2 {
            return ConfigurationStore(url: URL(fileURLWithPath: CommandLine.arguments[2]).deletingLastPathComponent()
                .appendingPathComponent("ui-test-state/configuration.json"))
        }
        return ConfigurationStore(url: UninstallPlan.supportURL(library:FileManager.default.urls(for:.libraryDirectory,in:.userDomainMask)[0])
            .appendingPathComponent("configuration.json"))
    }()

    private func configureStartupOnly() {
        // Explicit maintenance command: register startup without installing any input hook.
        do {
            instanceLock = try UninstallPlan.acquireDataLock(library:FileManager.default.urls(for:.libraryDirectory,in:.userDomainMask)[0])
            var candidate = try store.load()
            if SMAppService.mainApp.status != .enabled && SMAppService.mainApp.status != .requiresApproval { try SMAppService.mainApp.register() }
            candidate.loginLaunchDesired = true; candidate.startInputOnLaunch = true
            try candidate.validate()
            // Change only startup preferences; retain unknown fields and all existing assignments.
            var object = try JSONSerialization.jsonObject(with:FileManager.default.fileExists(atPath:store.url.path) ? Data(contentsOf:store.url) : ConfigurationStore.encoded(candidate)) as? [String:Any] ?? [:]
            try FileManager.default.createDirectory(at:store.url.deletingLastPathComponent(),withIntermediateDirectories:true)
            object["loginLaunchDesired"] = true; object["startInputOnLaunch"] = true
            try JSONSerialization.data(withJSONObject:object,options:[.prettyPrinted,.sortedKeys]).write(to:store.url,options:.atomic)
            let state = SMAppService.mainApp.status
            print("STARTUP_CONFIGURED login=\(state == .enabled ? "enabled" : state == .requiresApproval ? "requiresApproval" : "unavailable") inputOnLaunch=true hooksInstalled=false")
            instanceLock?.release(); exit(state == .enabled ? 0 : 3)
        } catch {
            print("STARTUP_CONFIGURATION_FAILED: \(error.localizedDescription)")
            instanceLock?.release(); exit(2)
        }
    }
    func applicationDidFinishLaunching(_ notification: Notification) {
        // Load our bundled artwork directly so Dock/Command-Tab do not depend
        // on an old generic Launch Services icon cached before the first update.
        if let url = Bundle.main.url(forResource:"RELYR",withExtension:"icns"),
           let icon = NSImage(contentsOf:url), icon.isValid {
            NSApp.applicationIconImage = icon
            if smoke { verifySmoke(NSApp.applicationIconImage?.isValid == true,"Bundled RELYR application icon"); print("PASS: bundled RELYR icon decoded and assigned to application") }
        } else if smoke { verifySmoke(false,"Missing or invalid RELYR.icns") }
        if CommandLine.arguments.contains("--configure-startup") && !smoke { configureStartupOnly(); return }
        if !smoke {
            do { instanceLock = try UninstallPlan.acquireDataLock(library:FileManager.default.urls(for:.libraryDirectory,in:.userDomainMask)[0]) }
            catch InstanceLockError.busy {
                if let id = Bundle.main.bundleIdentifier,
                   let other = NSRunningApplication.runningApplications(withBundleIdentifier: id).first(where: { $0.processIdentifier != ProcessInfo.processInfo.processIdentifier }) {
                    other.activate(options: [.activateIgnoringOtherApps])
                }
                NSApp.terminate(nil); return
            } catch {
                let alert = NSAlert(); alert.messageText = "RELYRを起動できません"; alert.informativeText = error.localizedDescription
                alert.runModal(); NSApp.terminate(nil); return
            }
        }
        if !smoke {
            do { configuration = try store.load() }
            catch { loadFailed = true; status.stringValue = "設定を読み込めません。元の設定ファイルは保持しています：\(error.localizedDescription)" }
        } else {
            configuration.mappings = [
                Mapping(layer: .space, input: 0, action: .key, output: 123),
                Mapping(layer: .space, input: 8, action: .shortcut, output: 8, modifiers: .command),
                Mapping(layer: .space, input: 17, action: .launch, application: "/System/Applications/TextEdit.app")
            ]
            configuration.profiles = [Profile(name: "テキスト編集", bundleIdentifiers: ["com.apple.TextEdit"], mappings: configuration.mappings)]
            configuration.deck = configuration.mappings.map { DeckItem(title: $0.targetDescription, profileID: "default", layer: $0.layer, input: $0.input) }
        }
        if !smoke, let data = try? Data(contentsOf:runtimeURL) { lastRequested = (try? JSONDecoder().decode(Bool.self,from:data)) ?? false }
        engine.apply(configuration)
        engine.onProfile = { [weak self] name in
            self?.runtimeLabel.stringValue = "実行中：\(name)"
            self?.workspace?.setRuntime(name)
            if self?.workspace != nil { self?.updateDecks() }
        }
        engine.onMouseDevice = { [weak self] id in self?.workspace?.setActiveMouse(id) }
        engine.onKeyboardCleaning = { [weak self] in self?.keyboardCleaner.setVisible($0) }
        engine.onCleanerFeedback = { [weak self] in self?.keyboardCleaner.update(keys: $0, progress: $1) }
        keyboardCleaner.onFinish = { [weak self] in self?.engine.finishKeyboardCleaning() }
        engine.onClipboardHistory = { [weak self] in self?.clipboardHistory.show() }
        clipboardHistory.onEnabledChanged = { [weak self] enabled in
            guard let self = self else { return false }; var candidate = self.configuration
            candidate.clipboardHistoryEnabled = enabled; return self.persist(candidate)
        }
        if !smoke { clipboardHistory.setEnabled(configuration.clipboardHistoryEnabled == true) }
        engine.onKeypad = { [weak self] extended in self?.toggleKeypad(extended) }
        keypad.execute = { [weak self] id in
            guard let self = self, let code = UInt16(id) else { return }
            self.engine.performFromDeck(Mapping(layer:.normal,input:0,action:.key,output:code))
        }
        engine.onToggleAutoExtract = { [weak self] in
            guard let self = self else { return }
            var candidate = self.configuration; var options = candidate.archiveSettings ?? ArchiveSettings()
            options.enabled.toggle(); candidate.archiveSettings = options; _ = self.persist(candidate)
        }
        engine.onShowRelyr = { [weak self] in self?.showWindow() }
        engine.onToggleDeck = { [weak self] id in if id.isEmpty { self?.toggleDeck() } else { self?.toggleDeckLayout(id) } }
        engine.onStatus = { [weak self] message in
            guard let self = self else { return }
            self.saveRuntimeIntent()
            if self.recentStatus.last?.1 != message {
                self.recentStatus.append((Date(),message))
                if self.recentStatus.count > 30 { self.recentStatus.removeFirst() }
            }
            self.status.stringValue = message
            self.toggle.title = self.engine.requested ? "停止する" : "有効にする"
            self.statusItem?.button?.title = message.contains("記録中") ? "RELYR ● 記録中" : (self.engine.enabled ? "RELYR ●" : "RELYR ○")
            self.deck.setEnabled(self.engine.enabled)
            self.additionalDecks.values.forEach { $0.setEnabled(self.engine.enabled) }
            self.keypad.setEnabled(self.engine.enabled)
            self.deck.showNotice(message)
            self.additionalDecks.values.forEach { $0.showNotice(message) }
            self.workspace?.setStatus(message, enabled: self.engine.enabled, requested:self.engine.requested)
        }
        configureDeck(deck,layoutID:"default")
        buildMenus()
        // Restore input before building the editor and decoding Deck artwork.
        // UI preparation must not be a prerequisite for the resident engine.
        if !smoke && !loadFailed,
           configuration.startInputOnLaunch ?? (configuration.resumeInputOnLaunch == true && lastRequested) {
            engine.restorePreviousSession()
        }
        buildWindow()
        refreshConfigurationViews()
        archiveAutomation.onStatus = { [weak self] text in self?.archiveStatus.stringValue = text }
        if !smoke && !loadFailed { archiveAutomation.apply(configuration.archiveSettings ?? ArchiveSettings()) }
        workspace.onPermissionSetup = { [weak self] in
            self?.permissionResumeRequested = true
            self?.showPermissionSetup()
        }
        refreshPermissionEntry()
        presentInitialWindow(background:!smoke && Self.isLoginLaunch(NSAppleEventManager.shared().currentAppleEvent))
        if !smoke && !loadFailed {
            if configuration.loginLaunchDesired == true && SMAppService.mainApp.status == .notRegistered {
                do { try SMAppService.mainApp.register(); refreshLoginStatus() }
                catch { loginStatus.stringValue = "自動起動を登録できません：" + error.localizedDescription }
            }
        }
        if smoke {
            // Asymmetric alpha fixtures catch crop orientation and threshold changes.
            for (width,height) in [(19,13),(24,31)] {
                let context = CGContext(data:nil,width:width,height:height,bitsPerComponent:8,bytesPerRow:width*4,space:CGColorSpaceCreateDeviceRGB(),bitmapInfo:CGImageAlphaInfo.premultipliedLast.rawValue)!
                context.clear(CGRect(x:0,y:0,width:width,height:height))
                context.setFillColor(CGColor(red:0.2,green:0.7,blue:0.4,alpha:1))
                context.fill(CGRect(x:2,y:3,width:width-7,height:height-5))
                let cg = context.makeImage()!
                let bitmap = NSBitmapImageRep(cgImage:cg)
                let icon = NSImage(cgImage:cg,size:NSSize(width:width,height:height))
                let fitted = Studio.fittedAppArtwork(icon)
                verifySmoke(fitted.size == NSSize(width:width-7,height:height-5),"Fast artwork crop preserves asymmetric alpha bounds")
                let mapping = Mapping(layer:.normal,input:0,action:.file,application:"/isolated-test")
                let data = bitmap.representation(using:.png,properties:[:])!
                verifySmoke(Studio.deckImage(mapping,iconPNG:data) === Studio.deckImage(mapping,iconPNG:data),"Repeated custom icons reuse decoded artwork")
            }
            let icon = NSWorkspace.shared.icon(forFile:"/System/Library/CoreServices/Finder.app"); icon.size = NSSize(width:128,height:128)
            let artworkStart = ProcessInfo.processInfo.systemUptime
            for _ in 0..<10 { _ = Studio.fittedAppArtwork(icon) }
            print("MEASURE: ten uncached Finder icon crops: \((ProcessInfo.processInfo.systemUptime-artworkStart)*1000) ms")
        }
        if smoke {
            let loginEvent = NSAppleEventDescriptor.appleEvent(withEventClass:kCoreEventClass,eventID:kAEOpenApplication,targetDescriptor:nil,returnID:AEReturnID(kAutoGenerateReturnID),transactionID:AETransactionID(kAnyTransactionID))
            for newWindow in [false,true] {
                var error: NSDictionary?
                let script = NSAppleScript(source:FinderActions.script(newWindow:newWindow))!
                verifySmoke(script.compileAndReturnError(&error),"Finder script compiles without executing or changing windows")
            }
            let newWindowStep = MacroStep(action:Mapping(layer:.normal,input:0,action:.shortcut,output:45,modifiers:.command))
            let placeStep = MacroStep(action:Mapping(layer:.normal,input:0,action:.window,payload:WindowCommand.snapRight.rawValue))
            verifySmoke(MacroNewWindow.requiresConfirmation(newWindowStep,remaining:[MacroStep(delayMilliseconds:1000),placeStep][...]),"New window before placement waits through explicit delays")
            verifySmoke(!MacroNewWindow.requiresConfirmation(newWindowStep,remaining:[]),"Ordinary Command N remains ordinary input")
            let otherShortcut = MacroStep(action:Mapping(layer:.normal,input:0,action:.shortcut,output:45,modifiers:[.command,.shift]))
            verifySmoke(!MacroNewWindow.requiresConfirmation(otherShortcut,remaining:[placeStep][...]),"Other shortcuts are not rewritten")
            verifySmoke(!Self.isLoginLaunch(loginEvent),"Ordinary launch is interactive")
            loginEvent.setParam(NSAppleEventDescriptor(enumCode:keyAELaunchedAsLogInItem),forKeyword:keyAEPropData)
            verifySmoke(Self.isLoginLaunch(loginEvent),"Login Apple event detected")
            presentInitialWindow(background:true)
            verifySmoke(!window.isVisible && NSApp.activationPolicy() == .accessory,"Login presentation remains in background")
            presentInitialWindow(background:false)
            verifySmoke(window.isVisible && NSApp.activationPolicy() == .regular,"Manual open restores settings")
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.5) {
                verifySmoke(self.window.isVisible && !self.engine.enabled)
                verifySmoke(self.input.numberOfItems == Keys.inputs.count)
                verifySmoke(self.table.numberOfColumns == 1 && self.output.numberOfItems == Keys.outputs.count)
                self.table.selectRowIndexes(IndexSet(integer: 1), byExtendingSelection: false)
                verifySmoke(self.action.indexOfSelectedItem == 1 && self.modifierButtons[0].state == .on)
                verifySmoke(!self.chooseApp.isEnabled && self.output.isEnabled)
                let original = self.configuration
                self.saveMapping()
                verifySmoke(self.configuration == original, "UI edit must preserve profile and Deck references")
                self.table.selectRowIndexes(IndexSet(integer: 1), byExtendingSelection: false)
                self.deleteMapping()
                verifySmoke(self.configuration.mappings.count == 2 && self.configuration.deck.count == 2)
                verifySmoke(self.configuration.profiles[0].mappings.count == 3)
                verifySmoke(self.persist(original))
                self.manualPicker.selectItem(at: 1)
                self.manualProfileChanged()
                verifySmoke(self.engine.machine.runtimeProfileID == original.profiles[0].id)
                verifySmoke(self.profilePicker.indexOfSelectedItem == 0, "Runtime profile must not move the editor")
                verifySmoke(self.persist(original))
                self.table.selectRowIndexes(IndexSet(integer: 1), byExtendingSelection: false)
                self.deck.toggle()
                let profileBaseline = self.configuration
                let runtimeBeforeCreate = self.configuration.manualProfileID
                self.newProfileName.stringValue = "作成テスト"; self.createProfile()
                let createdID = self.editedProfileID
                verifySmoke(self.configuration.profiles.last?.name == "作成テスト" && self.profilePagePicker.indexOfSelectedItem == self.profilePicker.indexOfSelectedItem,"Quick create selects profile in both pickers")
                verifySmoke(self.configuration.manualProfileID == runtimeBeforeCreate,"Creating profile must not switch runtime")
                verifySmoke(self.profileList.selectedID == createdID,"Profile list follows quick create")
                self.profileList.selected?("default")
                verifySmoke(self.editedProfileID == "default" && self.configuration.manualProfileID == runtimeBeforeCreate,"List selection edits without switching runtime")
                self.profileList.selected?(createdID)
                self.useEditedProfile()
                verifySmoke(self.configuration.manualProfileID == createdID && !self.configuration.automaticProfiles,"Explicit use switches runtime")
                verifySmoke((try? self.store.load()) == self.configuration,"Profile changes persist")
                self.newProfileName.stringValue = ""; self.createProfile()
                verifySmoke(self.configuration.profiles.last?.name == "新しいプロファイル 1","Blank name gets useful default")
                self.editedProfileID = "default"; verifySmoke(self.persist(profileBaseline))
                print("PASS: direct profile creation, picker synchronization, explicit runtime switch and persistence")
                let mouseEditor = MouseLayoutEditor(configuration:self.configuration)
                mouseEditor.verifyPresentation(previewPath:URL(fileURLWithPath:CommandLine.arguments[2]).deletingPathExtension().path)

                self.engine.restorePreviousSession()
                self.beginModalMouseCapture(); self.toggleInput(); verifySmoke(!self.engine.requested,"Capture dialog must not start input interception")
                self.finishModalMouseCapture(); verifySmoke(self.engine.requested,"Closing capture restores prior ON intent")
                self.beginModalMouseCapture(); self.stopInput(); self.finishModalMouseCapture()
                verifySmoke(!self.engine.requested,"Explicit stop during capture must remain stopped")
                self.beginModalMouseCapture(); self.finishModalMouseCapture()
                verifySmoke(!self.engine.requested,"OFF capture remains OFF")
                ProfileApplicationsView.verify()
                ClipboardHistoryController.verify()
                self.workspace.verifyEditor()
                self.engine.verifyApplicationExclusions()
                self.engine.verifyMouseRouting()
                let cleanerPreview = URL(fileURLWithPath:CommandLine.arguments[2]).deletingPathExtension().path + "-keyboard-cleaner.png"
                self.keyboardCleaner.savePreview(to: cleanerPreview)
                self.engine.verifyMacros()
                self.workspace.verifyMacroEditor()
                self.workspace.verifyGestureEditor()
                self.workspace.showLayer(.space)
                self.beginInlineMouseCapture()
                verifySmoke(self.inlineCapture != nil && !self.engine.enabled)
                self.inlineCapture?.received?(0); self.inlineCapture?.released?()
                verifySmoke(self.inlineCapture == nil && self.workspace.currentLayer == .space && self.workspace.keyboard.keys[256]?.selected == true, "Inline selection preserves layer and selects physical button")
                self.beginInlineMouseCapture(); self.inlineCapture?.cancelled?()
                verifySmoke(self.inlineCapture == nil && !self.engine.requested, "Cancelling capture preserves OFF")
                let ids = StudioAction.catalog.map { $0.id }
                verifySmoke(Set(ids).count == ids.count, "Action identifiers must remain unique")
                for action in StudioAction.catalog where !action.parameterized {
                    var config = Configuration(); config.mappings = [action.mapping(layer:.space,input:0)]
                    do { try config.validate() } catch { verifySmoke(false,"Invalid catalog action \(action.id): \(error)") }
                }
                for command: SystemCommand in [.volumeUp,.volumeDown,.mute,.mediaPlayPause,.mediaNext,.mediaPrevious,.brightnessUp,.brightnessDown] {
                    let events = MacSystemEvents.media(MacSystemEvents.mediaKey(command)!,marker:123)!
                    verifySmoke(events.count == 2 && events.allSatisfy { $0.getIntegerValueField(.eventSourceUserData) == 123 }, "Complete native media key pair")
                }
                for command: SystemCommand in [.scrollUp,.scrollDown,.scrollLeft,.scrollRight] {
                    verifySmoke(MacSystemEvents.scroll(command,marker:123)?.type == .scrollWheel, "Wheel output is a scroll event")
                }
                self.toggleKeypad(false); verifySmoke(self.keypad.panel.isVisible && !self.keypad.panel.canBecomeKey); self.toggleKeypad(false)
                verifySmoke(!self.keypad.panel.isVisible && self.configuration == original, "Keypad does not replace saved Deck")
                print("PASS: \(ids.count) catalog entries, inline mouse selection/cancellation, nonactivating keypad, media/wheel event creation; zero system changes")
                self.workspace.showLayer(.normal)
                if CommandLine.arguments.count > 2 {
                    saveSmokePreview(self.window.contentView!, to: CommandLine.arguments[2])
                    self.workspace.showLibraryForPreview()
                    self.window.contentView!.layoutSubtreeIfNeeded()
                    let libraryPath = URL(fileURLWithPath: CommandLine.arguments[2]).deletingPathExtension().path + "-library.png"
                    saveSmokePreview(self.window.contentView!, to: libraryPath)
                    self.workspace.showSection("deck")
                    self.workspace.view.layoutSubtreeIfNeeded()
                    saveSmokePreview(self.workspace.view,to:URL(fileURLWithPath:CommandLine.arguments[2]).deletingPathExtension().path + "-deck-gallery.png")
                    self.workspace.selectDeckLayout("default")
                    self.window.contentView!.layoutSubtreeIfNeeded()
                    let editorPath = URL(fileURLWithPath: CommandLine.arguments[2]).deletingPathExtension().path + "-deck-editor.png"
                    saveSmokePreview(self.window.contentView!, to: editorPath)
                    self.workspace.showDeckDrawerForPreview(true)
                    saveSmokePreview(self.window.contentView!,to:URL(fileURLWithPath:CommandLine.arguments[2]).deletingPathExtension().path + "-deck-drawer.png")
                    let savedDeckWindowSize = self.window.contentView!.frame.size
                    self.window.setContentSize(NSSize(width:1100,height:800)); self.window.contentView!.layoutSubtreeIfNeeded()
                    self.workspace.showDeckDrawerForPreview(true)
                    saveSmokePreview(self.window.contentView!,to:URL(fileURLWithPath:CommandLine.arguments[2]).deletingPathExtension().path + "-deck-drawer-compact.png")
                    self.window.setContentSize(savedDeckWindowSize); self.window.contentView!.layoutSubtreeIfNeeded()
                    self.workspace.showDeckDrawerForPreview(false)
                    self.workspace.showDeckListForPreview(true)
                    saveSmokePreview(self.window.contentView!,to:URL(fileURLWithPath:CommandLine.arguments[2]).deletingPathExtension().path + "-deck-list.png")
                    self.workspace.showDeckListForPreview(false)
                    for section in ["profiles", "settings", "gestures", "macros", "list"] {
                        self.workspace.showSection(section)
                        self.window.contentView!.layoutSubtreeIfNeeded()
                        let path = URL(fileURLWithPath: CommandLine.arguments[2]).deletingPathExtension().path + "-" + section + ".png"
                        saveSmokePreview(self.window.contentView!, to: path)
                        let originalSize = self.window.contentView!.frame.size
                        self.window.setContentSize(NSSize(width:1100,height:820))
                        self.window.contentView!.layoutSubtreeIfNeeded()
                        saveSmokePreview(self.window.contentView!,to:URL(fileURLWithPath:path).deletingPathExtension().path + "-compact.png")
                        self.window.setContentSize(originalSize)
                    }
                    self.workspace.showLayer(.space)
                    self.workspace.showLibraryForPreview()
                    self.window.setContentSize(NSSize(width: 1100, height: 820))
                    self.window.contentView!.layoutSubtreeIfNeeded()
                    self.workspace.verifyGeometry()
                    self.workspace.verifyManagementGeometry()
                    self.workspace.setTheme(light: true)
                    let lightPath = URL(fileURLWithPath: CommandLine.arguments[2]).deletingPathExtension().path + "-light.png"
                    saveSmokePreview(self.window.contentView!, to: lightPath)
                    self.workspace.setTheme(light: false)
                    let compactPath = URL(fileURLWithPath: CommandLine.arguments[2]).deletingPathExtension().path + "-compact.png"
                    saveSmokePreview(self.window.contentView!, to: compactPath)
                    let deckPath = URL(fileURLWithPath: CommandLine.arguments[2]).deletingPathExtension().path + "-deck.png"
                    saveSmokePreview(self.deck.panel.contentView!, to: deckPath)
                }
                self.deck.verifyPresentation(expectedButtons: 3)
                DeckController.verifyLifecycle()
                self.window.performClose(nil)
                verifySmoke(!self.window.isVisible && !self.engine.enabled,"Closing editor hides it while resident process remains alive")
                self.showWindow()
                verifySmoke(self.window.isVisible,"Reopening editor preserves the existing process")
                print("PASS: native window, UI save/delete/persistence, profile/editor isolation, Deck geometry, close/reopen and disabled startup")
                NSApp.terminate(nil)
            }
        }
    }

    @objc private func checkMacUpdates() { MacUpdateController.shared.check() }

    private func buildMenus() {
        let main = NSMenu()
        let app = NSMenuItem()
        let appMenu = NSMenu()
        appMenu.addItem(item("更新を確認…", #selector(checkMacUpdates)))
        appMenu.addItem(item("RELYRをアンインストール…", #selector(uninstallApplication)))
        appMenu.addItem(withTitle: "RELYRを終了", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q")
        app.submenu = appMenu
        main.addItem(app)
        let edit = NSMenuItem()
        edit.title = "編集"
        let editMenu = NSMenu(title: "編集")
        let undo = NSMenuItem(title: "元に戻す", action: #selector(undoEditor), keyEquivalent: "z"); undo.target = self; editMenu.addItem(undo)
        let redo = NSMenuItem(title: "やり直す", action: #selector(redoEditor), keyEquivalent: "z"); redo.keyEquivalentModifierMask = [.command, .shift]; redo.target = self; editMenu.addItem(redo)
        editMenu.addItem(.separator())
        editMenu.addItem(withTitle: "コピー", action: #selector(NSText.copy(_:)), keyEquivalent: "c")
        editMenu.addItem(withTitle: "ペースト", action: #selector(NSText.paste(_:)), keyEquivalent: "v")
        editMenu.addItem(withTitle: "すべて選択", action: #selector(NSText.selectAll(_:)), keyEquivalent: "a")
        edit.submenu = editMenu
        main.addItem(edit)
        NSApp.mainMenu = main
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        statusItem.button?.title = "RELYR ○"
        let menu = NSMenu()
        menu.addItem(item("設定を開く", #selector(showWindow)))
        menu.addItem(item("更新を確認…", #selector(checkMacUpdates)))
        menu.addItem(item("Deckパネルを表示／非表示", #selector(toggleDeck)))
        menu.addItem(item("入力変換を停止", #selector(stopInput)))
        menu.addItem(.separator())
        menu.addItem(withTitle: "終了", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "")
        statusItem.menu = menu
    }

    private func item(_ title: String, _ selector: Selector) -> NSMenuItem {
        let result = NSMenuItem(title: title, action: selector, keyEquivalent: "")
        result.target = self
        return result
    }

    private func buildWindow() {
        window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 1360, height: 900),
            styleMask: [.titled, .closable, .miniaturizable, .resizable], backing: .buffered, defer: false)
        window.title = "RELYR for Mac — 試用版 0.2.0"
        window.minSize = NSSize(width: 1100, height: 820)
        window.appearance = NSAppearance(named: .darkAqua)
        window.backgroundColor = Studio.background
        window.isReleasedWhenClosed = false
        window.delegate = self
        window.center()
        toggle.target = self; toggle.action = #selector(toggleInput)
        let permission = NSButton(title: "アクセシビリティを許可…", target: self, action: #selector(showPermissionSetup))
        let controls = row([toggle, permission])
        let starterButton = NSButton(title: "サンプル追加", target: self, action: #selector(addStarter))
        let exportButton = NSButton(title: "設定を書き出す…", target: self, action: #selector(exportSettings))
        let importButton = NSButton(title: "設定を読み込む…", target: self, action: #selector(importSettings))
        let helpButton = NSButton(title: "使い方", target: self, action: #selector(showHelp))
        let create = NSButton(title: "プロファイルを作成", target: self, action: #selector(createProfile))
        let duplicate = NSButton(title: "複製", target: self, action: #selector(duplicateProfile))
        let rename = NSButton(title: "名前変更", target: self, action: #selector(renameProfile))
        let remove = NSButton(title: "削除", target: self, action: #selector(deleteProfile))
        profilePicker.target = self; profilePicker.action = #selector(profileChanged)
        manualPicker.target = self; manualPicker.action = #selector(manualProfileChanged)
        automatic.target = self; automatic.action = #selector(automaticChanged)
        profilePicker.widthAnchor.constraint(equalToConstant: 180).isActive = true
        manualPicker.widthAnchor.constraint(equalToConstant: 180).isActive = true
        let profileActions = row([duplicate, rename, remove])
        profilePagePicker.target = self; profilePagePicker.action = #selector(profilePageChanged)
        newProfileName.placeholderString = "新しいプロファイルの名前"
        newProfileName.widthAnchor.constraint(equalToConstant:240).isActive = true
        newProfileName.target = self; newProfileName.action = #selector(createProfile)
        newProfileName.setAccessibilityLabel("新しいプロファイルの名前")
        let runtimeControls = row([label("手動使用"), manualPicker, runtimeLabel])
        let associate = NSButton(title: "対象アプリを追加…", target: self, action: #selector(associateApplication))
        let clear = NSButton(title: "関連付けを解除", target: self, action: #selector(clearApplications))
        profileApps.lineBreakMode = .byTruncatingTail
        profileApps.widthAnchor.constraint(lessThanOrEqualToConstant: 490).isActive = true
        _ = row([associate, clear])
        status.setAccessibilityIdentifier("engine-status")
        status.font = .systemFont(ofSize: 12)
        let column = NSTableColumn(identifier: NSUserInterfaceItemIdentifier("mapping"))
        column.title = "保存済みの割り当て（選択して編集）"
        column.width = 740
        table.addTableColumn(column)
        table.delegate = self; table.dataSource = self
        table.rowHeight = 30
        table.usesAlternatingRowBackgroundColors = true
        table.setAccessibilityLabel("保存済みの割り当て")
        let scroll = NSScrollView()
        scroll.documentView = table
        scroll.hasVerticalScroller = true
        scroll.borderType = .bezelBorder
        scroll.heightAnchor.constraint(greaterThanOrEqualToConstant: 140).isActive = true
        layer.addItems(withTitles: Layer.allCases.map { $0.title })
        input.addItems(withTitles: Keys.inputs.map { $0.name })
        action.addItems(withTitles: ActionType.allCases.map { $0.title })
        output.addItems(withTitles: Keys.outputs.map { $0.name })
        layer.setAccessibilityLabel("レイヤー"); input.setAccessibilityLabel("入力キー")
        action.setAccessibilityLabel("動作"); output.setAccessibilityLabel("出力キー")
        action.target = self; action.action = #selector(actionChanged)
        chooseApp.target = self; chooseApp.action = #selector(selectApplication)
        payloadControls.orientation = .horizontal; payloadControls.spacing = 10; payloadControls.alignment = .centerY
        payloadControls.addArrangedSubview(NSButton(title: "内容を編集…", target: self, action: #selector(editPayload)))
        payloadControls.addArrangedSubview(payloadSummary)
        payloadSummary.lineBreakMode = .byTruncatingTail
        payloadSummary.widthAnchor.constraint(lessThanOrEqualToConstant: 660).isActive = true
        application.lineBreakMode = .byTruncatingMiddle
        space.state = configuration.spaceEnabled ? .on : .off
        space.target = self; space.action = #selector(spaceChanged)
        let save = NSButton(title: "この割り当てを保存", target: self, action: #selector(saveMapping))
        let delete = NSButton(title: "選択した割り当てを削除", target: self, action: #selector(deleteMapping))
        let addDeck = NSButton(title: "Deckへ追加", target: self, action: #selector(addToDeck))
        editableControls = [toggle, save, delete, space, create, duplicate, rename, remove, associate, clear, addDeck, automatic, starterButton, exportButton]
        profileSpecificControls = [rename, remove, clear, associate]
        for control in editableControls { control.isEnabled = !loadFailed }
        // Mirror the Windows manager: list on the left, selected profile on the right.
        let profilePanel = StudioSurface()
        let profileHeading = Studio.label("プロファイル",24)
        let listHeading = Studio.label("一覧",17,true)
        newProfileName.placeholderString = "新しいプロファイル名"
        newProfileName.constraints.filter { $0.firstAttribute == .width }.forEach { $0.isActive = false }
        create.title = "＋ プロファイルを追加"
        let associateRow = row([associate]); profileAssociateRow = associateRow
        let profileDetail = studioPanel("選択したプロファイル",spacing:16,views:[
            profileSummary, profileActions,
            row([NSButton(title:"キー・レイヤーの割り当てを編集",target:self,action:#selector(editProfileMappings))]),
            label("一覧の選択は編集対象だけを変更します。入力で使用するプロファイルは変わりません。"),
            profileApplications, associateRow,
            Studio.label("入力で使用するプロファイル",17,true), automatic, runtimeControls,
            row([NSButton(title:"編集中のプロファイルを今すぐ使用",target:self,action:#selector(useEditedProfile))]),
            label("今すぐ使用すると、自動切替をOFFにしてこのプロファイルへ切り替えます。")])
        profileApplicationsHeight = profileApplications.heightAnchor.constraint(equalToConstant:340)
        profileApplicationsHeight?.isActive = true
        profileApplications.change = { [weak self] ids in
            guard let self = self,!self.loadFailed,let index = self.configuration.profiles.firstIndex(where:{$0.id == self.editedProfileID}) else { return }
            var candidate = self.configuration; candidate.profiles[index].bundleIdentifiers = ids
            _ = self.persist(candidate)
        }
        profileList.selected = { [weak self] id in
            self?.editedProfileID = id; self?.refreshConfigurationViews()
        }
        profileList.rename = { [weak self] in self?.renameProfile() }
        profileList.duplicate = { [weak self] in self?.duplicateProfile() }
        profileList.remove = { [weak self] in self?.deleteProfile() }
        for view in [profileHeading,listHeading,profileList,newProfileName,create,profileDetail] { profilePanel.addSubview(view) }
        profilePanel.arrange = { [weak profilePanel] in
            guard let panel = profilePanel else { return }
            let h = panel.bounds.height, w = panel.bounds.width
            let sidebar:CGFloat = 208, gutter:CGFloat = 26
            profileHeading.frame = NSRect(x:12,y:12,width:w-24,height:32)
            listHeading.frame = NSRect(x:12,y:64,width:sidebar,height:28)
            self.profileList.frame = NSRect(x:12,y:100,width:sidebar,height:max(100,h-216))
            self.newProfileName.frame = NSRect(x:12,y:h-104,width:sidebar,height:32)
            create.frame = NSRect(x:12,y:h-60,width:sidebar,height:40)
            profileDetail.frame = NSRect(x:12+sidebar+gutter,y:56,width:max(320,w-sidebar-gutter-24),height:h-68)
            panel.separators = [NSRect(x:sidebar+24,y:64,width:1,height:h-84)]
            Studio.themeControls(panel)
        }
        let windowTarget = NSPopUpButton()
        windowTarget.addItems(withTitles: WindowTargetMode.allCases.map { $0.title })
        windowTarget.selectItem(at: WindowTargetMode.allCases.firstIndex(of: configuration.windowTarget ?? .active) ?? 0)
        windowTarget.target = self; windowTarget.action = #selector(windowTargetChanged)
        loginToggle.target = self; loginToggle.action = #selector(loginSettingChanged)
        resumeToggle.target = self; resumeToggle.action = #selector(resumeSettingChanged)
        resumeToggle.state = (configuration.startInputOnLaunch ?? (configuration.resumeInputOnLaunch == true && lastRequested)) ? .on : .off
        refreshLoginStatus()
        let loginSettings = NSButton(title:"macOSのログイン項目を開く…",target:self,action:#selector(openLoginSettings))
        archiveEnabled.target = self; archiveEnabled.action = #selector(archiveChanged)
        archiveTrash.target = self; archiveTrash.action = #selector(archiveChanged)
        let reverseScroll = StudioToggle(title:"マウスのスクロール方向だけを反転",target:self,action:#selector(reverseScrollChanged(_:)))
        reverseScroll.state = configuration.reversesMouseScroll ? .on : .off
        reverseScrollToggle = reverseScroll
        let typingPriority = StudioToggle(title:"連続入力の反応を優先",target:self,action:#selector(typingPriorityChanged(_:)))
        typingPriority.state = configuration.typingPriority != false ? .on : .off
        typingPriorityToggle = typingPriority
        let settingsPanel = SettingsPane(pages: [
            sectionBox("起動と自動再開",views:[loginToggle,loginStatus,loginSettings,resumeToggle,
                label("起動時の入力をONにすると、ログインやアプリの再起動後に入力を開始します。手動停止は次の起動まで保持します。")]),
            sectionBox("入力と許可",views:[controls,status,NSButton(title:"最近の動作状況を確認…",target:self,action:#selector(showRecentStatus)),space,
                label("一時的なスリープ・保護された入力からは自動復帰します。緊急停止：Control＋Option＋Command＋Escape")]),
            sectionBox("マウス",views:[
                NSButton(title:"ボタンを押して割り当て",target:self,action:#selector(openMouseAssignment)),
                row([NSButton(title:"進むボタンを登録…",target:self,action:#selector(registerForwardButton)),NSButton(title:"戻るボタンを登録…",target:self,action:#selector(registerBackButton))]),
                reverseScroll, label("全レイヤー共通。割り当てたホイールActionの方向とトラックパッドは変更しません。")]),
            sectionBox("ウィンドウ操作",views:[windowTarget,label("メニューバーからのActionは使用中のウィンドウを操作します。")]),
            sectionBox("クリップボード",views:[NSButton(title:"履歴を開く・記録を設定…",target:self,action:#selector(showClipboardHistory)),label("文字・画像の履歴を検索・再コピーできます。記録は初期OFF、メモリ内に最大50件・合計32MB。終了時に消去します。")]),
            sectionBox("バックアップとヘルプ",views:[row([importButton,exportButton]),starterButton,helpButton,
                NSButton(title:"RELYRをアンインストール…",target:self,action:#selector(uninstallApplication)),
                label("読み込みは現在の設定を置き換えます。置き換え前の設定は自動で退避します。サンプル追加は割り当てを変更します。")]),
            sectionBox("キーボードと機能",views:[typingPriority,
                label("入力中は、通常レイヤーの文字キーの長押しだけを待たずに入力します。300ms入力を休めると長押しが復活します。短押しの割り当てとSpace・Controlレイヤーは従来どおりです。"),label("JIS／US配列は画面上部で選択できます。MacのControlレイヤーも使用できます。CapsLockレイヤーはF13入力を使用します。"),
                label("マクロ・ジェスチャーは左のメニューから作成できます。Mac試用版 0.2.0")]),
            sectionBox("入力を無効にするアプリ",views:[label("登録したアプリが前面にある間は、キー変換・マウスレイヤー・ジェスチャーを使わず通常入力を通します。別のアプリに戻ると自動復帰します。"),disabledApps,
                row([NSButton(title:"アプリを追加…",target:self,action:#selector(addDisabledApp)),NSButton(title:"選択したアプリを解除",target:self,action:#selector(removeDisabledApp))])]),
            sectionBox("自動解凍",views:[archiveEnabled,archiveStatus,label("監視フォルダー"),archiveWatch,
                NSButton(title:"監視フォルダーを選択…",target:self,action:#selector(selectArchiveWatch)),label("解凍後の保存先"),archiveDestination,
                NSButton(title:"保存先を選択…",target:self,action:#selector(selectArchiveDestination)),archiveTrash,
                label("ONにした後に追加されたファイルが対象です。ZIP・7z・RAR・tar・gzip等に対応します（暗号化や一部の圧縮方式は非対応）。同名フォルダーは上書きせず、連番を付けます。解凍に失敗したファイルは削除しません。")])])
        workspace = WorkspaceController(profileControl: profilePicker, profiles: profilePanel, settings: settingsPanel,
            mappingList: scroll, preferencesURL: store.url.deletingLastPathComponent().appendingPathComponent("editor-library.json"))
        workspace.commit = { [weak self] candidate in self?.persist(candidate) ?? false }
        workspace.onSelectMapping = { [weak self] id, selectedLayer, code in
            guard let self = self else { return }
            self.editedProfileID = id
            self.profilePicker.selectItem(at: self.profileIDs.firstIndex(of: id) ?? 0)
            self.layer.selectItem(at: Layer.allCases.firstIndex(of: selectedLayer)!)
            self.input.selectItem(at: Keys.inputs.firstIndex { $0.code == code } ?? 0)
            self.table.reloadData()
            if let index = self.editedMappings.firstIndex(where: { $0.layer == selectedLayer && $0.input == code }) {
                self.table.selectRowIndexes(IndexSet(integer: index), byExtendingSelection: false)
            } else { self.table.deselectAll(nil) }
        }
        workspace.keyboard.onMouseLayout = { [weak self] in self?.showMouseLayoutEditor() }
        workspace.keyboard.onCaptureMouse = { [weak self] in self?.beginInlineMouseCapture() }
        workspace.onCancelMouseCapture = { [weak self] in self?.finishInlineMouseCapture() }
        engine.onSwitchProfile = { [weak self] id in
            guard let self = self, id == "default" || self.configuration.profiles.contains(where: { $0.id == id }) else { return false }
            var candidate = self.configuration; candidate.manualProfileID = id; candidate.automaticProfiles = false
            return self.persist(candidate)
        }
        workspace.actionIssue = { [weak self] mapping in self?.issue(for:mapping) }
        workspace.onInspectAction = { [weak self] mapping in self?.inspectAction(mapping) }
        engine.onResourceResult = { [weak self] kind,value,error in
            guard let self = self else { return }
            if kind == .launch && error == nil {
                let name = URL(fileURLWithPath:value).deletingPathExtension().lastPathComponent
                let message = name.isEmpty ? "アプリを開きました" : "\(name)を開きました"
                self.deck.showNotice(message)
                self.additionalDecks.values.forEach { $0.showNotice(message) }
            }
            let key = self.resourceKey(kind,value)
            guard self.resourceIssues[key] != error else { return }
            if error == nil { self.resourceIssues.removeValue(forKey:key) }
            else {
                if self.resourceIssues.count >= 100 { self.resourceIssues.removeAll() }
                self.resourceIssues[key] = error
            }
            self.workspace.refreshActionIssues()
        }
        engine.onMacroProgress = { [weak self] progress in self?.workspace.updateMacroProgress(progress) }
        workspace.onPlayMacro = { [weak self] id in self?.engine.playMacroFromEditor(id) }
        workspace.onStopMacro = { [weak self] in self?.engine.stopMacro() }
        workspace.onRecordMacro = { [weak self] id,movement,relative,mapped in self?.engine.beginMacroRecording(id,movement:movement,relative:relative,mapped:mapped) ?? false }
        workspace.onStopRecording = { [weak self] in self?.engine.finishMacroRecording() }
        engine.onRecordingProgress = { [weak self] count in
            self?.workspace.updateRecordingCount(count)
            self?.statusItem?.button?.title = "RELYR ● 記録 \(count)"
        }
        engine.onMacroRecorded = { [weak self] id,steps in
            self?.workspace.acceptMacroRecording(id,steps:steps)
            self?.statusItem?.button?.title = self?.engine.enabled == true ? "RELYR ●" : "RELYR ○"
        }
        workspace.keyboard.onMouseLayerButtons = { [weak self] in self?.registerMouseButton() }
        workspace.onShowStatus = { [weak self] in self?.showRecentStatus() }
        workspace.onStopToggle = { [weak self] in self?.toggleInput() }
        workspace.onToggleDeckLayout = { [weak self] id in self?.toggleDeckLayout(id) }
        workspace.onToggleDeck = { [weak self] in self?.toggleDeck() }
        workspace.onChooseDeckColor = { [weak self] ids in self?.chooseDeckColor(ids) }
        workspace.onChooseDeckIcon = { [weak self] ids in self?.chooseDeckIcon(ids) }
        workspace.onRenameDeck = { [weak self] id in self?.renameDeck(id) }
        workspace.onEditDeck = { [weak self] id, edit in self?.editDeck(id, edit: edit) }
        workspace.onSave = { [weak self] in
            guard let self = self else { return }
            if self.persist(self.configuration) { self.workspace.setStatus("保存しました", enabled: self.engine.enabled, requested:self.engine.requested) }
        }
        workspace.onStatus = { [weak self] message in
            guard let self = self else { return }; self.workspace.setStatus(message, enabled: self.engine.enabled, requested:self.engine.requested)
        }
        workspace.onTheme = { [weak self] appearance in self?.deck.panel.appearance = appearance; self?.additionalDecks.values.forEach { $0.panel.appearance = appearance } }
        window.contentView = workspace.view
        workspace.applySavedTheme()
        workspace.setStatus(status.stringValue, enabled: engine.enabled, requested:engine.requested)
        actionChanged()
    }

    @objc private func addDisabledApp() {
        let picker = NSOpenPanel(); picker.allowedContentTypes = [.applicationBundle]; picker.directoryURL = URL(fileURLWithPath:"/Applications")
        picker.beginSheetModal(for:window) { [weak self] response in
            guard let self = self,response == .OK,let url = picker.url,let id = Bundle(url:url)?.bundleIdentifier else { return }
            var candidate = self.configuration; var apps = candidate.disabledApplications ?? []
            guard !apps.contains(where: { $0.bundleIdentifier == id }) else { return }
            apps.append(DisabledApplication(name:url.deletingPathExtension().lastPathComponent,bundleIdentifier:id)); candidate.disabledApplications = apps
            _ = self.persist(candidate)
        }
    }
    @objc private func removeDisabledApp() {
        var candidate = configuration; var apps = candidate.disabledApplications ?? []
        guard apps.indices.contains(disabledApps.indexOfSelectedItem) else { return }
        apps.remove(at:disabledApps.indexOfSelectedItem); candidate.disabledApplications = apps; _ = persist(candidate)
    }
    @objc private func archiveChanged() {
        var candidate = configuration; var settings = candidate.archiveSettings ?? ArchiveSettings()
        settings.enabled = archiveEnabled.state == .on; settings.trashAfterExtraction = archiveTrash.state == .on
        candidate.archiveSettings = settings; if !persist(candidate) { refreshConfigurationViews() }
    }
    @objc private func selectArchiveWatch() { chooseArchiveFolder(watch:true) }
    @objc private func selectArchiveDestination() { chooseArchiveFolder(watch:false) }
    private func chooseArchiveFolder(watch:Bool) {
        let picker = NSOpenPanel(); picker.canChooseFiles = false; picker.canChooseDirectories = true
        picker.beginSheetModal(for:window) { [weak self] response in
            guard let self = self,response == .OK,let url = picker.url else { return }
            var candidate = self.configuration; var settings = candidate.archiveSettings ?? ArchiveSettings()
            if watch { settings.watchFolder = url.path } else { settings.destinationFolder = url.path }
            candidate.archiveSettings = settings; _ = self.persist(candidate)
        }
    }

    private func studioPanel(_ title: String, spacing:CGFloat = 22, views: [NSView]) -> NSView {
        let heading = Studio.label(title, 24)
        let stack = NSStackView(views: [heading] + views)
        stack.orientation = .vertical; stack.alignment = .leading; stack.spacing = spacing
        stack.translatesAutoresizingMaskIntoConstraints = false
        let document = StudioSurface(); document.addSubview(stack)
        let scroll = NSScrollView(); scroll.documentView = document; scroll.hasVerticalScroller = true; scroll.drawsBackground = false
        document.translatesAutoresizingMaskIntoConstraints = false
        NSLayoutConstraint.activate([
            document.widthAnchor.constraint(equalTo: scroll.contentView.widthAnchor),
            stack.topAnchor.constraint(equalTo: document.topAnchor, constant: 12),
            stack.leadingAnchor.constraint(equalTo: document.leadingAnchor, constant: 12),
            stack.trailingAnchor.constraint(equalTo: document.trailingAnchor, constant: -12),
            stack.bottomAnchor.constraint(equalTo: document.bottomAnchor, constant: -24)
        ])
        for item in stack.arrangedSubviews { item.widthAnchor.constraint(equalTo:stack.widthAnchor).isActive = true }
        return scroll
    }

    private func sectionBox(_ title: String, views: [NSView]) -> NSView {
        let box = StudioSurface(); box.isPanel = true
        let stack = NSStackView(views:[Studio.label(title,16,true)] + views)
        stack.orientation = .vertical; stack.alignment = .leading; stack.spacing = 12
        stack.translatesAutoresizingMaskIntoConstraints = false; box.addSubview(stack)
        NSLayoutConstraint.activate([
            stack.topAnchor.constraint(equalTo:box.topAnchor,constant:16),
            stack.bottomAnchor.constraint(equalTo:box.bottomAnchor,constant:-16),
            stack.leadingAnchor.constraint(equalTo:box.leadingAnchor,constant:16),
            stack.trailingAnchor.constraint(equalTo:box.trailingAnchor,constant:-16)])
        for item in stack.arrangedSubviews { item.widthAnchor.constraint(lessThanOrEqualTo:stack.widthAnchor).isActive = true }

        return box
    }

    @objc private func showClipboardHistory() { clipboardHistory.show() }

    @objc private func windowTargetChanged(_ sender: NSPopUpButton) {
        var candidate = configuration
        candidate.windowTarget = WindowTargetMode.allCases[sender.indexOfSelectedItem]
        if !persist(candidate) { sender.selectItem(at: WindowTargetMode.allCases.firstIndex(of: configuration.windowTarget ?? .active) ?? 0) }
    }

    private func label(_ title: String) -> NSTextField { NSTextField(wrappingLabelWithString: title) }
    private func row(_ views: [NSView]) -> NSStackView {
        let stack = NSStackView(views: views)
        stack.orientation = .horizontal; stack.alignment = .centerY; stack.spacing = 10
        return stack
    }

    func numberOfRows(in tableView: NSTableView) -> Int { editedMappings.count }
    func tableView(_ tableView: NSTableView, viewFor tableColumn: NSTableColumn?, row: Int) -> NSView? {
        NSTextField(labelWithString: editedMappings[row].description(layout: configuration.keyboardLayout))
    }
    func tableViewSelectionDidChange(_ notification: Notification) {
        guard editedMappings.indices.contains(table.selectedRow) else { return }
        let mapping = editedMappings[table.selectedRow]
        workspace?.selectMappingFromList(mapping)
        layer.selectItem(at: Layer.allCases.firstIndex(of: mapping.layer)!)
        input.selectItem(at: Keys.inputs.firstIndex { $0.code == mapping.input } ?? 0)
        action.selectItem(at: ActionType.allCases.firstIndex(of: mapping.action) ?? 0)
        output.selectItem(at: Keys.outputs.firstIndex { $0.code == mapping.output } ?? 0)
        for (button, value) in zip(modifierButtons, modifierValues) { button.state = mapping.modifiers.contains(value) ? .on : .off }
        applicationPath = mapping.application
        application.stringValue = applicationPath.isEmpty ? "アプリ未選択" : applicationPath
        payload = mapping.payload; payloadSummary.stringValue = payload.isEmpty ? "未設定" : payload.replacingOccurrences(of: "\n", with: " ↵ ")
        actionChanged()
    }

    @objc private func actionChanged() {
        let kind = ActionType.allCases[action.indexOfSelectedItem]
        output.isEnabled = kind.isKey
        for button in modifierButtons { button.isEnabled = kind == .shortcut }
        chooseApp.isEnabled = kind == .launch
        payloadControls.isHidden = kind != .text && kind != .url
    }
    @objc private func newMapping() {
        table.deselectAll(nil)
        applicationPath = ""; application.stringValue = "アプリ未選択"
        payload = ""; payloadSummary.stringValue = "未設定"
        for button in modifierButtons { button.state = .off }
    }
    @objc private func saveMapping() {
        guard !loadFailed else { return }
        var candidate = configuration
        let kind = ActionType.allCases[action.indexOfSelectedItem]
        var modifiers: Modifiers = []
        if kind == .shortcut {
            for (button, value) in zip(modifierButtons, modifierValues) where button.state == .on { modifiers.insert(value) }
        }
        let mapping = Mapping(layer: Layer.allCases[layer.indexOfSelectedItem],
            input: Keys.inputs[input.indexOfSelectedItem].code, action: kind,
            output: Keys.outputs[output.indexOfSelectedItem].code, modifiers: modifiers, application: applicationPath, payload: payload, trigger: editedMappings.indices.contains(table.selectedRow) ? editedMappings[table.selectedRow].trigger : .tap)
        if kind == .launch, !FileManager.default.fileExists(atPath: applicationPath) {
            showError(ConfigurationError.invalid("アプリを選択してください。移動・削除されたアプリは再選択が必要です。")); return
        }
        candidate.replaceMapping(mapping, at: table.selectedRow >= 0 ? table.selectedRow : nil, profileID: editedProfileID)
        persist(candidate)
    }
    @objc private func deleteMapping() {
        guard !loadFailed, editedMappings.indices.contains(table.selectedRow) else { return }
        var candidate = configuration
        candidate.removeMapping(at: table.selectedRow, profileID: editedProfileID)
        persist(candidate)
    }
    @objc private func spaceChanged() {
        var candidate = configuration
        candidate.spaceEnabled = space.state == .on
        persist(candidate)
        space.state = configuration.spaceEnabled ? .on : .off
    }
    @discardableResult private func persist(_ candidate: Configuration) -> Bool {
        guard !loadFailed else { return false }
        do {
            var candidate = candidate; try candidate.synchronizeDeckProfiles()
            try store.save(candidate)
            configuration = candidate
            if !smoke { clipboardHistory.setEnabled(configuration.clipboardHistoryEnabled == true) }
            if !smoke { archiveAutomation.apply(configuration.archiveSettings ?? ArchiveSettings()) }
            engine.apply(candidate)
            table.deselectAll(nil)
            refreshConfigurationViews()
            workspace.showSaveResult(true)
            return true
        } catch { workspace.showSaveResult(false); showError(error); return false }
    }

    private func configureDeck(_ controller: DeckController, layoutID: String) {
        controller.historyTitles = { [weak self] in (self?.workspace.undoTitle ?? "元に戻す",self?.workspace.redoTitle ?? "やり直す") }
        controller.undoEdit = { [weak self] in self?.workspace.undo() }
        controller.redoEdit = { [weak self] in self?.workspace.redo() }
        controller.canUndoEdit = { [weak self] in self?.workspace.canUndo ?? false }
        controller.canRedoEdit = { [weak self] in self?.workspace.canRedo ?? false }
        controller.inspectAction = { [weak self] mapping in self?.inspectAction(mapping) }
        controller.performAction = { [weak self] mapping in self?.engine.performFromDeck(mapping) }
        controller.execute = { [weak self] id in
            guard let self = self, let item = self.configuration.deck.first(where: { $0.id == id }),
                  let mapping = self.configuration.mapping(for: item) else { return }
            self.engine.performFromDeck(mapping)
        }
        controller.receiveEmptyAction = { [weak self] json, slot in self?.workspace.receiveDeckAction(json,layout:layoutID,slot:slot) ?? false }
        controller.receiveEmptyFile = { [weak self] url, slot in self?.workspace.receiveDeckFile(url,layout:layoutID,slot:slot) ?? false }
        controller.moveToSlot = { [weak self] ids, anchor, slot in self?.workspace.moveDeckItems(ids,layout:layoutID,anchor:anchor,slot:slot) ?? false }
        controller.receiveFile = { [weak self] url, destination in self?.workspace.receiveDeckFile(url,to:destination) ?? false }
        controller.receiveAction = { [weak self] json, destination in
            guard let self = self, let item = self.configuration.deck.first(where: { $0.id == destination }) else { return false }
            return self.workspace.receiveDrop(json,key:item.input,deckID:destination)
        }
        controller.editMany = { [weak self] ids, edit in _ = self?.workspace.editDeckItems(ids,edit:edit) }
        controller.edit = { [weak self] id, edit in self?.editDeck(id, edit: edit) }
        controller.requestColor = { [weak self] ids in self?.chooseDeckColor(ids) }
        controller.requestIcon = { [weak self] ids in self?.chooseDeckIcon(ids) }
        controller.requestRename = { [weak self] id in self?.renameDeck(id) }
        controller.layoutID = layoutID
        controller.layoutChanged = { [weak self] layout in
            guard let self = self,!self.loadFailed,self.configuration.deckLayoutIDs.contains(layout.id),self.configuration.deckLayout(layout.id) != layout else { return }
            var candidate = self.configuration; candidate.setDeckLayout(layout); _ = self.persist(candidate)
        }
    }
    private func toggleDeckLayout(_ requested: String) {
        let profile = configuration.activeProfileID(bundleIdentifier:NSWorkspace.shared.frontmostApplication?.bundleIdentifier)
        guard let id = configuration.resolvedDeck(requested,profile:profile) else { return }
        if id == "default" { deck.toggle() } else { additionalDecks[id]?.toggle() }
    }
    private func updateDecks() {
        let profile = configuration.activeProfileID(bundleIdentifier:NSWorkspace.shared.frontmostApplication?.bundleIdentifier)
        var transfers: [(String,Bool)] = []
        for (id,controller) in [("default",deck)] + Array(additionalDecks) where controller.isVisible {
            if let target = configuration.resolvedDeck(id,profile:profile),target != id {
                transfers.append((target,controller.isCollapsed)); controller.hide()
            }
        }
        deck.update(configuration,enabled:engine.enabled)
        let ids = Set(configuration.deckLayoutIDs.filter { $0 != "default" })
        for id in Array(additionalDecks.keys) where !ids.contains(id) { additionalDecks.removeValue(forKey:id)?.hide() }
        for id in ids {
            if additionalDecks[id] == nil { let controller = DeckController(); configureDeck(controller,layoutID:id); additionalDecks[id] = controller }
            additionalDecks[id]?.update(configuration,enabled:engine.enabled)
        }
        for (id,collapsed) in transfers { let controller = id == "default" ? deck : additionalDecks[id]; controller?.show(collapsed:collapsed) }
    }
    private func refreshConfigurationViews() {
        typingPriorityToggle?.state = configuration.typingPriority != false ? .on : .off
        reverseScrollToggle?.state = configuration.reversesMouseScroll ? .on : .off
        disabledApps.removeAllItems()
        disabledApps.addItems(withTitles:(configuration.disabledApplications ?? []).map { $0.name + " — " + $0.bundleIdentifier })
        if disabledApps.numberOfItems == 0 { disabledApps.addItem(withTitle:"登録されていません") }
        let archive = configuration.archiveSettings ?? ArchiveSettings()
        archiveEnabled.state = archive.enabled ? .on : .off; archiveTrash.state = archive.trashAfterExtraction ? .on : .off
        archiveWatch.stringValue = archive.watchFolder.isEmpty ? "デスクトップ" : archive.watchFolder
        archiveDestination.stringValue = archive.destinationFolder.isEmpty ? "監視フォルダーと同じ" : archive.destinationFolder
        if !archive.enabled { archiveStatus.stringValue = "自動解凍：OFF" }

        resumeToggle.state = (configuration.startInputOnLaunch ?? (configuration.resumeInputOnLaunch == true && lastRequested)) ? .on : .off
        if !profileIDs.contains(editedProfileID) { editedProfileID = "default" }
        for picker in [profilePicker, manualPicker, profilePagePicker] {
            picker.removeAllItems()
            picker.addItems(withTitles: ["標準"] + configuration.profiles.map { $0.name })
        }
        profilePicker.selectItem(at: profileIDs.firstIndex(of: editedProfileID) ?? 0)
        profilePagePicker.selectItem(at: profileIDs.firstIndex(of: editedProfileID) ?? 0)
        profileSummary.stringValue = "\(configuration.profileName(editedProfileID)) · \(editedMappings.count)件の割り当て"
        profileList.editable = !loadFailed
        profileList.update([(id:"default",name:"標準")] + configuration.profiles.map { (id:$0.id,name:$0.name) },selectedID:editedProfileID)
        manualPicker.selectItem(at: profileIDs.firstIndex(of: configuration.manualProfileID) ?? 0)
        manualPicker.isEnabled = !configuration.automaticProfiles && !loadFailed
        automatic.state = configuration.automaticProfiles ? .on : .off
        space.state = configuration.spaceEnabled ? .on : .off
        let ids = configuration.profiles.first { $0.id == editedProfileID }?.bundleIdentifiers ?? []
        profileApplications.update(ids:ids,editable:!loadFailed && editedProfileID != "default")
        profileApplicationsHeight?.constant = editedProfileID == "default" ? 40 : 340
        profileAssociateRow?.isHidden = editedProfileID == "default"
        profileApps.stringValue = "対象アプリ：" + (ids.isEmpty ? (editedProfileID == "default" ? "専用プロファイルのないアプリ" : "なし（手動で使用できます）") : ids.map { id in
            NSWorkspace.shared.urlForApplication(withBundleIdentifier: id)?.deletingPathExtension().lastPathComponent ?? id
        }.joined(separator: "、"))
        profileApps.toolTip = profileApps.stringValue
        for control in editableControls { control.isEnabled = !loadFailed }
        table.reloadData()
        for control in profileSpecificControls { control.isEnabled = !loadFailed && editedProfileID != "default" }
        updateDecks()
        workspace?.update(configuration, profileID: editedProfileID)
        workspace?.setEditable(!loadFailed)
    }

    @objc private func profileChanged() {
        editedProfileID = profileIDs[profilePicker.indexOfSelectedItem]
        table.deselectAll(nil)
        refreshConfigurationViews()
    }
    @objc private func manualProfileChanged() {
        var candidate = configuration
        candidate.manualProfileID = profileIDs[manualPicker.indexOfSelectedItem]
        if !persist(candidate) { refreshConfigurationViews() }
    }
    @objc private func automaticChanged() {
        var candidate = configuration; candidate.automaticProfiles = automatic.state == .on
        if !persist(candidate) { refreshConfigurationViews() }
    }
    private func askName(_ title: String, initial: String, complete: @escaping (String) -> Void) {
        showWindow()
        let alert = NSAlert(); alert.messageText = title
        let field = NSTextField(string: initial)
        field.frame = NSRect(x: 0, y: 0, width: 320, height: 24)
        alert.accessoryView = field
        alert.addButton(withTitle: "保存"); alert.addButton(withTitle: "キャンセル")
        alert.beginSheetModal(for: window) { response in
            if response == .alertFirstButtonReturn { complete(field.stringValue.trimmingCharacters(in: .whitespacesAndNewlines)) }
        }
        alert.window.initialFirstResponder = field
    }
    @objc private func profilePageChanged() {
        guard profileIDs.indices.contains(profilePagePicker.indexOfSelectedItem) else { return }
        editedProfileID = profileIDs[profilePagePicker.indexOfSelectedItem]; refreshConfigurationViews()
    }
    @objc private func editProfileMappings() { workspace.showLayer(.normal) }
    @objc private func useEditedProfile() {
        var candidate = configuration; candidate.manualProfileID = editedProfileID; candidate.automaticProfiles = false
        _ = persist(candidate)
    }
    @objc private func createProfile() {
        guard !loadFailed else { return }
        var name = newProfileName.stringValue.trimmingCharacters(in:.whitespacesAndNewlines)
        if name.isEmpty {
            var index = 1
            repeat { name = "新しいプロファイル \(index)"; index += 1 } while configuration.profiles.contains(where:{ $0.name == name })
        }
        var candidate = configuration
        let profile = Profile(name:name,mappings:[]); candidate.profiles.append(profile)
        if persist(candidate) { editedProfileID = profile.id; newProfileName.stringValue = ""; refreshConfigurationViews() }
    }
    @objc private func duplicateProfile() { makeProfile(copy: true) }
    private func makeProfile(copy: Bool) {
        guard !loadFailed else { return }
        let mappings = copy ? editedMappings : []
        askName(copy ? "プロファイルを複製" : "プロファイルを作成", initial: "新しいプロファイル") { name in
            var candidate = self.configuration
            let profile = Profile(name: name, mappings: mappings)
            candidate.profiles.append(profile)
            if self.persist(candidate) { self.editedProfileID = profile.id; self.refreshConfigurationViews() }
        }
    }
    @objc private func renameProfile() {
        let id = editedProfileID
        guard id != "default", !loadFailed else { return }
        askName("プロファイル名", initial: configuration.profileName(id)) { name in
            var candidate = self.configuration
            if let index = candidate.profiles.firstIndex(where: { $0.id == id }) {
                candidate.profiles[index].name = name; self.persist(candidate)
            }
        }
    }
    @objc private func deleteProfile() {
        let id = editedProfileID
        guard id != "default", !loadFailed else { return }
        let alert = NSAlert()
        alert.messageText = "「\(configuration.profileName(id))」を削除しますか？"
        alert.informativeText = "このプロファイルの割り当てと、関連するDeckボタンを削除します。"
        alert.addButton(withTitle: "削除"); alert.addButton(withTitle: "キャンセル")
        alert.beginSheetModal(for: window) { response in
            if response == .alertFirstButtonReturn {
                var candidate = self.configuration; candidate.removeProfile(id); self.persist(candidate)
            }
        }
    }
    @objc private func associateApplication() {
        let id = editedProfileID
        guard !loadFailed else { return }
        guard id != "default" else { showError(ConfigurationError.invalid("対象アプリを指定するにはプロファイルを新規作成してください。")); return }
        let panel = NSOpenPanel(); panel.allowedContentTypes = [.applicationBundle]
        panel.directoryURL = URL(fileURLWithPath: "/Applications"); panel.allowsMultipleSelection = false
        panel.beginSheetModal(for: window) { response in
            guard response == .OK, let url = panel.url, let bundle = Bundle(url: url)?.bundleIdentifier else { return }
            var candidate = self.configuration
            guard let index = candidate.profiles.firstIndex(where: { $0.id == id }) else { return }
            if !candidate.profiles[index].bundleIdentifiers.contains(bundle) { candidate.profiles[index].bundleIdentifiers.append(bundle) }
            self.persist(candidate)
        }
    }
    @objc private func clearApplications() {
        guard !loadFailed else { return }
        var candidate = configuration
        if let index = candidate.profiles.firstIndex(where: { $0.id == editedProfileID }) {
            candidate.profiles[index].bundleIdentifiers = []; persist(candidate)
        }
    }
    @objc private func addStarter() {
        guard !loadFailed else { return }
        var candidate = configuration
        var values = editedMappings
        let starter: [(UInt16, UInt16)] = [(0, 123), (2, 124), (13, 126), (1, 125)]
        for (input, output) in starter {
            if !values.contains(where: { $0.layer == .space && $0.input == input }) {
                values.append(Mapping(layer: .space, input: input, action: .key, output: output))
            }
        }
        candidate.setMappings(values, for: editedProfileID)
        persist(candidate)
    }
    @objc private func addToDeck() {
        guard !loadFailed, editedMappings.indices.contains(table.selectedRow) else { return }
        let mapping = editedMappings[table.selectedRow], profileID = editedProfileID
        askName("Deckボタンの名前", initial: mapping.targetDescription(layout: configuration.keyboardLayout)) { name in
            var candidate = self.configuration
            candidate.deck.append(DeckItem(title: name, profileID: profileID, layer: mapping.layer, input: mapping.input))
            self.persist(candidate)
        }
    }
    @objc private func toggleDeck() {
        let profile = configuration.activeProfileID(bundleIdentifier:NSWorkspace.shared.frontmostApplication?.bundleIdentifier)
        if let id = configuration.resolvedDeck(nil,profile:profile) { toggleDeckLayout(id) }
    }
    private func chooseDeckColor(_ ids: [String]) {
        guard !loadFailed,!ids.isEmpty else { return }
        showWindow()
        let alert = NSAlert(); alert.messageText = "Deckボタンの色"; alert.informativeText = "色見本を押して色を選んでください。選択したボタンすべてに適用します。"
        let well = NSColorWell(frame:NSRect(x:0,y:0,width:180,height:42)); well.color = configuration.deck.first(where:{ ids.contains($0.id) }).flatMap { Studio.color($0.colorHex) } ?? Studio.accent
        alert.accessoryView = well; alert.addButton(withTitle:"適用"); alert.addButton(withTitle:"キャンセル")
        alert.beginSheetModal(for:window) { [weak self] response in
            well.deactivate()
            guard let self = self,response == .alertFirstButtonReturn,let color = well.color.usingColorSpace(.deviceRGB) else { return }
            let hex = String(format:"%02X%02X%02X",Int(round(max(0,min(1,color.redComponent))*255)),Int(round(max(0,min(1,color.greenComponent))*255)),Int(round(max(0,min(1,color.blueComponent))*255)))
            _ = self.workspace.editDeckItems(ids,edit:.color(hex))
        }
    }
    private func chooseDeckIcon(_ ids: [String]) {
        guard !loadFailed else { return }
        showWindow()
        let picker = NSOpenPanel()
        // A broad image type keeps the Open panel from selecting one extension
        // (for example ICO) as its only visible format. NSImage validates the
        // actual file after selection, and app bundles use their Finder icon.
        picker.allowedContentTypes = [.image, .applicationBundle]
        picker.message = "Deckアイコンに使う画像を選択（PNG・JPEG・HEIC・TIFF・ICO・ICNS・アプリなど）"
        picker.canChooseDirectories = false; picker.allowsMultipleSelection = false
        picker.beginSheetModal(for:window) { [weak self] response in
            guard let self = self,response == .OK,let url = picker.url else { return }
            let original = url.pathExtension.lowercased() == "app" ? NSWorkspace.shared.icon(forFile:url.path) : NSImage(contentsOf:url)
            guard let original = original,original.isValid else {
                self.workspace.setStatus("画像を読み込めませんでした。PNG・JPEG・HEIC・TIFF・ICO・ICNSを選んでください。",enabled:self.engine.enabled)
                return
            }
            let image = NSImage(size:NSSize(width:64,height:64)); image.lockFocus()
            original.draw(in:NSRect(x:0,y:0,width:64,height:64),from:.zero,operation:.sourceOver,fraction:1)
            image.unlockFocus()
            guard let tiff = image.tiffRepresentation,let bitmap = NSBitmapImageRep(data:tiff),let png = bitmap.representation(using:.png,properties:[:]) else { return }
            _ = self.workspace.editDeckItems(ids,edit:.icon(png))
        }
    }
    private func renameDeck(_ id: String) {
        guard let item = configuration.deck.first(where: { $0.id == id }) else { return }
        showWindow()
        askName("Deckボタンの名前", initial: item.title) { self.editDeck(id, edit: .rename($0)) }
    }
    private func editDeck(_ id: String, edit: DeckEdit) {
        guard !loadFailed else { return }
        _ = workspace.editDeckItem(id,edit:edit)
    }
    @objc private func exportSettings() {
        guard !loadFailed else { return }
        let panel = NSSavePanel(); panel.allowedContentTypes = [.json]
        panel.nameFieldStringValue = "RELYR-Mac-settings.json"
        panel.beginSheetModal(for: window) { response in
            guard response == .OK, let url = panel.url else { return }
            do { try ConfigurationStore.encoded(self.configuration).write(to: url, options: .atomic) }
            catch { self.showError(error) }
        }
    }
    @objc private func importSettings() {
        let panel = NSOpenPanel(); panel.allowedContentTypes = [.json]; panel.allowsMultipleSelection = false
        panel.beginSheetModal(for: window) { response in
            guard response == .OK, let url = panel.url else { return }
            do {
                let imported = try ConfigurationStore.read(url)
                let alert = NSAlert(); alert.messageText = "設定を読み込みますか？"
                alert.informativeText = "\(imported.profiles.count + 1)プロファイル・\(imported.deck.count)個のDeckボタンで現在の設定を置き換えます。元の設定はバックアップし、入力変換は停止します。Windows版の設定は読み込めません。"
                alert.addButton(withTitle: "読み込む"); alert.addButton(withTitle: "キャンセル")
                alert.beginSheetModal(for: self.window) { response in
                    guard response == .alertFirstButtonReturn else { return }
                    do {
                        self.engine.stop()
                        self.configuration = try self.store.importConfiguration(imported)
                        self.loadFailed = false; self.editedProfileID = "default"
                        self.engine.apply(self.configuration); self.table.deselectAll(nil); self.refreshConfigurationViews()
                        self.workspace.setStatus("設定を読み込みました。入力は停止中です。確認後、左下から有効にできます。",enabled:false)
                        let completion = NSAlert()
                        completion.messageText = "設定を読み込みました"
                        completion.informativeText = "新しい割り当てが意図せず動かないよう、入力変換を停止しています。すぐ使う場合は「有効にする」を押してください。設定を先に確認する場合は、確認後に左下の「入力停止中」から有効にできます。"
                        completion.addButton(withTitle:"有効にする")
                        completion.addButton(withTitle:"停止したまま設定を確認")
                        completion.beginSheetModal(for:self.window) { response in
                            guard response == .alertFirstButtonReturn, !self.engine.requested else { return }
                            self.toggleInput()
                        }
                    } catch { self.showError(error) }
                }
            } catch { self.showError(error) }
        }
    }
    @objc private func uninstallApplication() {
        guard !smoke, !uninstalling else { return }
        showWindow()
        let alert = NSAlert()
        alert.messageText = "RELYRをアンインストールしますか？"
        alert.informativeText = "入力変換を停止し、ログイン時の自動起動を解除します。このアプリと、設定・プロファイル・マクロ・Deck・内部バックアップ・キャッシュをゴミ箱へ移動して終了します。\n\n書き出した設定、ダウンロードしたZIP、登録したアプリやファイル、解凍済みファイルは削除しません。必要な設定は先に書き出してください。\n\nmacOSが管理するアクセス許可や診断履歴は削除対象外です。ゴミ箱は自動で空にしません。"
        alert.addButton(withTitle:"キャンセル")
        alert.addButton(withTitle:"アンインストール")
        alert.beginSheetModal(for:window) { [weak self] response in
            guard response == .alertSecondButtonReturn, let self = self else { return }
            do {
                let app = Bundle.main.bundleURL
                guard app.pathExtension == "app", Bundle.main.bundleIdentifier == UninstallPlan.bundleID else {
                    throw NSError(domain:"RELYR.Uninstall",code:1,userInfo:[NSLocalizedDescriptionKey:"アプリ本体を確認できません。"])
                }
                // Cancel captures first; they may otherwise restore the previous input intent.
                self.captureResume = false; self.modalCaptureResume = false
                self.finishModalMouseCapture(); self.finishInlineMouseCapture()
                self.engine.stop()
                if SMAppService.mainApp.status != .notRegistered {
                    try SMAppService.mainApp.unregister()
                }
                // Persist OFF before removal, so a failed removal cannot re-register on relaunch.
                var stopped = self.configuration
                stopped.loginLaunchDesired = false; stopped.startInputOnLaunch = false
                stopped.resumeInputOnLaunch = false
                if !self.loadFailed { guard self.persist(stopped) else { return } }
                self.uninstalling = true; self.suppressRuntimeSave = true
                self.permissionTimer?.invalidate(); self.permissionTimer = nil
                self.archiveAutomation.apply(ArchiveSettings())
                let manager = FileManager.default
                let library = manager.urls(for:.libraryDirectory,in:.userDomainMask)[0]
                UserDefaults.standard.removePersistentDomain(forName:UninstallPlan.bundleID)
                UserDefaults.standard.synchronize()
                let data = UninstallPlan.dataURLs(library:library)
                try UninstallPlan.moveExisting(data, exists:{ manager.fileExists(atPath:$0.path) }, move:{ url in
                    try manager.trashItem(at:url,resultingItemURL:nil)
                })
                // Move only the executing bundle, never other copies or the containing folder.
                UserDefaults.standard.setVolatileDomain(["ApplePersistenceIgnoreState":true],forName:UserDefaults.argumentDomain)
                try manager.trashItem(at:app,resultingItemURL:nil)
                NSApp.terminate(nil)
            } catch {
                let failure = NSAlert(); failure.messageText = "アンインストールを完了できませんでした"
                failure.informativeText = "入力は停止しています。移動済みの項目はゴミ箱にあります。残った項目の確認後、もう一度実行してください。\n" + error.localizedDescription
                failure.addButton(withTitle:"閉じる")
                failure.beginSheetModal(for:self.window) { _ in
                    // Do not let a partially removed configuration be silently recreated.
                    if self.uninstalling { NSApp.terminate(nil) }
                }
            }
        }
    }
    @objc private func showHelp() {
        let alert = NSAlert(); alert.messageText = "最初のテスト"
        alert.informativeText = "1.「サンプル追加」を押します。\n2. アクセシビリティを許可して「有効にする」を押します。\n3. テキストエディットでSpace単押しとSpace+A/D/W/Sを試します。\n\nプロファイルの「編集」は設定対象、「手動使用」は実行対象です。自動切替時は前面アプリに一致するプロファイル、なければ標準を使います。Deckへ追加するには保存済みの行を選択してください。\n\n緊急停止：Control + Option + Command + Escape。"
        alert.beginSheetModal(for: window)
    }
    @objc private func editPayload() {
        let kind = ActionType.allCases[action.indexOfSelectedItem]
        let alert = NSAlert(); alert.messageText = kind == .url ? "開くURL（http / https）" : "入力する文字列"
        alert.informativeText = kind == .url ? "URLを入力してください。" : "改行・日本語・絵文字を入力できます。最大4096 UTF-16文字。送信中に別の操作をすると中断します。"
        let scroll = NSScrollView(frame: NSRect(x: 0, y: 0, width: 420, height: 140))
        let text = NSTextView(frame: scroll.bounds)
        text.isRichText = false; text.isAutomaticQuoteSubstitutionEnabled = false
        text.isAutomaticDashSubstitutionEnabled = false; text.font = .systemFont(ofSize: 13)
        text.string = payload; text.isVerticallyResizable = true; text.isHorizontallyResizable = false
        text.autoresizingMask = [.width]; text.textContainer?.widthTracksTextView = true
        scroll.documentView = text; scroll.hasVerticalScroller = true
        alert.accessoryView = scroll
        alert.addButton(withTitle: "設定"); alert.addButton(withTitle: "キャンセル")
        alert.beginSheetModal(for: window) { response in
            if response == .alertFirstButtonReturn {
                self.payload = kind == .url ? text.string.trimmingCharacters(in: .whitespacesAndNewlines) : text.string
                self.payloadSummary.stringValue = self.payload.isEmpty ? "未設定" : self.payload.replacingOccurrences(of: "\n", with: " ↵ ")
            }
        }
        alert.window.initialFirstResponder = text
    }
    private func showError(_ error: Error) {
        let alert = NSAlert()
        alert.messageText = "設定を保存できません"
        alert.informativeText = error.localizedDescription
        alert.beginSheetModal(for: window)
    }
    @objc private func selectApplication() {
        let panel = NSOpenPanel()
        panel.allowedContentTypes = [.applicationBundle]
        panel.directoryURL = URL(fileURLWithPath: "/Applications")
        panel.canChooseDirectories = false; panel.allowsMultipleSelection = false
        panel.beginSheetModal(for: window) { response in
            if response == .OK, let url = panel.url {
                self.applicationPath = url.path; self.application.stringValue = url.path
            }
        }
    }
    private var mouseLayoutEditor: MouseLayoutEditor?
    private func showMouseLayoutEditor() {
        guard !loadFailed, mouseLayoutEditor == nil, window.attachedSheet == nil else { return }
        let deviceID = workspace.editingMouseID
        let deviceName = deviceID.map {
            MouseDeviceTracker.shared.devices[$0] ?? configuration.mouseDeviceNames?[$0] ?? $0
        } ?? "すべてのマウス（共通）"
        beginModalMouseCapture()
        let editor = MouseLayoutEditor(configuration: configuration, deviceID: deviceID, deviceName: deviceName)
        mouseLayoutEditor = editor
        editor.save = { [weak self] layout in
            self?.workspace.saveMouseLayout(layout, deviceID: deviceID, deviceName: deviceName) ?? false
        }
        editor.startMonitoring()
        window.beginSheet(editor.panel) { [weak self] _ in
            guard let self = self else { return }
            self.mouseLayoutEditor?.stopMonitoring(); self.mouseLayoutEditor = nil
            self.finishModalMouseCapture()
        }
    }
    private var inlineCapture: MouseRegistrationView?
    private var inlineMXMaster4Observer: UUID?
    private var captureResume = false
    private var modalCaptureActive = false
    private var modalCaptureResume = false
    private var captureObserver: NSObjectProtocol?
    private func finishInlineMouseCapture() {
        guard inlineCapture != nil else { return }
        if let observer = inlineMXMaster4Observer { MXMaster4HID.shared.remove(observer); inlineMXMaster4Observer = nil }
        inlineCapture?.removeFromSuperview(); inlineCapture = nil
        if let observer = captureObserver { NotificationCenter.default.removeObserver(observer) }; captureObserver = nil
        window.makeFirstResponder(nil)
        let resume = captureResume; captureResume = false
        if resume { engine.restorePreviousSession() }
        else { engine.stop(message:"ボタン選択を終了しました。入力は停止中です。") }
        suppressRuntimeSave = false; saveRuntimeIntent()
    }
    private func beginInlineMouseCapture() {
        guard !loadFailed, inlineCapture == nil else { return }
        captureResume = engine.requested; suppressRuntimeSave = true
        engine.stop(message:"選択するマウスボタンを押してください。Escでキャンセル。")
        let canvas = workspace.keyboard
        let capture = MouseRegistrationView(frame:canvas.bounds); capture.autoresizingMask = [.width,.height]
        capture.acceptsAllButtons = true
        capture.note = "ここにポインタを置き、割り当てたいマウスボタンを押してください。\n選択中の「\(workspace.currentLayer.title)」に割り当てます。Escでキャンセル。"
        inlineCapture = capture; canvas.addSubview(capture); window.makeFirstResponder(capture)
        var selected: UInt16?
        capture.received = { [weak self, weak capture] button in
            guard let self = self else { return }
            selected = self.configuration.mouseInput(Int64(button),deviceID:self.workspace.editingMouseID)
            if let code = selected { capture?.note = "\(self.configuration.inputName(code))を検出しました。ボタンを離してください。" }
        }
        capture.wheelReceived = { [weak capture] code in selected = code; capture?.released?() }
        MXMaster4HID.shared.onError = { [weak capture] message in capture?.note = message }
        MXMaster4HID.shared.requestPermissionForCapture()
        inlineMXMaster4Observer = MXMaster4HID.shared.observe { [weak capture] down in
            guard let capture = capture else { return }
            if down { selected = 340; capture.note = "MX Master 4 モード切替を検出しました。ボタンを離してください。" }
            else if selected == 340 { capture.released?() }
        }
        capture.released = { [weak self] in
            guard let self = self, let code = selected else { return }
            self.finishInlineMouseCapture()
            self.workspace.selectKey(code)
            self.workspace.openMouseLibrary()
            if InputPolicy.allows(code,layer:self.workspace.currentLayer) || InputPolicy.allows(code,layer:self.workspace.currentLayer,trigger:.hold) {
                self.workspace.setStatus("\(self.configuration.inputName(code))を選択しました。Actionを選択したマウスボタンへドラッグしてください。",enabled:self.engine.enabled, requested:self.engine.requested)
            } else { self.workspace.setStatus("このレイヤーの\(self.configuration.inputName(code))は通常操作のため保護されています。左側で別のレイヤーを選んでください。",enabled:self.engine.enabled, requested:self.engine.requested) }
        }
        capture.cancelled = { [weak self] in self?.finishInlineMouseCapture() }
        captureObserver = NotificationCenter.default.addObserver(forName:NSWindow.didResignKeyNotification,object:window,queue:.main) { [weak self] _ in self?.finishInlineMouseCapture() }
    }

    private func toggleKeypad(_ extended: Bool) {
        if keypad.panel.isVisible && keypadExtended == extended { keypad.toggle(); return }
        if keypad.panel.isVisible { keypad.toggle() }
        keypadExtended = extended
        let codes: [UInt16] = extended ? [115,126,116,71,75,67,123,125,124,89,91,92,119,117,121,86,87,88,53,48,51,83,84,85,36,49,65,82,69,78] : [89,91,92,86,87,88,83,84,85,82,65,76,71,75,67,69,78,51]
        var config = Configuration(); config.deckColumns = extended ? 6 : 3
        config.deck = codes.map { code in DeckItem(id:String(code),title:Keys.name(code),profileID:"default",layer:.normal,input:0,directAction:Mapping(layer:.normal,input:0,action:.key,output:code)) }
        keypad.update(config,enabled:engine.enabled); keypad.panel.title = extended ? "ナビゲーション・テンキー" : "テンキー"; keypad.toggle()
    }

    private func refreshLoginStatus() {
        guard !smoke else { loginToggle.state = .off; loginStatus.stringValue = "自動起動：OFF（検証中は変更しません）"; return }
        let state = SMAppService.mainApp.status
        loginToggle.state = state == .enabled || state == .requiresApproval ? .on : .off
        switch state {
        case .enabled: loginStatus.stringValue = "自動起動：ON"
        case .requiresApproval: loginStatus.stringValue = "自動起動：macOSの許可待ち。「ログイン項目を開く」からRELYRを許可してください。"
        case .notRegistered: loginStatus.stringValue = "自動起動：OFF"
        case .notFound: loginStatus.stringValue = "自動起動：アプリを確認できません。現在のRELYRを再起動してください。"
        @unknown default: loginStatus.stringValue = "自動起動：状態を確認できません。"
        }
    }
    @objc private func loginSettingChanged() {
        guard !smoke else { refreshLoginStatus(); return }
        do {
            if loginToggle.state == .on {
                if SMAppService.mainApp.status != .enabled && SMAppService.mainApp.status != .requiresApproval { try SMAppService.mainApp.register() }
            } else { try SMAppService.mainApp.unregister() }
            var candidate = configuration; candidate.loginLaunchDesired = loginToggle.state == .on
            _ = persist(candidate)
            refreshLoginStatus()
        } catch { refreshLoginStatus(); showError(error) }
    }
    @objc private func openLoginSettings() { if !smoke { SMAppService.openSystemSettingsLoginItems() } }
    @objc private func resumeSettingChanged() {
        var candidate = configuration; candidate.startInputOnLaunch = resumeToggle.state == .on; candidate.resumeInputOnLaunch = false
        if !persist(candidate) { resumeToggle.state = (configuration.startInputOnLaunch ?? (configuration.resumeInputOnLaunch == true && lastRequested)) ? .on : .off }
        saveRuntimeIntent()
    }
    func applicationDidBecomeActive(_ notification: Notification) {
        refreshLoginStatus()
        refreshPermissionEntry()
    }
    private func refreshPermissionEntry() {
        guard !CommandLine.arguments.contains("--ui-smoke-test") else { return }
        workspace?.setPermissionRequired(!AXIsProcessTrusted())
    }

    private func beginModalMouseCapture() {
        finishInlineMouseCapture()
        modalCaptureActive = true; modalCaptureResume = engine.requested
        suppressRuntimeSave = true
        engine.stop(message:"ボタン確認中：一時的に入力変換を止めています。閉じると元の状態へ戻ります。")
    }
    private func finishModalMouseCapture() {
        guard modalCaptureActive else { return }
        modalCaptureActive = false
        let resume = modalCaptureResume; modalCaptureResume = false
        if resume { engine.restorePreviousSession() }
        else { engine.stop() }
        suppressRuntimeSave = false; saveRuntimeIntent()
    }

    @objc private func showRecentStatus() {
        let alert = NSAlert(); alert.messageText = "最近の動作状況"
        alert.informativeText = "直近30件。入力の停止・復帰やActionの結果を表示します。履歴は終了時に消去されます。Actionの送信は、対象アプリでの実行成功を意味しません。"
        let formatter = DateFormatter(); formatter.dateFormat = "HH:mm:ss"
        let text = NSTextView(frame:NSRect(x:0,y:0,width:520,height:260))
        text.isEditable = false; text.isSelectable = true; text.font = .systemFont(ofSize:12)
        text.string = recentStatus.isEmpty ? status.stringValue : recentStatus.reversed().map { formatter.string(from:$0.0) + "  " + $0.1 }.joined(separator:"\n\n")
        let scroll = NSScrollView(frame:text.frame); scroll.hasVerticalScroller = true; scroll.documentView = text
        text.isVerticallyResizable = true; text.autoresizingMask = [.width]
        text.textContainer?.widthTracksTextView = true
        alert.accessoryView = scroll; alert.addButton(withTitle:"閉じる"); alert.beginSheetModal(for:window)
    }

    @objc private func inspectMouseButton() {
        guard !loadFailed else { return }
        beginModalMouseCapture()
        let alert = NSAlert(); alert.messageText = "マウスのボタンを確認・割り当て"
        alert.informativeText = "枠内でボタンを押すと、macOSが受信した番号を表示します。Fn1等の本体表記は機種ごとに違うため、実際に押して確認してください。キーとして届く場合はELECOMアシスタント等のボタン変換をOFFにしてください。"
        alert.addButton(withTitle:"このボタンを編集"); alert.addButton(withTitle:"閉じる")
        alert.buttons[0].isEnabled = false
        let content = NSView(frame:NSRect(x:0,y:0,width:440,height:180))
        let capture = MouseRegistrationView(frame:NSRect(x:0,y:85,width:440,height:95)); capture.acceptsAllButtons = true
        capture.note = "この枠内で左・右・中央・サイド・Fnボタンを押してください。"
        let layers = NSPopUpButton(frame:NSRect(x:0,y:0,width:220,height:30)); layers.addItems(withTitles:Layer.allCases.map { $0.title }); layers.selectItem(at:1)
        let name = NSTextField(frame:NSRect(x:0,y:42,width:440,height:28)); name.placeholderString = "ボタン名（任意：Fn1、親指ボタン等）"
        name.setAccessibilityLabel("マウスボタン名")
        content.addSubview(name); content.addSubview(capture); content.addSubview(layers); alert.accessoryView = content
        var detected: UInt16?
        capture.received = { [weak self, weak alert, weak capture] button in
            guard let self = self, let code = self.configuration.mouseInput(Int64(button),deviceID:self.workspace.editingMouseID) else { capture?.note = "未対応の番号です。"; return }
            detected = code; name.stringValue = self.configuration.mouseButtonNames?[String(code)] ?? ""
            capture?.note = "検出：\(self.configuration.inputName(code)) ／ macOSボタン番号 \(button)\n下で編集するレイヤーを選択できます。"
            alert?.buttons.first?.isEnabled = true
        }
        capture.wheelReceived = { [weak self, weak alert, weak capture] code in
            guard let self = self else { return }
            detected = code; name.stringValue = self.configuration.mouseButtonNames?[String(code)] ?? ""
            capture?.note = "検出：\(self.configuration.inputName(code))"
            alert?.buttons.first?.isEnabled = true
        }
        alert.beginSheetModal(for:window) { [weak self] response in
            guard let self = self else { return }
            defer { self.finishModalMouseCapture() }
            guard response == .alertFirstButtonReturn, let code = detected else { return }
            let title = name.stringValue.trimmingCharacters(in:.whitespacesAndNewlines)
            var candidate = self.configuration
            var names = candidate.mouseButtonNames ?? [:]
            if title.isEmpty { names.removeValue(forKey:String(code)) } else { names[String(code)] = title }
            candidate.mouseButtonNames = names
            guard self.persist(candidate) else { return }
            let chosen = Layer.allCases[layers.indexOfSelectedItem]
            self.workspace.showLayer(chosen); self.workspace.selectKey(code)
            self.workspace.setStatus("\(chosen.title)の\(self.configuration.inputDetail(code))を選択しました。右側のActionから割り当てます。",enabled:false)
        }
        alert.window.makeFirstResponder(capture)
    }

    @objc private func openMouseAssignment() {
        workspace.showLayer(.normal)
        beginInlineMouseCapture()
    }
    @objc private func registerForwardButton() { registerMouseButton(forward:true) }
    @objc private func registerBackButton() { registerMouseButton(forward:false) }
    private func registerMouseButton(forward: Bool = true) {
        guard !loadFailed else { return }
        let connected = MouseDeviceTracker.shared.devices
        let preferred = workspace.editingMouseID ?? MouseDeviceTracker.shared.activeID
        let names = (configuration.mouseDeviceNames ?? [:]).merging(connected) { _, current in current }
        let ids = Set(names.keys).union((configuration.mouseLayerButtonsByDevice ?? [:]).keys).sorted()
        let scopes: [String?] = [nil] + ids.map { Optional($0) }
        let initialScope = scopes.firstIndex(of: preferred) ?? 0

        beginModalMouseCapture()
        let alert = NSAlert()
        alert.messageText = "レイヤーボタンを設定"
        alert.informativeText = "マウスとレイヤーを選び、下の枠内で使いたいサイドボタンを押してください。"
        alert.addButton(withTitle: "登録")
        alert.addButton(withTitle: "キャンセル")
        alert.buttons[0].isEnabled = false

        let body = NSView(frame: NSRect(x: 0, y: 0, width: 360, height: 152))
        let scope = NSPopUpButton(frame: NSRect(x: 0, y: 120, width: 360, height: 26))
        scope.addItems(withTitles: ["すべてのマウス（共通）"] + ids.map { names[$0] ?? $0 })
        scope.selectItem(at: initialScope)
        scope.setAccessibilityLabel("対象マウス")
        let role = NSPopUpButton(frame: NSRect(x: 0, y: 90, width: 360, height: 26))
        role.addItems(withTitles: ["進むレイヤー", "戻るレイヤー"])
        role.selectItem(at: forward ? 0 : 1)
        role.setAccessibilityLabel("対象レイヤー")
        let capture = MouseRegistrationView(frame: NSRect(x: 0, y: 0, width: 360, height: 84))
        body.addSubview(scope)
        body.addSubview(role)
        body.addSubview(capture)
        var detected: Int?
        capture.received = { [weak capture, weak alert] button in
            detected = button
            capture?.note = "ボタン \(button) を検出しました。"
            alert?.buttons.first?.isEnabled = true
        }
        alert.accessoryView = body
        alert.beginSheetModal(for: window) { [weak self] response in
            guard let self = self else { return }
            defer { self.finishModalMouseCapture() }
            guard response == .alertFirstButtonReturn, let detected = detected,
                  scopes.indices.contains(scope.indexOfSelectedItem) else { return }
            let deviceID = scopes[scope.indexOfSelectedItem]
            let forward = role.indexOfSelectedItem == 0
            var candidate = self.configuration
            candidate.registerMouseButton(detected, forward: forward, deviceID: deviceID)
            if let deviceID = deviceID, let name = names[deviceID] {
                candidate.mouseDeviceNames = candidate.mouseDeviceNames ?? [:]
                candidate.mouseDeviceNames?[deviceID] = name
            }
            if self.persist(candidate) {
                self.workspace.selectMouseScope(deviceID)
                self.workspace.showLayer(forward ? .mouseForward : .mouseBack)
                self.workspace.setStatus("ボタン \(detected) を「\(forward ? "進む" : "戻る")」レイヤーに登録しました。", enabled: false)
            }
        }
        alert.window.makeFirstResponder(capture)
    }

    private var permissionTimer: Timer?
    private var permissionNotice: NSAlert?
    private var permissionResumeRequested = false
    private weak var permissionStateLabel: NSTextField?
    @objc private func revealPermissionApp() {
        NSWorkspace.shared.activateFileViewerSelecting([Bundle.main.bundleURL])
    }
    @objc private func showPermissionTroubleshooting() {
        permissionNotice?.informativeText = "ONなのに未確認の場合\n1. macOS側のRELYRを一度OFFにして、もう一度ONにします。\n2. 改善しない場合は、古いRELYRの項目を選び「−」で外します。\n3.「このアプリをFinderで表示」で表示したアプリを、許可一覧へドラッグしてONにします。\n\n一覧にない場合は3から進めてください。「−」が使えない場合は、まずOFF→ONを試してください。macOSから終了・再起動を求められた場合は、その案内に従ってください。"
        permissionNotice?.layout()
    }
    @objc private func showPermissionSetup() {
        guard permissionNotice == nil else { return }
        let alert = NSAlert()
        alert.messageText = "入力を使うための許可"
        alert.informativeText = "1.「macOSの許可設定を開く」を押します。\n2. 表示された一覧で「RELYR Mac Trial」をONにします。\n3. この画面へ戻り、許可済みになったことを確認して完了します。\n\n設定の場所は「プライバシーとセキュリティ」内の操作許可です。左側の一般的な「アクセシビリティ」画面ではありません。macOSのバージョンにより名称が異なるため、下のボタンから直接開いてください。"
        alert.addButton(withTitle:permissionResumeRequested ? "入力を有効にする" : "完了")
        alert.addButton(withTitle:"あとで")
        let body = NSView(frame:NSRect(x:0,y:0,width:420,height:150))
        let state = NSTextField(wrappingLabelWithString:"")
        state.frame = NSRect(x:0,y:116,width:420,height:32); body.addSubview(state)
        let controls: [(String,Selector)] = [
            ("macOSの許可設定を開く",#selector(requestPermission)),
            ("一覧にない／ONでも認識されない",#selector(showPermissionTroubleshooting)),
            ("このアプリをFinderで表示",#selector(revealPermissionApp))]
        for (index,item) in controls.enumerated() {
            let button = NSButton(title:item.0,target:self,action:item.1)
            button.frame = NSRect(x:0,y:78-CGFloat(index)*36,width:420,height:32)
            body.addSubview(button)
        }
        alert.accessoryView = body
        permissionNotice = alert; permissionStateLabel = state
        refreshPermissionGuide()
        permissionTimer?.invalidate()
        permissionTimer = Timer.scheduledTimer(withTimeInterval:1,repeats:true) { [weak self] _ in self?.refreshPermissionGuide() }
        alert.beginSheetModal(for:window) { [weak self] response in
            guard let self = self else { return }
            self.permissionTimer?.invalidate(); self.permissionTimer = nil
            self.permissionNotice = nil; self.permissionStateLabel = nil
            let resume = self.permissionResumeRequested
            self.permissionResumeRequested = false
            if response == .alertFirstButtonReturn && AXIsProcessTrusted() && resume { self.engine.start() }
        }
    }
    private func refreshPermissionGuide() {
        let trusted = AXIsProcessTrusted()
        refreshPermissionEntry()
        permissionStateLabel?.stringValue = trusted ? "✓ 許可済み — macOS側の操作は完了しています" : "許可をまだ確認できません — 下のボタンから設定してください"
        permissionStateLabel?.textColor = trusted ? Studio.accent : Studio.text
        permissionNotice?.buttons.first?.isEnabled = trusted
    }
    @objc private func requestPermission() {
        let options = [kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String:true] as CFDictionary
        _ = AXIsProcessTrustedWithOptions(options)
        if let url = URL(string:"x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility") {
            NSWorkspace.shared.open(url)
        }
        refreshPermissionGuide()
    }
    @objc private func undoEditor() {
        if let text = window.firstResponder as? NSTextView { text.undoManager?.undo() } else { workspace.undo() }
    }
    @objc private func redoEditor() {
        if let text = window.firstResponder as? NSTextView { text.undoManager?.redo() } else { workspace.redo() }
    }
    @objc private func toggleInput() {
        if modalCaptureActive {
            workspace.setStatus("ボタン確認画面を閉じてください。確認前の入力状態へ戻ります。",enabled:false,requested:modalCaptureResume)
            return
        }
        if inlineCapture != nil { finishInlineMouseCapture() }
        if engine.requested { engine.stop(); return }
        guard AXIsProcessTrusted() else { permissionResumeRequested = true; showPermissionSetup(); return }
        permissionTimer?.invalidate(); permissionTimer = nil
        engine.start()
    }
    @objc private func stopInput() { captureResume = false; modalCaptureResume = false; finishInlineMouseCapture(); engine.stop() }
    static func isLoginLaunch(_ event: NSAppleEventDescriptor?) -> Bool {
        event?.paramDescriptor(forKeyword:keyAEPropData)?.enumCodeValue == keyAELaunchedAsLogInItem ||
            event?.paramDescriptor(forKeyword:keyAELaunchedAsLogInItem) != nil
    }
    private func presentInitialWindow(background: Bool) {
        window.isRestorable = false
        if background { window.orderOut(nil); NSApp.setActivationPolicy(.accessory) }
        else { showWindow() }
    }
    private var offeredPermissionSetup = false
    @objc private func showWindow() {
        NSApp.setActivationPolicy(.regular); window.makeKeyAndOrderFront(nil); NSApp.activate(ignoringOtherApps: true)
        refreshPermissionEntry()
        guard !CommandLine.arguments.contains("--ui-smoke-test"), !offeredPermissionSetup,
              !AXIsProcessTrusted() else { return }
        offeredPermissionSetup = true
        DispatchQueue.main.async { [weak self] in
            guard let self = self, self.window.isVisible, !AXIsProcessTrusted() else { return }
            self.permissionResumeRequested = true
            self.showPermissionSetup()
        }
    }
    @objc private func typingPriorityChanged(_ sender: NSButton) {
        var candidate = configuration; candidate.typingPriority = sender.state == .on
        if persist(candidate) { refreshConfigurationViews() }
        else { sender.state = configuration.typingPriority != false ? .on : .off }
    }
    @objc private func reverseScrollChanged(_ sender: NSButton) {
        workspace.setReverseMouseScroll(sender.state == .on)
    }
    func application(_ application: NSApplication, open urls: [URL]) {
        guard urls.count == 1, let url = urls.first, url.scheme == "relyr-macro", url.host == "run",
              url.query == nil, url.fragment == nil else { return }
        let id = url.lastPathComponent
        DispatchQueue.main.asyncAfter(deadline:.now()+0.5) { [weak self] in
            guard let self = self, self.configuration.macros.contains(where: { $0.id == id }) else { return }
            self.engine.playMacro(id)
        }
    }
    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool { showWindow(); return true }
    func windowShouldClose(_ sender: NSWindow) -> Bool {
        guard sender === window else { return true }
        window.orderOut(nil)
        NSApp.setActivationPolicy(.accessory)
        return false
    }
    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { false }
    func applicationWillTerminate(_ notification: Notification) { if uninstalling { engine.stop(); instanceLock?.release(); return }; finishModalMouseCapture(); if inlineCapture != nil { finishInlineMouseCapture() }; saveRuntimeIntent(); suppressRuntimeSave = true; permissionTimer?.invalidate(); engine.stop(); instanceLock?.release() }
}
