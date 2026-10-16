import AppKit
import ApplicationServices
import UniformTypeIdentifiers
import Darwin

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

final class AppDelegate: NSObject, NSApplicationDelegate, NSTableViewDataSource, NSTableViewDelegate {
    private var window: NSWindow!
    private let table = NSTableView()
    private let status = NSTextField(wrappingLabelWithString: "停止中：設定後に「有効にする」を押してください。")
    private let toggle = NSButton(title: "有効にする", target: nil, action: nil)
    private let layer = NSPopUpButton()
    private let input = NSPopUpButton()
    private let action = NSPopUpButton()
    private let output = NSPopUpButton()
    private let space = NSButton(checkboxWithTitle: "Spaceレイヤーを使う", target: nil, action: nil)
    private let application = NSTextField(labelWithString: "アプリ未選択")
    private let chooseApp = NSButton(title: "アプリを選ぶ…", target: nil, action: nil)
    private let payloadControls = NSStackView()
    private let payloadSummary = NSTextField(labelWithString: "未設定")
    private var payload = ""
    private let modifierButtons = ["⌘ Command", "⌃ Control", "⌥ Option", "⇧ Shift"].map {
        NSButton(checkboxWithTitle: $0, target: nil, action: nil)
    }
    private let modifierValues: [Modifiers] = [.command, .control, .option, .shift]
    private var configuration = Configuration()
    private var applicationPath = ""
    private var loadFailed = false
    private var statusItem: NSStatusItem!
    private let engine = InputEngine()
    private let deck = DeckController()
    private let profilePicker = NSPopUpButton()
    private let manualPicker = NSPopUpButton()
    private let automatic = NSButton(checkboxWithTitle: "アプリに合わせて自動切替", target: nil, action: nil)
    private let runtimeLabel = NSTextField(labelWithString: "実行中：標準")
    private let profileApps = NSTextField(labelWithString: "対象アプリ：なし")
    private var editedProfileID = "default"
    private var editableControls: [NSControl] = []
    private var profileSpecificControls: [NSControl] = []
    private var editedMappings: [Mapping] { configuration.mappings(for: editedProfileID) }
    private var profileIDs: [String] { ["default"] + configuration.profiles.map { $0.id } }
    private let smoke = CommandLine.arguments.contains("--ui-smoke-test")
    private lazy var store: ConfigurationStore = {
        if smoke, CommandLine.arguments.count > 2 {
            return ConfigurationStore(url: URL(fileURLWithPath: CommandLine.arguments[2]).deletingLastPathComponent()
                .appendingPathComponent("ui-test-state/configuration.json"))
        }
        return ConfigurationStore(url: FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("RELYR-Mac-Trial/configuration.json"))
    }()

    func applicationDidFinishLaunching(_ notification: Notification) {
        if !smoke, let id = Bundle.main.bundleIdentifier,
           let other = NSRunningApplication.runningApplications(withBundleIdentifier: id).first(where: { $0.processIdentifier != ProcessInfo.processInfo.processIdentifier }) {
            other.activate(options: [.activateIgnoringOtherApps])
            NSApp.terminate(nil)
            return
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
        engine.apply(configuration)
        engine.onProfile = { [weak self] name in self?.runtimeLabel.stringValue = "実行中：\(name)" }
        engine.onToggleDeck = { [weak self] in self?.deck.toggle() }
        engine.onStatus = { [weak self] message in
            guard let self = self else { return }
            self.status.stringValue = message
            self.toggle.title = self.engine.enabled ? "停止する" : "有効にする"
            self.statusItem?.button?.title = self.engine.enabled ? "RELYR ●" : "RELYR ○"
            self.deck.setEnabled(self.engine.enabled)
        }
        deck.execute = { [weak self] id in
            guard let self = self, let item = self.configuration.deck.first(where: { $0.id == id }),
                  let mapping = self.configuration.mapping(for: item) else { return }
            self.engine.performFromDeck(mapping)
        }
        deck.edit = { [weak self] id, edit in self?.editDeck(id, edit: edit) }
        deck.requestRename = { [weak self] id in self?.renameDeck(id) }
        buildMenus()
        buildWindow()
        refreshConfigurationViews()
        engine.apply(configuration)
        showWindow()
        if smoke {
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
                if CommandLine.arguments.count > 2 {
                    saveSmokePreview(self.window.contentView!, to: CommandLine.arguments[2])
                    let deckPath = URL(fileURLWithPath: CommandLine.arguments[2]).deletingPathExtension().path + "-deck.png"
                    saveSmokePreview(self.deck.panel.contentView!, to: deckPath)
                }
                self.deck.verifyPresentation(expectedButtons: 3)
                print("PASS: native window, UI save/delete/persistence, profile/editor isolation, Deck geometry and disabled startup")
                NSApp.terminate(nil)
            }
        }
    }

