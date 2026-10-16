import AppKit
import ApplicationServices
import UniformTypeIdentifiers

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
    private let modifierButtons = ["⌘ Command", "⌃ Control", "⌥ Option", "⇧ Shift"].map {
        NSButton(checkboxWithTitle: $0, target: nil, action: nil)
    }
    private let modifierValues: [Modifiers] = [.command, .control, .option, .shift]
    private var configuration = Configuration()
    private var applicationPath = ""
    private var loadFailed = false
    private var statusItem: NSStatusItem!
    private let engine = InputEngine()
    private let smoke = CommandLine.arguments.contains("--ui-smoke-test")
    private lazy var store = ConfigurationStore(url: FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
        .appendingPathComponent("RELYR-Mac-Trial/configuration.json"))

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
        }
        engine.machine.configuration = configuration
        engine.onStatus = { [weak self] message in
            guard let self = self else { return }
            self.status.stringValue = message
            self.toggle.title = self.engine.enabled ? "停止する" : "有効にする"
            self.statusItem?.button?.title = self.engine.enabled ? "RELYR ●" : "RELYR ○"
        }
        buildMenus()
        buildWindow()
        showWindow()
        if smoke {
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.5) {
                precondition(self.window.isVisible && !self.engine.enabled)
                precondition(self.input.numberOfItems == Keys.inputs.count)
                precondition(self.table.numberOfColumns == 1 && self.output.numberOfItems == Keys.outputs.count)
                self.table.selectRowIndexes(IndexSet(integer: 1), byExtendingSelection: false)
                precondition(self.action.indexOfSelectedItem == 1 && self.modifierButtons[0].state == .on)
                precondition(!self.chooseApp.isEnabled && self.output.isEnabled)
                self.window.contentView?.layoutSubtreeIfNeeded()
                if CommandLine.arguments.count > 2, let view = self.window.contentView,
                   let bitmap = view.bitmapImageRepForCachingDisplay(in: view.bounds) {
                    view.cacheDisplay(in: view.bounds, to: bitmap)
                    // The content view is transparent; include the native window background in the preview.
                    let preview = NSImage(size: view.bounds.size)
                    preview.lockFocus()
                    NSColor.windowBackgroundColor.setFill()
                    view.bounds.fill()
                    let foreground = NSImage(size: view.bounds.size)
                    foreground.addRepresentation(bitmap)
                    foreground.draw(in: view.bounds, from: .zero, operation: .sourceOver, fraction: 1)
                    preview.unlockFocus()
                    if let tiff = preview.tiffRepresentation,
                       let rendered = NSBitmapImageRep(data: tiff),
                       let data = rendered.representation(using: .png, properties: [:]) {
                        do { try data.write(to: URL(fileURLWithPath: CommandLine.arguments[2])) }
                        catch { fatalError("Could not save UI verification image: \(error)") }
                    }
                }
                print("PASS: native window, key selectors, table, disabled startup")
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
        window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 820, height: 650),
            styleMask: [.titled, .closable, .miniaturizable, .resizable], backing: .buffered, defer: false)
        window.title = "RELYR for Mac — 試用版 0.1.0"
        window.minSize = NSSize(width: 760, height: 620)
        window.isReleasedWhenClosed = false
        window.center()
        let title = NSTextField(labelWithString: "キーボードを、拡張する。")
        title.font = .systemFont(ofSize: 25, weight: .semibold)
        let intro = NSTextField(wrappingLabelWithString: "通常キー、Spaceレイヤー、Macのショートカットを設定できます。変更は保存後すぐ反映されます。")
        intro.textColor = .secondaryLabelColor
        toggle.target = self; toggle.action = #selector(toggleInput)
        let permission = NSButton(title: "アクセシビリティを許可…", target: self, action: #selector(requestPermission))
        let controls = row([toggle, permission])
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
        action.addItems(withTitles: ["キー", "ショートカット", "アプリ起動"])
        output.addItems(withTitles: Keys.outputs.map { $0.name })
        layer.setAccessibilityLabel("レイヤー"); input.setAccessibilityLabel("入力キー")
        action.setAccessibilityLabel("動作"); output.setAccessibilityLabel("出力キー")
        action.target = self; action.action = #selector(actionChanged)
        chooseApp.target = self; chooseApp.action = #selector(selectApplication)
        application.lineBreakMode = .byTruncatingMiddle
        space.state = configuration.spaceEnabled ? .on : .off
        space.target = self; space.action = #selector(spaceChanged)
        let save = NSButton(title: "この割り当てを保存", target: self, action: #selector(saveMapping))
        let delete = NSButton(title: "選択した割り当てを削除", target: self, action: #selector(deleteMapping))
        let fresh = NSButton(title: "新規", target: self, action: #selector(newMapping))
        toggle.isEnabled = !loadFailed; save.isEnabled = !loadFailed; delete.isEnabled = !loadFailed; space.isEnabled = !loadFailed
        let note = NSTextField(wrappingLabelWithString: "Space単押しは空白入力。未割り当てのキーと組み合わせた場合は通常入力に戻ります。キー名はUS配列の物理位置です。緊急停止：Control + Option + Command + Escape。ウィンドウを閉じてもメニューバーに常駐します。")
        note.font = .systemFont(ofSize: 12)
        note.textColor = .secondaryLabelColor
        let stack = NSStackView(views: [title, intro, controls, status, scroll, space,
            row([label("レイヤー"), layer, label("入力"), input, label("動作"), action]),
            row([label("出力"), output] + modifierButtons), row([chooseApp, application]),
            row([fresh, save, delete]), note])
        stack.orientation = .vertical
        stack.alignment = .leading
        stack.spacing = 14
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

    func numberOfRows(in tableView: NSTableView) -> Int { configuration.mappings.count }
    func tableView(_ tableView: NSTableView, viewFor tableColumn: NSTableColumn?, row: Int) -> NSView? {
        NSTextField(labelWithString: configuration.mappings[row].description)
    }
    func tableViewSelectionDidChange(_ notification: Notification) {
        guard configuration.mappings.indices.contains(table.selectedRow) else { return }
        let mapping = configuration.mappings[table.selectedRow]
        layer.selectItem(at: mapping.layer == .normal ? 0 : 1)
        input.selectItem(at: Keys.inputs.firstIndex { $0.code == mapping.input } ?? 0)
        action.selectItem(at: ActionType.allCases.firstIndex(of: mapping.action) ?? 0)
        output.selectItem(at: Keys.outputs.firstIndex { $0.code == mapping.output } ?? 0)
        for (button, value) in zip(modifierButtons, modifierValues) { button.state = mapping.modifiers.contains(value) ? .on : .off }
        applicationPath = mapping.application
        application.stringValue = applicationPath.isEmpty ? "アプリ未選択" : applicationPath
        actionChanged()
    }

    @objc private func actionChanged() {
        output.isEnabled = action.indexOfSelectedItem != 2
        for button in modifierButtons { button.isEnabled = action.indexOfSelectedItem == 1 }
        chooseApp.isEnabled = action.indexOfSelectedItem == 2
    }
    @objc private func newMapping() {
        table.deselectAll(nil)
        applicationPath = ""; application.stringValue = "アプリ未選択"
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
            output: Keys.outputs[output.indexOfSelectedItem].code, modifiers: modifiers, application: applicationPath)
        if kind == .launch, !FileManager.default.fileExists(atPath: applicationPath) {
            showError(ConfigurationError.invalid("アプリを選択してください。移動・削除されたアプリは再選択が必要です。")); return
        }
        if candidate.mappings.indices.contains(table.selectedRow) { candidate.mappings.remove(at: table.selectedRow) }
        // A changed source must not silently overwrite another row.
        candidate.mappings.append(mapping)
        persist(candidate)
    }
    @objc private func deleteMapping() {
        guard !loadFailed, configuration.mappings.indices.contains(table.selectedRow) else { return }
        var candidate = configuration
        candidate.mappings.remove(at: table.selectedRow)
        persist(candidate)
    }
    @objc private func spaceChanged() {
        var candidate = configuration
        candidate.spaceEnabled = space.state == .on
        persist(candidate)
        space.state = configuration.spaceEnabled ? .on : .off
    }
    private func persist(_ candidate: Configuration) {
        do {
            try store.save(candidate)
            configuration = candidate
            engine.machine.configuration = candidate
            table.deselectAll(nil)
            table.reloadData()
        } catch { showError(error) }
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
