import AppKit

private final class DeckPanel: NSPanel {
    override var canBecomeKey: Bool { false }
    override var canBecomeMain: Bool { false }
}

private final class DeckButton: NSButton {
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }
}

enum DeckEdit { case rename(String), remove, move(Int) }

final class DeckController: NSObject {
    private(set) var panel: NSPanel!
    private let status = NSTextField(labelWithString: "停止中")
    private let scroll = NSScrollView()
    private var cells: [String: (DeckButton, NSTextField)] = [:]
    private var order: [String] = []
    private var configuration = Configuration()
    var execute: ((String) -> Void)?
    var edit: ((String, DeckEdit) -> Void)?
    var requestRename: ((String) -> Void)?

    override init() {
        super.init()
        panel = DeckPanel(contentRect: NSRect(x: 0, y: 0, width: 550, height: 180),
            styleMask: [.titled, .closable, .nonactivatingPanel], backing: .buffered, defer: false)
        panel.title = "RELYR Deck"
        panel.isReleasedWhenClosed = false
        panel.level = .floating
        panel.hidesOnDeactivate = false
        panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        panel.isMovableByWindowBackground = true
        panel.center()
        scroll.hasVerticalScroller = true
        scroll.drawsBackground = false
        status.font = .systemFont(ofSize: 11)
        status.textColor = .secondaryLabelColor
        let root = NSStackView(views: [status, scroll])
        root.orientation = .vertical; root.alignment = .leading; root.spacing = 8
        root.translatesAutoresizingMaskIntoConstraints = false
        panel.contentView!.addSubview(root)
        NSLayoutConstraint.activate([
            root.leadingAnchor.constraint(equalTo: panel.contentView!.leadingAnchor, constant: 12),
            root.trailingAnchor.constraint(equalTo: panel.contentView!.trailingAnchor, constant: -12),
            root.topAnchor.constraint(equalTo: panel.contentView!.topAnchor, constant: 12),
            root.bottomAnchor.constraint(equalTo: panel.contentView!.bottomAnchor, constant: -12),
            scroll.widthAnchor.constraint(equalTo: root.widthAnchor)
        ])
    }

    func toggle() { if panel.isVisible { panel.orderOut(nil) } else { panel.orderFrontRegardless() } }

    func update(_ configuration: Configuration, enabled: Bool) {
        self.configuration = configuration
        let ids = configuration.deck.map { $0.id }
        if ids != order || scroll.documentView == nil {
            order = ids; cells.removeAll()
            let content = NSStackView()
            content.orientation = .vertical; content.alignment = .leading; content.spacing = 6
            if configuration.deck.isEmpty {
                content.addArrangedSubview(NSTextField(labelWithString: "設定画面で割り当てを選び「Deckへ追加」を押してください。"))
            }
            for start in stride(from: 0, to: configuration.deck.count, by: 6) {
                let row = NSStackView()
                row.orientation = .horizontal; row.alignment = .top; row.spacing = 40
                for item in configuration.deck[start..<min(start + 6, configuration.deck.count)] {
                    let button = DeckButton(title: "", target: self, action: #selector(clicked(_:)))
                    button.identifier = NSUserInterfaceItemIdentifier(item.id)
                    button.bezelStyle = .regularSquare
                    button.widthAnchor.constraint(equalToConstant: 54).isActive = true
                    button.heightAnchor.constraint(equalToConstant: 52).isActive = true
                    button.menu = contextMenu(item.id)
                    let label = NSTextField(wrappingLabelWithString: item.title)
                    label.alignment = .center; label.maximumNumberOfLines = 2; label.lineBreakMode = .byTruncatingTail
                    label.font = .systemFont(ofSize: 11)
                    label.widthAnchor.constraint(equalToConstant: 54).isActive = true
                    label.heightAnchor.constraint(equalToConstant: 30).isActive = true
                    let cell = NSStackView(views: [button, label])
                    cell.orientation = .vertical; cell.spacing = 4; cell.alignment = .centerX
                    row.addArrangedSubview(cell)
                    cells[item.id] = (button, label)
                }
                content.addArrangedSubview(row)
            }
            content.layoutSubtreeIfNeeded()
            content.setFrameSize(content.fittingSize)
            scroll.documentView = content
            let height = min(440, max(80, content.fittingSize.height)) + 48
            let top = panel.frame.maxY
            panel.setContentSize(NSSize(width: 566, height: height))
            panel.setFrameOrigin(NSPoint(x: panel.frame.minX, y: top - panel.frame.height))
        }
        for item in configuration.deck {
            guard let (button, label) = cells[item.id], let mapping = configuration.mapping(for: item) else { continue }
            label.stringValue = item.title; label.toolTip = item.title
            button.toolTip = "\(item.title) — \(configuration.profileName(item.profileID)): \(mapping.description)"
            button.setAccessibilityLabel(item.title)
            switch mapping.action {
            case .key, .shortcut: button.title = mapping.targetDescription
            case .launch: button.title = ""
            case .text: button.title = "文字"
            case .url: button.title = "↗"
            case .deck: button.title = "Deck"
            }
            button.image = nil
            if mapping.action == .launch, let icon = NSWorkspace.shared.icon(forFile: mapping.application).copy() as? NSImage {
                icon.size = NSSize(width: 32, height: 32); button.image = icon
            }
        }
        setEnabled(enabled)
    }

    func setEnabled(_ enabled: Bool) {
        status.stringValue = enabled ? "作業中のアプリへ実行 • 右クリックで編集・並べ替え" : "停止中 • 設定画面で「有効にする」を押してください"
        for (button, _) in cells.values { button.isEnabled = enabled }
    }

    func verifyPresentation(expectedButtons: Int) {
        panel.contentView?.layoutSubtreeIfNeeded()
        scroll.documentView?.layoutSubtreeIfNeeded()
        verifySmoke(cells.count == expectedButtons && panel.isVisible)
        verifySmoke(!panel.canBecomeKey && !panel.canBecomeMain)
        verifySmoke(panel.styleMask.contains(.nonactivatingPanel))
        for (button, _) in cells.values {
            // AppKit frames include bezel/shadow insets; width/height constraints size the alignment rect.
            let rect = button.alignmentRect(forFrame: button.frame)
            verifySmoke(rect.width == 54 && rect.height == 52, "Deck button alignment \(rect), frame \(button.frame)")
            verifySmoke(!button.isEnabled, "Smoke test must never enable input")
        }
    }

    private func contextMenu(_ id: String) -> NSMenu {
        let menu = NSMenu()
        for (title, tag) in [("名前を変更…", 0), ("前へ移動", -1), ("後へ移動", 1), ("Deckから削除", 2)] {
            let item = NSMenuItem(title: title, action: #selector(contextAction(_:)), keyEquivalent: "")
            item.target = self; item.representedObject = id; item.tag = tag
            menu.addItem(item)
        }
        return menu
    }
    @objc private func clicked(_ sender: NSButton) {
        if let id = sender.identifier?.rawValue { execute?(id) }
    }
    @objc private func contextAction(_ sender: NSMenuItem) {
        guard let id = sender.representedObject as? String else { return }
        switch sender.tag {
        case 0: requestRename?(id)
        case 2: edit?(id, .remove)
        default: edit?(id, .move(sender.tag))
        }
    }
}