    private func buildMenus() {
        let main = NSMenu()
        let app = NSMenuItem()
        let appMenu = NSMenu()
        appMenu.addItem(withTitle: "RELYRを終了", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q")
        app.submenu = appMenu
        main.addItem(app)
        let edit = NSMenuItem()
        edit.title = "編集"
        let editMenu = NSMenu(title: "編集")
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
        menu.addItem(item("Deckを表示／非表示", #selector(toggleDeck)))
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
        window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 940, height: 800),
            styleMask: [.titled, .closable, .miniaturizable, .resizable], backing: .buffered, defer: false)
        window.title = "RELYR for Mac — 試用版 0.2.0"
        window.minSize = NSSize(width: 880, height: 760)
        window.isReleasedWhenClosed = false
        window.center()
        let title = NSTextField(labelWithString: "キーボードを、拡張する。")
        title.font = .systemFont(ofSize: 25, weight: .semibold)
        let intro = NSTextField(wrappingLabelWithString: "通常キー、Spaceレイヤー、Macのショートカットを設定できます。変更は保存後すぐ反映されます。")
        intro.textColor = .secondaryLabelColor
        toggle.target = self; toggle.action = #selector(toggleInput)
        let permission = NSButton(title: "アクセシビリティを許可…", target: self, action: #selector(requestPermission))
        let controls = row([toggle, permission])
        let starterButton = NSButton(title: "サンプル追加", target: self, action: #selector(addStarter))
        let exportButton = NSButton(title: "設定を書き出す…", target: self, action: #selector(exportSettings))
        let tools = row([
            starterButton,
            NSButton(title: "設定を読み込む…", target: self, action: #selector(importSettings)),
            exportButton,
            NSButton(title: "Deckを表示", target: self, action: #selector(toggleDeck)),
            NSButton(title: "使い方", target: self, action: #selector(showHelp))
        ])
        let create = NSButton(title: "新規", target: self, action: #selector(createProfile))
        let duplicate = NSButton(title: "複製", target: self, action: #selector(duplicateProfile))
        let rename = NSButton(title: "名前変更", target: self, action: #selector(renameProfile))
        let remove = NSButton(title: "削除", target: self, action: #selector(deleteProfile))
        profilePicker.target = self; profilePicker.action = #selector(profileChanged)
        manualPicker.target = self; manualPicker.action = #selector(manualProfileChanged)
        automatic.target = self; automatic.action = #selector(automaticChanged)
        profilePicker.widthAnchor.constraint(equalToConstant: 180).isActive = true
        manualPicker.widthAnchor.constraint(equalToConstant: 180).isActive = true
        let profiles = row([label("編集"), profilePicker, create, duplicate, rename, remove])
        let runtime = row([automatic, label("手動使用"), manualPicker, runtimeLabel])
        let associate = NSButton(title: "対象アプリを追加…", target: self, action: #selector(associateApplication))
        let clear = NSButton(title: "関連付けを解除", target: self, action: #selector(clearApplications))
        profileApps.lineBreakMode = .byTruncatingTail
        profileApps.widthAnchor.constraint(lessThanOrEqualToConstant: 490).isActive = true
        let association = row([associate, clear, profileApps])
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
        layer.addItems(withTitles: ["通常", "Space"])
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
        let fresh = NSButton(title: "新規", target: self, action: #selector(newMapping))
        let addDeck = NSButton(title: "Deckへ追加", target: self, action: #selector(addToDeck))
        editableControls = [toggle, save, delete, space, create, duplicate, rename, remove, associate, clear, addDeck, automatic, starterButton, exportButton]
        profileSpecificControls = [rename, remove, clear]
        for control in editableControls { control.isEnabled = !loadFailed }
        let note = NSTextField(wrappingLabelWithString: "Space単押しは空白入力。未割り当てのキーと組み合わせた場合は通常入力に戻ります。キー名はUS配列の物理位置です。緊急停止：Control + Option + Command + Escape。ウィンドウを閉じてもメニューバーに常駐します。")
        note.font = .systemFont(ofSize: 12)
        note.textColor = .secondaryLabelColor
        let stack = NSStackView(views: [title, intro, controls, status, tools, profiles, runtime, association, scroll, space,
            row([label("レイヤー"), layer, label("入力"), input, label("動作"), action]),
            row([label("出力"), output] + modifierButtons), row([chooseApp, application]), payloadControls,
            row([fresh, save, delete, addDeck]), note])
        stack.orientation = .vertical
        stack.alignment = .leading
        stack.spacing = 9
        stack.translatesAutoresizingMaskIntoConstraints = false
        let content = window.contentView!
        content.addSubview(stack)
        NSLayoutConstraint.activate([
            stack.leadingAnchor.constraint(equalTo: content.leadingAnchor, constant: 24),
            stack.trailingAnchor.constraint(equalTo: content.trailingAnchor, constant: -24),
            stack.topAnchor.constraint(equalTo: content.topAnchor, constant: 24),
            stack.bottomAnchor.constraint(equalTo: content.bottomAnchor, constant: -24),
            scroll.widthAnchor.constraint(equalTo: stack.widthAnchor),
            status.widthAnchor.constraint(equalTo: stack.widthAnchor),
            intro.widthAnchor.constraint(equalTo: stack.widthAnchor), note.widthAnchor.constraint(equalTo: stack.widthAnchor)
        ])
        actionChanged()
    }

    private func label(_ title: String) -> NSTextField { NSTextField(labelWithString: title) }
    private func row(_ views: [NSView]) -> NSStackView {
        let stack = NSStackView(views: views)
        stack.orientation = .horizontal; stack.alignment = .centerY; stack.spacing = 10
        return stack
    }

    func numberOfRows(in tableView: NSTableView) -> Int { editedMappings.count }
    func tableView(_ tableView: NSTableView, viewFor tableColumn: NSTableColumn?, row: Int) -> NSView? {
        NSTextField(labelWithString: editedMappings[row].description)
    }
    func tableViewSelectionDidChange(_ notification: Notification) {
        guard editedMappings.indices.contains(table.selectedRow) else { return }
        let mapping = editedMappings[table.selectedRow]
        layer.selectItem(at: mapping.layer == .normal ? 0 : 1)
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
        let mapping = Mapping(layer: layer.indexOfSelectedItem == 0 ? .normal : .space,
            input: Keys.inputs[input.indexOfSelectedItem].code, action: kind,
            output: Keys.outputs[output.indexOfSelectedItem].code, modifiers: modifiers, application: applicationPath, payload: payload)
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
            try store.save(candidate)
            configuration = candidate
            engine.apply(candidate)
            table.deselectAll(nil)
            refreshConfigurationViews()
            return true
        } catch { showError(error); return false }
    }

    private func refreshConfigurationViews() {
        if !profileIDs.contains(editedProfileID) { editedProfileID = "default" }
        for picker in [profilePicker, manualPicker] {
            picker.removeAllItems()
            picker.addItems(withTitles: ["標準"] + configuration.profiles.map { $0.name })
        }
        profilePicker.selectItem(at: profileIDs.firstIndex(of: editedProfileID) ?? 0)
        manualPicker.selectItem(at: profileIDs.firstIndex(of: configuration.manualProfileID) ?? 0)
        manualPicker.isEnabled = !configuration.automaticProfiles && !loadFailed
        automatic.state = configuration.automaticProfiles ? .on : .off
        space.state = configuration.spaceEnabled ? .on : .off
        let ids = configuration.profiles.first { $0.id == editedProfileID }?.bundleIdentifiers ?? []
        profileApps.stringValue = "対象アプリ：" + (ids.isEmpty ? "なし（標準はその他のアプリ）" : ids.map { id in
            NSWorkspace.shared.urlForApplication(withBundleIdentifier: id)?.deletingPathExtension().lastPathComponent ?? id
        }.joined(separator: "、"))
        profileApps.toolTip = profileApps.stringValue
        for control in editableControls { control.isEnabled = !loadFailed }
        table.reloadData()
        for control in profileSpecificControls { control.isEnabled = !loadFailed && editedProfileID != "default" }
        deck.update(configuration, enabled: engine.enabled)
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
    @objc private func createProfile() { makeProfile(copy: false) }
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
        askName("Deckボタンの名前", initial: mapping.targetDescription) { name in
            var candidate = self.configuration
            candidate.deck.append(DeckItem(title: name, profileID: profileID, layer: mapping.layer, input: mapping.input))
            self.persist(candidate)
        }
    }
    @objc private func toggleDeck() { deck.toggle() }
    private func renameDeck(_ id: String) {
        guard let item = configuration.deck.first(where: { $0.id == id }) else { return }
        askName("Deckボタンの名前", initial: item.title) { self.editDeck(id, edit: .rename($0)) }
    }
    private func editDeck(_ id: String, edit: DeckEdit) {
        guard !loadFailed else { return }
        var candidate = configuration
        guard let index = candidate.deck.firstIndex(where: { $0.id == id }) else { return }
        switch edit {
        case .rename(let title): candidate.deck[index].title = title
        case .remove: candidate.deck.remove(at: index)
        case .move(let offset):
            let destination = index + offset
            guard candidate.deck.indices.contains(destination) else { return }
            candidate.deck.swapAt(index, destination)
        }
        persist(candidate)
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
                    } catch { self.showError(error) }
                }
            } catch { self.showError(error) }
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
    @objc private func requestPermission() {
        let options = [kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String: true] as CFDictionary
        _ = AXIsProcessTrustedWithOptions(options)
        if let url = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility") {
            NSWorkspace.shared.open(url)
        }
    }
    @objc private func toggleInput() { if engine.enabled { engine.stop() } else { engine.start() } }
    @objc private func stopInput() { engine.stop() }
    @objc private func showWindow() { window.makeKeyAndOrderFront(nil); NSApp.activate(ignoringOtherApps: true) }
    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool { showWindow(); return true }
    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { false }
    func applicationWillTerminate(_ notification: Notification) { engine.stop() }
}
