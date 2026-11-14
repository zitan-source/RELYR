import AppKit

final class TrackpadVolumeHUD {
    private lazy var panel = makePanel()
    private let meter = NSProgressIndicator(frame: NSRect(x: 40, y: 18, width: 118, height: 12))
    private var hideTimer: Timer?

    private func makePanel() -> NSPanel {
        let panel = NSPanel(contentRect: NSRect(x: 0, y: 0, width: 198, height: 48),
                            styleMask: [.titled, .fullSizeContentView, .nonactivatingPanel],
                            backing: .buffered, defer: false)
        panel.titleVisibility = .hidden
        panel.titlebarAppearsTransparent = true
        panel.hidesOnDeactivate = false
        panel.ignoresMouseEvents = true
        panel.level = .statusBar
        panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .ignoresCycle]
        for button in [NSWindow.ButtonType.closeButton, .miniaturizeButton, .zoomButton] {
            panel.standardWindowButton(button)?.isHidden = true
        }
        let surface = NSVisualEffectView(frame: panel.contentView?.bounds ?? .zero)
        surface.material = .hudWindow
        surface.state = .active
        panel.contentView = surface
        let speaker = NSImageView(frame: NSRect(x: 14, y: 16, width: 18, height: 16))
        speaker.image = NSImage(systemSymbolName: "speaker.wave.2", accessibilityDescription: "音量")
        surface.addSubview(speaker)
        meter.style = .bar
        meter.isIndeterminate = false
        meter.minValue = 0
        meter.maxValue = 1
        surface.addSubview(meter)
        return panel
    }

    func show(_ volume: Float32) {
        let panel = panel
        meter.doubleValue = Double(volume)
        if !panel.isVisible, let screen = NSScreen.screens.first(where: { $0.frame.contains(NSEvent.mouseLocation) }) ?? NSScreen.main {
            let area = screen.visibleFrame
            panel.setFrameOrigin(NSPoint(x: area.midX - panel.frame.width / 2, y: area.minY + 64))
            panel.orderFrontRegardless()
        }
        hideTimer?.invalidate()
        let timer = Timer(timeInterval: 0.8, repeats: false) { [weak panel] _ in panel?.orderOut(nil) }
        hideTimer = timer
        RunLoop.main.add(timer, forMode: .common)
    }
}
