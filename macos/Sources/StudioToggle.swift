import AppKit

/// Theme-colored switch, preserving the existing NSButton state/action contract.
final class StudioToggle: NSButton {
    let toggle = StudioThemeSwitch()
    override var intrinsicContentSize: NSSize {
        let width = (title as NSString).size(withAttributes:[.font:font ?? NSFont.systemFont(ofSize:13)]).width
        return NSSize(width:ceil(width)+52,height:26)
    }
    override var isFlipped: Bool { true }
    override var state: NSControl.StateValue { didSet { toggle.state = state; needsDisplay = true } }
    override var isEnabled: Bool { didSet { toggle.isEnabled = isEnabled; needsDisplay = true } }
    init(title: String, target: AnyObject?, action: Selector?) {
        super.init(frame:.zero)
        self.title = title; self.target = target; self.action = action
        setButtonType(.switch); isBordered = false; font = .systemFont(ofSize:13)
        toggle.controlSize = .small; toggle.target = self; toggle.action = #selector(changed)
        toggle.setAccessibilityLabel(title); addSubview(toggle)
        setAccessibilityElement(false)
    }
    required init?(coder: NSCoder) { fatalError("init(coder:) has not been implemented") }
    @objc private func changed() { state = toggle.state; _ = sendAction(action,to:target) }
    override func layout() {
        super.layout(); toggle.sizeToFit()
        toggle.frame.origin = NSPoint(x:0,y:(bounds.height-toggle.frame.height)/2)
    }
    override func draw(_ dirtyRect: NSRect) {
        let paragraph = NSMutableParagraphStyle(); paragraph.lineBreakMode = .byTruncatingTail
        (title as NSString).draw(in:NSRect(x:48,y:(bounds.height-18)/2,width:max(0,bounds.width-48),height:20),
            withAttributes:[.font:font ?? NSFont.systemFont(ofSize:13),.foregroundColor:isEnabled ? NSColor.labelColor : NSColor.disabledControlTextColor,.paragraphStyle:paragraph])
    }
}

/// AppKit handles mouse/keyboard/VoiceOver activation; RELYR owns only the appearance.
final class StudioThemeSwitch: NSButton {
    override var isFlipped: Bool { true }
    override var state: NSControl.StateValue { didSet { needsDisplay = true } }
    override var isEnabled: Bool { didSet { needsDisplay = true } }
    override init(frame: NSRect) {
        super.init(frame:frame); setButtonType(.switch); isBordered = false; title = ""
        setAccessibilityRole(.checkBox)
    }
    required init?(coder: NSCoder) { fatalError("init(coder:) has not been implemented") }
    override var intrinsicContentSize: NSSize { NSSize(width:36,height:22) }
    override func sizeToFit() { frame.size = intrinsicContentSize }
    override func draw(_ dirtyRect: NSRect) {
        let track = bounds.insetBy(dx:1,dy:1)
        let on = state == .on
        (on ? Studio.accent : Studio.muted.withAlphaComponent(0.3)).withAlphaComponent(isEnabled ? 1 : 0.35).setFill()
        let path = NSBezierPath(roundedRect:track,xRadius:10,yRadius:10); path.fill()
        if Studio.highContrast { Studio.text.setStroke(); path.lineWidth = 1; path.stroke() }
        let knob = NSRect(x:on ? track.maxX-18 : track.minX+2,y:track.minY+2,width:16,height:16)
        NSColor.white.withAlphaComponent(isEnabled ? 1 : 0.5).setFill(); NSBezierPath(ovalIn:knob).fill()
    }
}
