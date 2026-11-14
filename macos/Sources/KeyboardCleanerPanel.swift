import AppKit

final class KeyboardCleanerPanel {
    private let panel: NSPanel
    private let diagram = CleaningKeyboard(frame: NSRect(x: 32, y: 111, width: 376, height: 118))
    var onFinish: (() -> Void)?

    init() {
        panel = NSPanel(contentRect: NSRect(x: 0, y: 0, width: 440, height: 292),
                        styleMask: [.titled, .fullSizeContentView, .nonactivatingPanel],
                        backing: .buffered, defer: false)
        panel.titleVisibility = .hidden
        panel.titlebarAppearsTransparent = true
        panel.standardWindowButton(.closeButton)?.isHidden = true
        panel.standardWindowButton(.miniaturizeButton)?.isHidden = true
        panel.standardWindowButton(.zoomButton)?.isHidden = true
        panel.level = .floating
        panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        panel.hidesOnDeactivate = false
        let surface = NSVisualEffectView()
        surface.material = .hudWindow
        surface.state = .active
        panel.contentView = surface

        let title = NSTextField(labelWithString: "キーボード掃除中")
        title.font = .systemFont(ofSize: 21, weight: .semibold)
        title.alignment = .center
        title.frame = NSRect(x: 24, y: 244, width: 392, height: 28)
        surface.addSubview(title)
        surface.addSubview(diagram)

        let finish = CleanerHoldButton(frame: NSRect(x: 123, y: 59, width: 194, height: 36))
        finish.title = "掃除を終了（長押し）"
        finish.font = .systemFont(ofSize: 13, weight: .semibold)
        finish.bezelStyle = .rounded
        finish.onFinish = { [weak self] in self?.onFinish?() }
        surface.addSubview(finish)

        let hint = NSTextField(labelWithString: "左右のShiftを3秒長押しでも終了")
        hint.textColor = .secondaryLabelColor
        hint.font = .systemFont(ofSize: 11)
        hint.alignment = .center
        hint.frame = NSRect(x: 24, y: 36, width: 392, height: 16)
        surface.addSubview(hint)
        let note = NSTextField(labelWithString: "電源・Touch IDなど一部のキーは対象外")
        note.textColor = .secondaryLabelColor
        note.font = .systemFont(ofSize: 10)
        note.alignment = .center
        note.frame = NSRect(x: 24, y: 15, width: 392, height: 15)
        surface.addSubview(note)
    }

    func setVisible(_ visible: Bool) {
        if visible {
            if let screen = NSScreen.screens.first(where: { $0.frame.contains(NSEvent.mouseLocation) }) {
                let area = screen.visibleFrame
                panel.setFrameOrigin(NSPoint(x: area.midX - panel.frame.width/2, y: area.midY - panel.frame.height/2))
            } else { panel.center() }
            panel.orderFrontRegardless()
        } else {
            panel.orderOut(nil)
            update(keys: [], progress: 0)
        }
    }

    func savePreview(to path: String) {
        guard let content = panel.contentView else { return }
        update(keys: [56,60], progress: 0.55)
        saveSmokePreview(content, to: path)
        update(keys: [], progress: 0)
    }

    func update(keys: Set<UInt16>, progress: Double) {
        diagram.update(keys: keys, progress: progress)
    }
}

private final class CleanerHoldButton: NSButton {
    var onFinish: (() -> Void)?
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }
    override func accessibilityPerformPress() -> Bool {
        onFinish?()
        return true
    }
    private var pressedAt: TimeInterval?
    override func mouseDown(with event: NSEvent) {
        pressedAt = ProcessInfo.processInfo.systemUptime
        highlight(true)
    }

    override func mouseDragged(with event: NSEvent) {
        if !bounds.contains(convert(event.locationInWindow, from: nil)) {
            pressedAt = nil
            highlight(false)
        }
    }

    override func mouseUp(with event: NSEvent) {
        let start = pressedAt
        pressedAt = nil
        highlight(false)
        if let start = start, ProcessInfo.processInfo.systemUptime - start >= 1,
           bounds.contains(convert(event.locationInWindow, from: nil)) {
            onFinish?()
        }
    }

}

private final class CleaningKeyboard: NSView {
    private var held: Set<UInt16> = []
    private var progress: Double = 0
    private let rows: [[UInt16]] = [
        [53,122,120,99,118,96,97,98,100,101,109,103,111],
        [50,18,19,20,21,23,22,26,28,25,29,27,24,51],
        [48,12,13,14,15,17,16,32,34,31,35,33,30,42],
        [57,0,1,2,3,5,4,38,40,37,41,39,36],
        [56,6,7,8,9,11,45,46,43,47,44,60],
        [63,59,58,55,49,54,61,123,125,124]
    ]

    func update(keys: Set<UInt16>, progress: Double) {
        guard keys != held || progress != self.progress else { return }
        held = keys
        self.progress = progress
        needsDisplay = true
    }

    override func draw(_ dirtyRect: NSRect) {
        NSColor.black.withAlphaComponent(0.15).setFill()
        NSBezierPath(roundedRect: bounds.insetBy(dx: 1, dy: 1), xRadius: 13, yRadius: 13).fill()
        for (row, keys) in rows.enumerated() {
            let weights = keys.map { key -> CGFloat in
                if key == 49 { return 5 }
                if [56,60,36,51,48,57].contains(key) { return 1.6 }
                return 1
            }
            let spacing: CGFloat = 3
            let unit = (bounds.width - 24 - CGFloat(keys.count - 1) * spacing) / weights.reduce(0,+)
            var x: CGFloat = 12
            for (index, key) in keys.enumerated() {
                let width = unit * weights[index]
                let cap = NSRect(x: x, y: 94 - CGFloat(row)*16, width: width, height: 13)
                (held.contains(key) ? Studio.accent : NSColor.labelColor.withAlphaComponent(0.15)).setFill()
                NSBezierPath(roundedRect: cap, xRadius: 3, yRadius: 3).fill()
                x += width + spacing
            }
        }
        if progress > 0 {
            Studio.accent.setFill()
            NSBezierPath(roundedRect: NSRect(x: 12, y: 6, width: (bounds.width-24)*progress, height: 2),
                         xRadius: 1, yRadius: 1).fill()
        }
    }
}
