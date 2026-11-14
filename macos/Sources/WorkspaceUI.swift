import AppKit
import UniformTypeIdentifiers
import QuartzCore

// Keep native target/action and accessibility while drawing disabled captions legibly.
private final class StudioNativeActionCell: NSButtonCell {
    override var cellSize: NSSize {
        let size = super.cellSize
        return NSSize(width:size.width+28,height:max(40,size.height))
    }
    override func drawTitle(_ title: NSAttributedString, withFrame frame: NSRect, in controlView: NSView) -> NSRect {
        let text = NSMutableAttributedString(attributedString:title)
        text.addAttribute(.foregroundColor,value:isEnabled ? Studio.text : Studio.muted,range:NSRange(location:0,length:text.length))
        let paragraph = NSMutableParagraphStyle(); paragraph.alignment = .center; paragraph.lineBreakMode = .byTruncatingTail
        text.addAttribute(.paragraphStyle,value:paragraph,range:NSRange(location:0,length:text.length))
        let rect = NSRect(x:14,y:(controlView.bounds.height-text.size().height)/2,width:max(0,controlView.bounds.width-28),height:text.size().height)
        text.draw(in:rect)
        return rect
    }
}

// Visual tokens follow RELYR/ThemeService.cs. Input and configuration models stay shared.
enum Studio {
    // RELYR/MainWindow.xaml.cs AssignmentColorFor: opaque key backgrounds.
    static func assignmentFill(_ kind: ActionType) -> NSColor {
        switch kind {
        case .key: return color(0xAD621C)
        case .disabled: return color(0x626B78)
        case .text: return color(0x947017)
        case .macro: return color(0xC2434D)
        case .launch,.file,.url: return color(0x8B5BBE)
        case .profile: return color(0x3270C4)
        case .gesture: return color(0x007F8D)
        default: return color(0x14816F)
        }
    }
    // ThemeService icon colors are distinct from key backgrounds.
    static func assignmentColor(_ kind: ActionType) -> NSColor {
        switch kind {
        case .key: return adaptive(0xF09A3E,0xB85B00)
        case .text: return adaptive(0xE4B936,0x8A6500)
        case .launch,.file,.url: return adaptive(0xAA78DA,0x7040A3)
        case .macro: return adaptive(0xE15A65,0xB42332)
        case .gesture: return adaptive(0x00B8CC,0x007C8A)
        case .profile: return adaptive(0x68A7FF,0x075EAD)
        case .disabled: return adaptive(0xAAB4C2,0x596575)
        default: return adaptive(0x52D5BE,0x087B69)
        }
    }
    // One foreground for every assigned key, summary and HOLD badge.
    // Assignment backgrounds retain their category hues with white-text contrast.
    static func assignmentText(_ kind: ActionType) -> NSColor { .white }
    static func color(_ hex: UInt32) -> NSColor {
        NSColor(srgbRed: CGFloat((hex >> 16) & 255) / 255, green: CGFloat((hex >> 8) & 255) / 255,
                blue: CGFloat(hex & 255) / 255, alpha: 1)
    }
    static func adaptive(_ dark: UInt32, _ light: UInt32) -> NSColor {
        NSColor(name: nil) { appearance in color(appearance.bestMatch(from: [.darkAqua, .aqua]) == .darkAqua ? dark : light) }
    }
    static let background = adaptive(0x161719, 0xF3F3F3), key = adaptive(0x272A2E, 0xFBFBFC), border = adaptive(0x2D3034, 0xE5E6E8)
    static let text = adaptive(0xF4F4F5, 0x202124), muted = adaptive(0xA9ADB3, 0x676B73), accent = adaptive(0x1DA78C, 0x087B69)
    static let selected = adaptive(0x193F39, 0xDCEFEA), assigned = adaptive(0x168D76, 0x8BD6C5)
    static let animationDuration: Double = 0.14
    static var verificationAccessibility: (motion: Bool, contrast: Bool)?
    static var reduceMotion: Bool { verificationAccessibility?.motion ?? NSWorkspace.shared.accessibilityDisplayShouldReduceMotion }
    static var highContrast: Bool { verificationAccessibility?.contrast ?? NSWorkspace.shared.accessibilityDisplayShouldIncreaseContrast }
    // Keep the fixed key silhouette and hit target; only its clipped paint moves.
    static func assignmentCommit(_ view: NSView) {
        (view as? StudioButton)?.animateAssignmentCommit()
    }
    static func fade(_ layer: CALayer, to value: Float, duration: Double, key: String) {
        if !reduceMotion, layer.opacity == value { return }
        let start = layer.presentation()?.opacity ?? layer.opacity
        layer.removeAnimation(forKey:key)
        CATransaction.begin(); CATransaction.setDisableActions(true); layer.opacity = value; CATransaction.commit()
        guard !reduceMotion, abs(start-value) > 0.001 else { return }
        let animation = CABasicAnimation(keyPath:"opacity")
        animation.fromValue = start; animation.toValue = value; animation.duration = duration
        animation.timingFunction = CAMediaTimingFunction(controlPoints:0.2,0,0.2,1)
        layer.add(animation,forKey:key)
    }
    static func place(_ view: NSView, at frame: NSRect, animated: Bool = true) {
        let old = view.frame
        guard old != frame else { return }
        view.wantsLayer = true
        let prior = view.layer?.presentation()?.transform ?? CATransform3DIdentity
        view.layer?.removeAnimation(forKey:"studio-reorder")
        CATransaction.begin(); CATransaction.setDisableActions(true); view.frame = frame; CATransaction.commit()
        guard animated, !reduceMotion, view.window?.isVisible == true, old.size == frame.size, old.width > 0 else { return }
        let motion = CABasicAnimation(keyPath:"transform.translation")
        motion.fromValue = NSValue(point:NSPoint(x:old.minX-frame.minX+prior.m41,y:old.minY-frame.minY+prior.m42))
        motion.toValue = NSValue(point:.zero); motion.duration = 0.18
        motion.timingFunction = CAMediaTimingFunction(controlPoints:0.2,0,0.2,1)
        view.layer?.add(motion,forKey:"studio-reorder")
    }
    static func changedDeckAssignments(from old: Configuration, to new: Configuration) -> Set<String> {
        Set(new.deck.filter { item in
            guard let previous = old.deck.first(where: { $0.id == item.id }) else { return true }
            return old.mapping(for:previous) != new.mapping(for:item) || previous.monitor != item.monitor
        }.map { $0.id })
    }
    static func transition(_ view: NSView) {
        view.wantsLayer = true
        view.layer?.removeAnimation(forKey:"studio-transition")
        guard !reduceMotion, view.window?.isVisible == true else { return }
        // Reveal the new content, without retaining a snapshot of old text.
        let reveal = CABasicAnimation(keyPath:"opacity")
        reveal.fromValue = 0.72; reveal.toValue = 1; reveal.duration = 0.12
        reveal.timingFunction = CAMediaTimingFunction(controlPoints:0.2,0,0.2,1)
        view.layer?.add(reveal,forKey:"studio-transition")
    }
    static func themeControls(_ view: NSView) {
        // Native search/cancel buttons carry internal titles such as "search".
        // Styling their attributed titles makes those strings overlap the field editor.
        if view is NSSearchField { return }
        if let button = view as? NSButton, !(button is StudioButton), !(button is StudioToggle), !(button is StudioThemeSwitch) {
            if !(button is NSPopUpButton) {
                if !(button.cell is StudioNativeActionCell) {
                    let target = button.target, action = button.action, title = button.title
                    let enabled = button.isEnabled, state = button.state, key = button.keyEquivalent
                    let font = button.font, image = button.image, position = button.imagePosition
                    button.cell = StudioNativeActionCell(textCell:title)
                    button.target = target; button.action = action; button.title = title
                    button.isEnabled = enabled; button.state = state; button.keyEquivalent = key
                    button.font = font; button.image = image; button.imagePosition = position
                    // NSButton caches its bezel-based intrinsic size. Replacing the
                    // cell alone does not reserve enough space in an NSStackView.
                    let captionWidth = (title as NSString).size(withAttributes:[.font:font ?? NSFont.systemFont(ofSize:13)]).width
                    let minimumWidth = button.widthAnchor.constraint(greaterThanOrEqualToConstant:ceil(captionWidth)+28)
                    let minimumHeight = button.heightAnchor.constraint(greaterThanOrEqualToConstant:40)
                    minimumWidth.priority = .defaultHigh; minimumHeight.priority = .defaultHigh
                    NSLayoutConstraint.activate([minimumWidth,minimumHeight])
                    button.invalidateIntrinsicContentSize()
                }
                button.bezelStyle = .rounded; button.isBordered = false
                button.controlSize = .large
                button.wantsLayer = true; button.layer?.cornerRadius = 7
                button.layer?.borderWidth = 1; button.layer?.borderColor = Studio.border.cgColor
                button.layer?.backgroundColor = Studio.background.cgColor
            }
            button.bezelColor = button.keyEquivalent == "\r" ? Studio.accent : Studio.key
            button.contentTintColor = Studio.text
            if !(button is NSPopUpButton), !button.title.isEmpty {
                button.attributedTitle = NSAttributedString(string:button.title,attributes:[.font:button.font ?? NSFont.systemFont(ofSize:13),.foregroundColor:button.isEnabled ? Studio.text : Studio.muted])
            }
        }
        if let segments = view as? NSSegmentedControl { segments.selectedSegmentBezelColor = Studio.accent }
        view.subviews.forEach(themeControls)
    }
    static let dragType = NSPasteboard.PasteboardType("io.relyr.mac.action.v1")
    private static let appArtwork = NSCache<NSString, NSImage>()
    static func fittedAppArtwork(_ image: NSImage) -> NSImage {
        var rect = NSRect(origin:.zero,size:image.size)
        guard let cg = image.cgImage(forProposedRect:&rect,context:nil,hints:nil) else { return image }
        // Scan alpha bytes directly: colorAt creates an NSColor per pixel and
        // stalls launch/Deck rendering for hundreds of milliseconds per icon.
        var pixels = [UInt8](repeating:0,count:cg.width*cg.height*4)
        var left = cg.width, top = cg.height, right = -1, bottom = -1
        let decoded = pixels.withUnsafeMutableBytes { raw -> Bool in
            guard let context = CGContext(data:raw.baseAddress,width:cg.width,height:cg.height,
                bitsPerComponent:8,bytesPerRow:cg.width*4,space:CGColorSpaceCreateDeviceRGB(),
                bitmapInfo:CGImageAlphaInfo.premultipliedLast.rawValue | CGBitmapInfo.byteOrder32Big.rawValue) else { return false }
            context.draw(cg,in:CGRect(x:0,y:0,width:cg.width,height:cg.height))
            let bytes = raw.bindMemory(to:UInt8.self)
            for y in 0..<cg.height { for x in 0..<cg.width {
                if bytes[(y*cg.width+x)*4+3] > 20 {
                    left = min(left,x); right = max(right,x); top = min(top,y); bottom = max(bottom,y)
                }
            } }
            return true
        }
        guard decoded else { return image }
        guard right >= left, bottom >= top,
              let cropped = cg.cropping(to:CGRect(x:left,y:top,width:right-left+1,height:bottom-top+1)) else { return image }
        return NSImage(cgImage:cropped,size:NSSize(width:cropped.width,height:cropped.height))
    }
    private static let customArtwork = NSCache<NSData, NSImage>()
    static func deckImage(_ mapping: Mapping, iconPNG: Data? = nil) -> NSImage? {
        if let data = iconPNG {
            let key = data as NSData
            if let cached = customArtwork.object(forKey:key) { return cached }
            if let custom = NSImage(data:data) {
                customArtwork.countLimit = 256; customArtwork.totalCostLimit = 64*1024*1024
                customArtwork.setObject(custom,forKey:key,cost:max(data.count,Int(custom.size.width*custom.size.height*4)))
                return custom
            }
        }
        if mapping.action == .system, [SystemCommand.finderExisting.rawValue,SystemCommand.finderNewWindow.rawValue].contains(mapping.payload) {
            let finder = NSWorkspace.shared.urlForApplication(withBundleIdentifier:"com.apple.finder")?.path ?? "/System/Library/CoreServices/Finder.app"
            return deckImage(Mapping(layer:.normal,input:0,action:.launch,application:finder))
        }
        if mapping.action == .launch || mapping.action == .file {
            if let cached = appArtwork.object(forKey:mapping.application as NSString) { return cached }
            let image = NSWorkspace.shared.icon(forFile:mapping.application)
            image.size = NSSize(width:128,height:128)
            let fitted = fittedAppArtwork(image)
            appArtwork.countLimit = 256; appArtwork.setObject(fitted,forKey:mapping.application as NSString)
            return fitted
        }
        let symbol: String?
        switch mapping.action {
        case .shortcut: symbol = StudioAction.catalog.first { $0.kind == .shortcut && !$0.parameterized && $0.output == mapping.output && $0.modifiers == mapping.modifiers }?.symbol ?? "command"
        case .text: symbol = "text.cursor"
        case .url: symbol = "link"
        case .deck: symbol = "square.grid.3x3"
        case .window: symbol = "macwindow"
        case .file: symbol = "folder"
        case .system:
            let category = SystemCommand(rawValue:mapping.payload)?.category
            symbol = category == "音量" ? "speaker.wave.2" : (category == "スクリーンショット" ? "camera.viewfinder" : "rectangle.3.group")
        default: symbol = nil
        }
        return symbol.flatMap {
            let image = NSImage(systemSymbolName:$0,accessibilityDescription:mapping.targetDescription)
            image?.isTemplate = true
            return image
        }
    }
    static func color(_ hex: String?) -> NSColor? {
        guard let hex = hex, Configuration.validColor(hex), let value = UInt32(hex, radix: 16) else { return nil }
        return NSColor(srgbRed: CGFloat((value >> 16) & 255) / 255, green: CGFloat((value >> 8) & 255) / 255, blue: CGFloat(value & 255) / 255, alpha: 1)
    }
    static func configureDeckLabel(_ label: NSTextField, text: String) {
        label.stringValue = text; label.toolTip = text
        label.font = .systemFont(ofSize:11)
        label.lineBreakMode = .byTruncatingTail
    }
    static func label(_ text: String, _ size: CGFloat = 12, _ muted: Bool = false) -> NSTextField {
        let field = NSTextField(labelWithString: text)
        field.font = .systemFont(ofSize: size); field.textColor = muted ? Self.muted : Self.text
        field.lineBreakMode = .byTruncatingTail
        return field
    }
}

// Windows ActionPaletteLaunchButton: circular 68 pt target.
private final class ActionLibraryLaunchButton: StudioButton {
    override var tileCornerRadius: CGFloat { min(bounds.width, bounds.height) / 2 }

}

class StudioButton: NSButton {
    var fill: NSColor = .clear { didSet { needsDisplay = true } }
    var holdAssignmentColor: NSColor?
    var preservesAssignmentFill = false
    var tapAssignmentKind: ActionType?
    var holdAssignmentKind: ActionType?
    var tapAssignmentDescription: String?
    var holdAssignmentDescription: String?
    var hasHoldAssignment = false { didSet { needsDisplay = true } }
    var selected = false { didSet { needsDisplay = true } }
    var symbol = "" { didSet { needsDisplay = true } }
    func imageRect(for image: NSImage) -> NSRect {
        NSRect(x:(bounds.width-28)/2,y:(bounds.height-28)/2,width:28,height:28)
    }
    var showsTileChrome: Bool { true }
    var tileCornerRadius: CGFloat { 8 }
    func keyOutline(_ rect: NSRect) -> NSBezierPath { NSBezierPath(roundedRect: rect, xRadius: tileCornerRadius, yRadius: tileCornerRadius) }
    var leftAligned = false
    var fitKeyLabel = false
    var subtitle = ""
    var labelColor: NSColor?
    var tapOnlyDrop = false
    var supportsTap = true
    var supportsHold = true
    var dropTrigger: Trigger = .tap { didSet { if oldValue != dropTrigger { needsDisplay = true } } }
    private var assignmentChooser: NSPopover?
    var drop: ((String) -> Bool)?
    var hoveringDrop = false { didSet { needsDisplay = true } }
    override var isEnabled: Bool {
        didSet { if !isEnabled { pointerInside = false; hoveringDrop = false }; updateHover(); needsDisplay = true }
    }
    private var pointerInside = false
    private var hoverTracking: NSTrackingArea?
    private let hoverLayer = CALayer()
    private let commitLayer = CAShapeLayer()
    private let commitReveal = CAShapeLayer()
    func animateAssignmentCommit() {
        layer?.removeAnimation(forKey:"relyr-assignment-commit")
        commitLayer.removeAllAnimations(); commitReveal.removeAllAnimations()
        CATransaction.begin(); CATransaction.setDisableActions(true)
        commitLayer.opacity = 0
        CATransaction.commit()
        guard !Studio.reduceMotion, window?.isVisible == true, !isHidden else { return }
        layoutSubtreeIfNeeded()
        let radius = hypot(bounds.width,bounds.height)
        let small = CGPath(ellipseIn:NSRect(x:bounds.midX-1,y:bounds.midY-1,width:2,height:2),transform:nil)
        let full = CGPath(ellipseIn:NSRect(x:bounds.midX-radius,y:bounds.midY-radius,width:radius*2,height:radius*2),transform:nil)
        CATransaction.begin(); CATransaction.setDisableActions(true); commitReveal.path = full; CATransaction.commit()
        let spread = CABasicAnimation(keyPath:"path"); spread.fromValue = small; spread.toValue = full
        spread.duration = 0.18; spread.timingFunction = CAMediaTimingFunction(name:.easeOut)
        commitReveal.add(spread,forKey:"spread")
        let paint = CAKeyframeAnimation(keyPath:"opacity")
        paint.values = [0,0.25,0.20,0]; paint.keyTimes = [0,0.15,0.6,1]; paint.duration = 0.30
        commitLayer.add(paint,forKey:"paint")
    }
    func verifyMotion() {
        let initial = frame, count = layer?.sublayers?.count
        Studio.verificationAccessibility = (false,false)
        for _ in 0..<20 { animateAssignmentCommit() }
        verifySmoke(frame == initial && layer?.sublayers?.count == count,"Repeated commit preserves geometry and bounded layer count")
        verifySmoke(commitLayer.animation(forKey:"paint") != nil && commitLayer.opacity == 0,"Commit paint ends at transparent model state")
        isHighlighted = true; isHighlighted = false
        verifySmoke(frame == initial && hoverLayer.opacity == 0,"Quick press release restores visual state without changing hit target")
        Studio.verificationAccessibility = (true,false)
        animateAssignmentCommit(); isHighlighted = false
        verifySmoke(commitLayer.animationKeys()?.isEmpty != false && commitReveal.animationKeys()?.isEmpty != false && hoverLayer.animationKeys()?.isEmpty != false,"Reduced motion removes active paint and hover clocks")
        Studio.verificationAccessibility = nil
    }
    override var isHighlighted: Bool { didSet { updateHover() } }
    override func updateTrackingAreas() {
        super.updateTrackingAreas()
        if let hoverTracking = hoverTracking { removeTrackingArea(hoverTracking) }
        let area = NSTrackingArea(rect: .zero, options: [.mouseEnteredAndExited, self is DeckTile ? .activeAlways : .activeInKeyWindow, .inVisibleRect], owner: self)
        addTrackingArea(area); hoverTracking = area
    }
    private func updateHover() {
        let opacity: Float = isEnabled && showsTileChrome ? (isHighlighted ? 0.16 : (pointerInside ? (Studio.highContrast ? 0.14 : 0.07) : 0)) : 0
        Studio.fade(hoverLayer,to:opacity,duration:isHighlighted ? 0.07 : (pointerInside ? 0.14 : 0.10),key:"hover")
    }
    override func mouseEntered(with event: NSEvent) { pointerInside = true; updateHover() }
    override func mouseExited(with event: NSEvent) { pointerInside = false; updateHover() }
    override func viewDidMoveToWindow() {
        super.viewDidMoveToWindow(); pointerInside = false; updateHover()
        if window == nil { hoverLayer.removeAllAnimations(); commitLayer.removeAllAnimations(); commitReveal.removeAllAnimations(); layer?.removeAnimation(forKey:"studio-reorder") }
    }
    override func layout() {
        super.layout()
        CATransaction.begin(); CATransaction.setDisableActions(true)
        hoverLayer.frame = bounds.insetBy(dx: 2, dy: 2); hoverLayer.cornerRadius = max(0, tileCornerRadius - 1)
        hoverLayer.backgroundColor = NSColor.labelColor.cgColor
        commitLayer.frame = bounds; commitReveal.frame = bounds
        commitLayer.fillColor = Studio.accent.cgColor
        if #available(macOS 14.0, *) { commitLayer.path = keyOutline(bounds.insetBy(dx:1,dy:1)).cgPath }
        else { commitLayer.path = CGPath(roundedRect:bounds.insetBy(dx:1,dy:1),cornerWidth:tileCornerRadius,cornerHeight:tileCornerRadius,transform:nil) }
        CATransaction.commit()
    }
    override func drawFocusRingMask() { NSBezierPath(roundedRect: bounds.insetBy(dx: 2, dy: 2), xRadius: max(0, tileCornerRadius - 1), yRadius: max(0, tileCornerRadius - 1)).fill() }
    override var focusRingMaskBounds: NSRect { bounds }
    override var isFlipped: Bool { true }
    override var alignmentRectInsets: NSEdgeInsets { NSEdgeInsets(top: 0, left: 0, bottom: 0, right: 0) }
    override init(frame: NSRect) { super.init(frame: frame); isBordered = false; setButtonType(.momentaryChange); font = .systemFont(ofSize: 12); wantsLayer = true; hoverLayer.opacity = 0; layer?.addSublayer(hoverLayer); commitLayer.opacity = 0; commitLayer.mask = commitReveal; layer?.addSublayer(commitLayer); focusRingType = .exterior }
    required init?(coder: NSCoder) { fatalError("init(coder:) has not been implemented") }
    convenience init(_ title: String, symbol: String = "", action: @escaping () -> Void) {
        self.init(frame: .zero); self.title = title; self.symbol = symbol; handler = action; target = self; self.action = #selector(invoke)
        setAccessibilityLabel(title)
    }
    var contextBuilder: (() -> NSMenu?)?
    var lastClickModifiers: NSEvent.ModifierFlags = []
    override func mouseDown(with event: NSEvent) { lastClickModifiers = event.modifierFlags; super.mouseDown(with: event) }
    override func menu(for event: NSEvent) -> NSMenu? { contextBuilder?() ?? super.menu(for: event) }
    var handler: (() -> Void)?
    @objc private func invoke() { handler?() }
    override func draw(_ dirtyRect: NSRect) {
        defer {
            if hasHoldAssignment {
                // Match Windows' bottom-right, action-colored HOLD capsule.
                // Draw only: it must never intercept a key click or a drop.
                let badgeFont = NSFont.systemFont(ofSize: 7, weight: .semibold)
                let attributes: [NSAttributedString.Key: Any] = [
                    .font: badgeFont,
                    .foregroundColor: NSColor.white
                ]
                let textSize = ("HOLD" as NSString).size(withAttributes: attributes)
                let badge = NSRect(x: bounds.maxX-textSize.width-11, y: bounds.maxY-textSize.height-5,
                                   width: textSize.width+8, height: textSize.height+2)
                (holdAssignmentColor ?? Studio.accent).setFill()
                let capsule = NSBezierPath(roundedRect: badge, xRadius: 5, yRadius: 5)
                capsule.fill()
                // Keep the badge visible when TAP and HOLD share the same hue.
                NSColor.white.withAlphaComponent(0.45).setStroke()
                capsule.lineWidth = 0.75; capsule.stroke()
                ("HOLD" as NSString).draw(at: NSPoint(x: badge.minX+4, y: badge.minY+1), withAttributes: attributes)
            }
        }
        let rect = bounds.insetBy(dx: 1, dy: 1)
        let path = keyOutline(rect)
        if showsTileChrome {
        (preservesAssignmentFill ? fill : (selected ? Studio.selected : (isHighlighted ? Studio.border : fill))).setFill(); path.fill()
        if fill != .clear || Studio.highContrast {
            (Studio.highContrast ? NSColor.labelColor.withAlphaComponent(0.65) : NSColor.separatorColor).setStroke()
            path.lineWidth = 1 / (window?.backingScaleFactor ?? 2); path.stroke()
        }
        }
        if hoveringDrop {
            // Opaque neutral feedback stays readable over every assignment color.
            Studio.key.setFill(); path.fill()
        }
        if hoveringDrop || selected { Studio.accent.setStroke(); path.lineWidth = hoveringDrop ? 2 : 1; path.stroke() }
        if let image = image, !hoveringDrop {
            let rendered = image.isTemplate ? (image.withSymbolConfiguration(.init(paletteColors: [Studio.text])) ?? image) : image
            rendered.draw(in: imageRect(for: image), from: .zero, operation: .sourceOver, fraction: isEnabled ? 1 : 0.4, respectFlipped: true, hints: nil)
            return
        }
        let foreground = isEnabled ? (labelColor ?? Studio.text) : Studio.muted
        let paragraph = NSMutableParagraphStyle(); paragraph.alignment = leftAligned ? .left : .center; paragraph.lineBreakMode = .byTruncatingTail
        let icon = NSImage(systemSymbolName: symbol, accessibilityDescription: nil)?.withSymbolConfiguration(.init(pointSize:16,weight:.regular))?.withSymbolConfiguration(.init(paletteColors: [foreground]))
        let text = leftAligned || icon != nil || symbol.isEmpty ? title : "\(symbol)   \(title)"
        let labelFont = font ?? NSFont.systemFont(ofSize:12)
        let hasIconAndTitle = icon != nil && !title.isEmpty && !hoveringDrop
        let labelWidth = min(max(0,bounds.width - 44),(text as NSString).size(withAttributes:[.font:labelFont]).width)
        let groupStart = max(4,(bounds.width - (20 + 8 + labelWidth))/2)
        if let icon = icon, !hoveringDrop {
            let x: CGFloat = leftAligned ? 12 : (hasIconAndTitle ? groupStart : (bounds.width - 20)/2)
            icon.draw(in: NSRect(x:x,y:(bounds.height - 20)/2,width:20,height:20), from:.zero,operation:.sourceOver,fraction:1,respectFlipped:true,hints:nil)
        }
        let inset: CGFloat = leftAligned ? (symbol.isEmpty ? 12 : 44) : (hasIconAndTitle ? groupStart + 28 : 4)
        if hasIconAndTitle && !leftAligned { paragraph.alignment = .left }
        if hoveringDrop && tapOnlyDrop {
            // Keep the key recognizable; the accent outline communicates the target.
            (title as NSString).draw(in:NSRect(x:3,y:(bounds.height-20)/2,width:bounds.width-6,height:22),withAttributes:[.font:NSFont.systemFont(ofSize:13,weight:.semibold),.foregroundColor:Studio.text,.paragraphStyle:paragraph])
            return
        }
        let available = hasIconAndTitle && !leftAligned ? max(1,labelWidth) : max(1, bounds.width - (fitKeyLabel ? 4 : inset + 8))
        let base = font ?? NSFont.systemFont(ofSize: 12)
        let measured = (text as NSString).size(withAttributes: [.font: base]).width
        let size = fitKeyLabel ? min(base.pointSize, max(7, base.pointSize * available / max(1, measured))) : base.pointSize
        let drawingFont = fitKeyLabel ? NSFont.systemFont(ofSize: size) : base
        // Half-height arrow keys put the label beside the badge, not underneath it.
        let compactHold = hasHoldAssignment && bounds.height < 32 && subtitle.isEmpty
        if compactHold { paragraph.alignment = .left }
        let y = subtitle.isEmpty ? (hasHoldAssignment && !compactHold ? max(2, min((bounds.height - size - 3) / 2, bounds.height - size - 18)) : (bounds.height - size - 3) / 2) : 9
        (hoveringDrop ? (tapOnlyDrop && supportsTap && supportsHold ? "↓ 選択" : "↓ 配置") : text as NSString).draw(in: NSRect(x: fitKeyLabel ? 2 : inset, y: y, width: available, height: size + 6),
            withAttributes: [.font: drawingFont, .foregroundColor: foreground, .paragraphStyle: paragraph])
        if !subtitle.isEmpty {
            (subtitle as NSString).draw(in: NSRect(x: leftAligned ? 12 : 4, y: 28, width: bounds.width - 24, height: 16),
                withAttributes: [.font: NSFont.systemFont(ofSize: 10), .foregroundColor: labelColor ?? Studio.muted, .paragraphStyle: paragraph])
        }
    }
    func enableDrop(_ handler: @escaping (String) -> Bool) { drop = handler; registerForDraggedTypes([Studio.dragType]) }
    override func draggingEntered(_ sender: NSDraggingInfo) -> NSDragOperation {
        guard isEnabled, drop != nil, sender.draggingPasteboard.string(forType: Studio.dragType) != nil else { return [] }
        return draggingUpdated(sender)
    }
    func updateDrop(at point: NSPoint) -> NSDragOperation {
        dropTrigger = tapOnlyDrop && point.y >= bounds.midY ? .hold : .tap
        let allowed = isEnabled && drop != nil && bounds.contains(point) &&
            (dropTrigger == .hold ? supportsHold : supportsTap)
        hoveringDrop = allowed
        return allowed ? .copy : []
    }
    override func draggingUpdated(_ sender: NSDraggingInfo) -> NSDragOperation {
        guard sender.draggingPasteboard.string(forType: Studio.dragType) != nil else { hoveringDrop = false; return [] }
        let point = convert(sender.draggingLocation, from:nil)
        if tapOnlyDrop {
            hoveringDrop = isEnabled && drop != nil && bounds.contains(point) && (supportsTap || supportsHold)
            return hoveringDrop ? .copy : []
        }
        return updateDrop(at:point)
    }
    override func draggingExited(_ sender: NSDraggingInfo?) { hoveringDrop = false }
    override func draggingEnded(_ sender: NSDraggingInfo) { hoveringDrop = false }
    override func performDragOperation(_ sender: NSDraggingInfo) -> Bool {
        defer { hoveringDrop = false }
        guard draggingUpdated(sender) != [], isEnabled, let value = sender.draggingPasteboard.string(forType: Studio.dragType) else { return false }
        if tapOnlyDrop && supportsTap && supportsHold {
            showAssignmentChooser(value)
            return true
        }
        dropTrigger = supportsTap ? .tap : .hold
        return drop?(value) ?? false
    }
    func verifyAssignmentChooser() {
        var calls: [Trigger] = []
        tapAssignmentKind = nil; holdAssignmentKind = .system
        holdAssignmentDescription = "RELYRを表示"
        enableDrop { [weak self] _ in if let self = self { calls.append(self.dropTrigger) }; return true }
        for trigger in [Trigger.tap,.hold] {
            showAssignmentChooser("fixture")
            guard let body = assignmentChooser?.contentViewController?.view else { verifySmoke(false,"Drop chooser opens"); return }
            if trigger == .tap { saveSmokePreview(body,to:URL(fileURLWithPath:CommandLine.arguments[2]).deletingLastPathComponent().appendingPathComponent("assignment-chooser.png").path) }
            let choices = body.subviews.compactMap { $0 as? StudioButton }.filter { $0.identifier?.rawValue != "cancel-assignment" }
            verifySmoke(choices.count == 2 && choices.allSatisfy { $0.frame.height >= 48 && body.bounds.contains($0.frame) },"Large TAP/HOLD targets remain inside chooser")
            verifySmoke(choices[0].fill == Studio.key && choices[1].fill == Studio.assignmentFill(.system),"Only assigned HOLD uses action color")
            verifySmoke(choices[1].preservesAssignmentFill,"Assigned color survives press and hover")
            choices[trigger == .tap ? 0 : 1].handler?()
            verifySmoke(calls.last == trigger && assignmentChooser == nil,"Explicit choice commits correct trigger exactly once")
        }
        showAssignmentChooser("cancel")
        let cancel = assignmentChooser?.contentViewController?.view.subviews.first { $0.identifier?.rawValue == "cancel-assignment" } as? StudioButton
        verifySmoke(cancel != nil,"Explicit Cancel is available")
        cancel?.handler?()
        verifySmoke(calls == [.tap,.hold],"Dismissal does not assign")
    }
    private func showAssignmentChooser(_ value: String) {
        assignmentChooser?.close()
        guard window != nil else { return }
        let popover = NSPopover(); popover.behavior = .transient; popover.animates = !NSWorkspace.shared.accessibilityDisplayShouldReduceMotion
        let controller = NSViewController()
        let body = StudioSurface(frame:NSRect(x:0,y:0,width:320,height:234))
        let heading = Studio.label(title,15)
        heading.frame = NSRect(x:18,y:14,width:240,height:24); body.addSubview(heading)
        for (index,trigger) in [Trigger.tap,.hold].enumerated() {
            let label = trigger == .tap ? "TAP" : "HOLD"
            let choice = StudioButton(label) { [weak self, weak popover] in
                guard let self = self, self.isEnabled else { popover?.close(); return }
                self.dropTrigger = trigger
                popover?.close(); self.assignmentChooser = nil
                _ = self.drop?(value)
            }
            choice.frame = NSRect(x:16,y:46+index*86,width:288,height:72)
            let existing = (trigger == .tap ? tapAssignmentDescription : holdAssignmentDescription) ?? "未割り当て"
            choice.subtitle = existing
            choice.toolTip = nil
            choice.leftAligned = true
            let kind = trigger == .tap ? tapAssignmentKind : holdAssignmentKind
            choice.fill = kind.map { Studio.assignmentFill($0) } ?? Studio.key
            choice.labelColor = kind.map { Studio.assignmentText($0) }
            choice.preservesAssignmentFill = kind != nil
            choice.setAccessibilityLabel(title + "：" + label + "。現在：" + existing)
            body.addSubview(choice)
        }
        let cancel = StudioButton("") { [weak self, weak popover] in popover?.close(); self?.assignmentChooser = nil }
        cancel.identifier = NSUserInterfaceItemIdentifier("cancel-assignment")
        cancel.fill = NSColor.systemRed.withAlphaComponent(0.16)
        cancel.labelColor = .systemRed
        cancel.symbol = "xmark"
        cancel.toolTip = "キャンセル（Esc）"
        cancel.setAccessibilityLabel("キャンセル")
        cancel.frame = NSRect(x:270,y:10,width:34,height:30); cancel.keyEquivalent = "\u{1b}"
        body.addSubview(cancel)
        controller.view = body; popover.contentViewController = controller
        assignmentChooser = popover
        popover.show(relativeTo:bounds,of:self,preferredEdge:.maxY)
    }
}

struct ActionOrigin: Codable, Equatable {
    var profile: String
    var mapping: Mapping
}

struct StudioAction: Codable, Equatable {
    var id: String
    var name: String
    var category: String
    var symbol: String
    var kind: ActionType
    var output: UInt16 = 0
    var modifiers: Modifiers = []
    var value: String = ""
    var parameterized = false
    var origin: ActionOrigin? = nil
    var displayLayout: KeyboardLayout? = nil
    var deckMonitor: DeckMonitor? { id.hasPrefix("deck-monitor-") ? DeckMonitor(rawValue:String(id.dropFirst("deck-monitor-".count))) : nil }
    static var monitorActions: [StudioAction] { DeckMonitor.allCases.map { StudioAction(id:"deck-monitor-"+$0.rawValue,name:$0.title,category:"Deckモニター",symbol:"gauge.with.dots.needle.50percent",kind:$0 == .autoExtract ? .system : .disabled,value:$0 == .autoExtract ? SystemCommand.toggleAutoExtract.rawValue : "") } }
    var detail: String {
        if kind == .system && !modifiers.isEmpty && ["scrollUp","scrollDown","scrollLeft","scrollRight"].contains(value) { return modifiers.label + "スクロール · ホイールへの割り当ては元の量・滑らかさを保持" }
        if deckMonitor != nil { return "Deck専用モニター · 表示中だけ更新" }
        if kind == .window, WindowCommand(rawValue:value)?.isSpaceMove == true { return "ウィンドウと一緒に移動先のデスクトップへ切り替え · 全画面は解除して移動 · 対応macOSのみ" }
        if kind == .window && value == WindowCommand.closeSurface.rawValue { return "対象画面を閉じる · Finder・全画面対応 · 保存確認あり" }
        if kind == .window && value == WindowCommand.close.rawValue { return "対象ウィンドウの赤い閉じるボタンを押す" }
        if kind == .shortcut && modifiers == .command && output == 12 { return "アプリ全体を終了 · Finderは対象外 · ⌘Q" }
        if kind == .shortcut && modifiers == .command && output == 13 { return "使用中のタブ／書類を閉じる · ⌘W" }
        if kind == .system && value == SystemCommand.screenshotAreaClipboard.rawValue { return "範囲をドラッグで選択 → 画像をコピー → ⌘Vで貼り付け" }

        if kind == .system, let command = SystemCommand(rawValue:value) {
            switch command {
            case .keyboardCleaner: return "掃除中のキー入力を一時停止"
            case .desktopLeft,.desktopRight: return "macOSのMission ControlでControl＋矢印のショートカットを有効にしてください"
            case .brightnessUp,.brightnessDown: return "macOSの輝度キーに対応する画面のみ。外部モニターは機器依存"
            case .volumeUp,.volumeDown,.mute: return "現在の音声出力を操作。音量調整に対応する機器が必要"
            case .navigateBack,.navigateForward: return "戻る／進むに対応するブラウザーやFinderで使用"
            default: break
            }
        }
        if kind == .file && !parameterized {
            var directory: ObjCBool = false
            if FileManager.default.fileExists(atPath:value,isDirectory:&directory), directory.boolValue, !NSWorkspace.shared.isFilePackage(atPath:value) {
                return "Finderで開く · 同じフォルダーが開いていれば前面に表示"
            }
        }
        if kind == .gesture { return "押したまま動かす · 上下左右／中央" }
        if kind == .system, let number = SystemCommand(rawValue:value)?.desktopNumber { return "⌃\(number)（macOS側で有効化）" }
        if !parameterized {
            switch kind {
            case .macro: return "登録したマクロを再生"
            case .profile: return "使用するプロファイルを切り替え"
            case .deck: return "登録したDeckパネルを表示／非表示"
            case .window: return "アクティブウィンドウの位置・サイズを変更"
            case .system: return "macOSの操作"
            default: break
            }
        }
        return parameterized ? "クリックまたはドロップして内容を設定" : kind.isKey ? modifiers.label + Keys.name(output, layout: displayLayout ?? .us) : value.isEmpty ? kind.title : value }
    func mapping(layer: Layer, input: UInt16) -> Mapping {
        var resolvedOutput = output
        var resolvedModifiers = modifiers
        if ["parity-browser-back","parity-finder-back"].contains(id) { resolvedOutput = displayLayout == .jis ? 30 : 33 }
        if ["parity-browser-forward","parity-finder-forward"].contains(id) { resolvedOutput = displayLayout == .jis ? 42 : 30 }
        if ["parity-window-next","parity-window-previous"].contains(id) { resolvedOutput = displayLayout == .jis ? 33 : 50 }
        if id == "parity-browser-zoom-in", displayLayout == .jis { resolvedOutput = 41; resolvedModifiers.insert(.shift) }
        return Mapping(layer: layer, input: input, action: kind, output: resolvedOutput, modifiers: resolvedModifiers,
                application: kind == .launch || kind == .file ? value : "", payload: kind == .text || kind == .url || kind == .window || kind == .system || kind == .gesture || kind == .macro || kind == .profile || kind == .deck ? value : "")
    }
    static let catalog: [StudioAction] = {
        var values: [StudioAction] = []
        for (name, code, symbol) in [("コピー", 8, "doc.on.doc"), ("貼り付け", 9, "doc.on.clipboard"), ("切り取り", 7, "scissors"),
                                     ("元に戻す", 6, "arrow.uturn.backward"), ("保存", 1, "square.and.arrow.down"),
                                     ("すべて選択", 0, "selection.pin.in.out"), ("検索", 3, "magnifyingglass")] {
            values.append(StudioAction(id: "cmd-\(code)", name: name, category: "基本操作", symbol: symbol, kind: .shortcut, output: UInt16(code), modifiers: .command))
        }
        values.append(StudioAction(id: "redo", name: "やり直す", category: "基本操作", symbol: "arrow.uturn.forward", kind: .shortcut, output: 6, modifiers: [.command, .shift]))
        values.append(StudioAction(id: "symbol-question", name: "?", category: "キー", symbol: "questionmark", kind: .shortcut, output: 44, modifiers: .shift))
        for (name, flags) in [("Shift＋左クリック", Modifiers.shift), ("Control＋左クリック", .control), ("Command＋左クリック", .command), ("Option＋左クリック", .option)] {
            values.append(StudioAction(id:"mouse-\(flags.rawValue)",name:name,category:"マウス",symbol:"computermouse",kind:.mouse,output:0,modifiers:flags))
        }
        for (name, button) in [("左クリック",0),("右クリック",1),("中央クリック",2),("戻るボタンを送信",3),("進むボタンを送信",4)] {
            values.append(StudioAction(id:"mouse-button-\(button)",name:name,category:"マウス",symbol:"computermouse",kind:.mouse,output:UInt16(button)))
        }
        for button in 5...31 {
            values.append(StudioAction(id:"mouse-button-\(button)",name:"マウスボタン番号 \(button) を押す",category:"マウス",symbol:"computermouse",kind:.mouse,output:UInt16(button)))
        }
        values += Keys.outputs.map { StudioAction(id: "key-\($0.code)", name: $0.name, category: "キー", symbol: "keyboard", kind: .key, output: $0.code) }
        values += [StudioAction(id: "shortcut", name: "カスタムショートカット", category: "カスタム", symbol: "command", kind: .shortcut, parameterized: true),
                   StudioAction(id: "launch", name: "アプリを起動", category: "アプリ・URL", symbol: "app", kind: .launch, parameterized: true),
                   StudioAction(id: "text", name: "文字列入力（定型文）", category: "カスタム", symbol: "text.cursor", kind: .text, parameterized: true),
                   StudioAction(id: "url", name: "URLを開く", category: "アプリ・URL", symbol: "link", kind: .url, parameterized: true),
                   StudioAction(id: "deck", name: "Deckパネルを表示／非表示", category: "Deckパネル", symbol: "square.grid.3x3", kind: .deck)]
        values += WindowCommand.allCases.map { StudioAction(id: "window-" + $0.rawValue, name: $0.title, category: "ウィンドウ", symbol: "macwindow", kind: .window, value: $0.rawValue) }
        values.append(StudioAction(id: "file", name: "ファイル・フォルダを開く", category: "アプリ・URL", symbol: "folder", kind: .file, parameterized: true))
        for command in [SystemCommand.scrollUp,.scrollDown,.scrollLeft,.scrollRight] {
            values.append(StudioAction(id:"command-wheel-"+command.rawValue,name:"Command＋"+command.title,category:"マウス",symbol:"computermouse",kind:.system,modifiers:.command,value:command.rawValue))
        }
        values += SystemCommand.allCases.map { StudioAction(id:"system-" + $0.rawValue,name:$0.title,category:$0.category,symbol:$0 == .keyboardCleaner ? "keyboard" : ($0.category == "音量" ? "speaker.wave.2" : "macwindow"),kind:.system,value:$0.rawValue) }
        let extras: [(String, String, UInt16, Modifiers)] = [
            ("ファイルを移動して貼り付け","基本操作",9,[.command,.option]),
            ("新規ウィンドウ","基本操作",45,.command),
            ("新規タブ","基本操作",17,.command),
            ("ウィンドウ／タブを閉じる（⌘W）","基本操作",13,.command),
            ("閉じたタブを開く","基本操作",17,[.command,.shift]),
            ("次のタブ","基本操作",48,.control),
            ("前のタブ","基本操作",48,[.control,.shift]),
            ("再読み込み","基本操作",15,.command),
            ("印刷","基本操作",35,.command),
            ("実際のサイズ","基本操作",29,.command),
            ("アプリを終了（⌘Q）","基本操作",12,.command)]
        values += extras.enumerated().map { i, item in StudioAction(id:"mac-extra-\(i)",name:item.0,category:item.1,symbol:"keyboard",kind:.shortcut,output:item.2,modifiers:item.3) }
        values.append(StudioAction(id:"input-disabled",name:"この入力を無効化",category:"基本操作",symbol:"nosign",kind:.disabled))
        values += Self.macParityActions
        return values
    }()
}

private final class CustomActionEntry: StudioButton, NSDraggingSource {
    var template: StudioAction?
    private var pressLocation: NSPoint?
    override func mouseDown(with event: NSEvent) {
        guard isEnabled else { return }
        pressLocation = event.locationInWindow
        highlight(true)
    }
    override func mouseUp(with event: NSEvent) {
        let clicked = pressLocation != nil && bounds.contains(convert(event.locationInWindow,from:nil))
        pressLocation = nil; highlight(false)
        if clicked { performClick(nil) }
    }
    override func mouseDragged(with event: NSEvent) {
        guard isEnabled, let start = pressLocation, let template = template,
              hypot(event.locationInWindow.x-start.x,event.locationInWindow.y-start.y) >= 5 else { return }
        pressLocation = nil; highlight(false)
        guard let data = try? JSONEncoder().encode(template), let json = String(data:data,encoding:.utf8) else { return }
        let board = NSPasteboardItem(); board.setString(json,forType:Studio.dragType)
        let item = NSDraggingItem(pasteboardWriter:board)
        let image = NSImage(size:bounds.size)
        image.lockFocus()
        Studio.key.setFill(); NSBezierPath(roundedRect:NSRect(origin:.zero,size:bounds.size),xRadius:8,yRadius:8).fill()
        (template.name as NSString).draw(at:NSPoint(x:12,y:10),withAttributes:[.font:NSFont.systemFont(ofSize:12,weight:.medium),.foregroundColor:Studio.text])
        image.unlockFocus()
        let point = convert(event.locationInWindow,from:nil)
        item.setDraggingFrame(NSRect(x:point.x-20,y:point.y-18,width:bounds.width,height:bounds.height),contents:image)
        beginDraggingSession(with:[item],event:event,source:self)
    }
    func draggingSession(_ session:NSDraggingSession,sourceOperationMaskFor context:NSDraggingContext)->NSDragOperation { .copy }
}

// A help bubble must never activate or consume the click that begins a drag.
private final class ActionHelpPanel: NSPanel {
    override var canBecomeKey: Bool { false }
    override var canBecomeMain: Bool { false }
    init(content: NSView) {
        super.init(contentRect:content.bounds,styleMask:[.borderless,.nonactivatingPanel],backing:.buffered,defer:false)
        isReleasedWhenClosed = false; ignoresMouseEvents = true; hidesOnDeactivate = true
        isOpaque = false; backgroundColor = .clear; hasShadow = true
        level = .popUpMenu; animationBehavior = .none; contentView = content
    }
}

private final class ActionRow: NSView, NSDraggingSource {
    let value: StudioAction
    var star: StudioButton!
    private var hoverWork: DispatchWorkItem?
    private var helpPopover: ActionHelpPanel?
    private static weak var activeHelpRow: ActionRow?
    private var helpEventMonitor: Any?
    private var helpGeneration = 0
    private var hoverArea: NSTrackingArea?
    override func updateTrackingAreas() {
        super.updateTrackingAreas()
        if let hoverArea = hoverArea { removeTrackingArea(hoverArea) }
        let area = NSTrackingArea(rect:.zero,options:[.mouseEnteredAndExited,.activeInActiveApp,.inVisibleRect],owner:self,userInfo:nil)
        addTrackingArea(area); hoverArea = area
    }
    private func closeHelp() {
        helpGeneration += 1
        hoverWork?.cancel(); hoverWork = nil
        if let monitor = helpEventMonitor { NSEvent.removeMonitor(monitor); helpEventMonitor = nil }
        if Self.activeHelpRow === self { Self.activeHelpRow = nil }
        if let panel = helpPopover { panel.parent?.removeChildWindow(panel); panel.close() }
        helpPopover = nil
    }
    override func mouseEntered(with event:NSEvent) {
        Self.activeHelpRow?.closeHelp()
        closeHelp(); Self.activeHelpRow = self
        let generation = helpGeneration
        let work = DispatchWorkItem { [weak self] in
            guard let self = self, self.helpGeneration == generation, Self.activeHelpRow === self,
                  NSEvent.pressedMouseButtons == 0 else { return }
            self.showHelp()
        }
        hoverWork = work; DispatchQueue.main.asyncAfter(deadline:.now()+0.15,execute:work)
    }
    private func showHelp() {
        guard let window = window,window.isVisible else { return }
        Self.activeHelpRow?.closeHelp()
        closeHelp(); Self.activeHelpRow = self
        let width: CGFloat = 300
        let title = NSTextField(wrappingLabelWithString:value.name)
        title.font = .systemFont(ofSize:13,weight:.semibold); title.textColor = .labelColor
        let detail = NSTextField(wrappingLabelWithString:value.detail == value.name ? "" : value.detail)
        detail.font = .systemFont(ofSize:12); detail.textColor = .secondaryLabelColor
        func height(_ field:NSTextField)->CGFloat { field.cell?.cellSize(forBounds:NSRect(x:0,y:0,width:width,height:1000)).height ?? 20 }
        let titleHeight = max(20,height(title)), detailHeight = detail.stringValue.isEmpty ? 0 : max(18,height(detail))
        let total = 28 + titleHeight + (detailHeight > 0 ? detailHeight+8 : 0)
        let body = NSVisualEffectView(frame:NSRect(x:0,y:0,width:width+28,height:total))
        body.material = .popover; body.state = .active; body.wantsLayer = true
        body.layer?.cornerRadius = 10; body.layer?.masksToBounds = true
        title.frame = NSRect(x:14,y:total-14-titleHeight,width:width,height:titleHeight)
        detail.frame = NSRect(x:14,y:14,width:width,height:detailHeight)
        body.addSubview(title); body.addSubview(detail)
        let panel = ActionHelpPanel(content:body); panel.appearance = effectiveAppearance
        let anchor = window.convertToScreen(convert(bounds,to:nil))
        let screen = window.screen?.visibleFrame ?? window.frame
        var origin = NSPoint(x:anchor.minX-body.bounds.width-8,y:anchor.midY-total/2)
        if origin.x < screen.minX+8 { origin.x = anchor.maxX+8 }
        origin.x = max(screen.minX+8,min(origin.x,screen.maxX-body.bounds.width-8))
        origin.y = max(screen.minY+8,min(origin.y,screen.maxY-total-8))
        panel.setFrameOrigin(origin); helpPopover = panel
        window.addChildWindow(panel,ordered:.above); panel.orderFront(nil)
        helpEventMonitor = NSEvent.addLocalMonitorForEvents(matching:[.scrollWheel,.leftMouseDown,.rightMouseDown,.otherMouseDown,.keyDown,.mouseMoved]) { [weak self] event in
            guard let self = self else { return event }
            if event.type != .mouseMoved || event.window !== self.window || !self.visibleRect.contains(self.convert(event.locationInWindow,from:nil)) {
                self.closeHelp()
            }
            return event
        }
    }
    func verifyPassiveHelp() {
        let previousKey = NSApp.keyWindow
        showHelp()
        let first = helpPopover
        showHelp()
        verifySmoke(first?.isVisible == false && first?.parent == nil,"Repeated help replaces and detaches previous panel")
        let other = ActionRow(value,favorite:false,toggle:{})
        superview?.addSubview(other); other.showHelp()
        verifySmoke(helpPopover == nil && other.helpPopover?.isVisible == true,"Moving across actions leaves only one bubble")
        other.closeHelp(); other.removeFromSuperview(); showHelp()
        verifySmoke(helpPopover?.ignoresMouseEvents == true && helpPopover?.canBecomeKey == false && helpPopover?.canBecomeMain == false,"Action help cannot intercept mouse or focus")
        verifySmoke(NSApp.keyWindow === previousKey,"Action help preserves drag-source window focus")
        guard let event = NSEvent.mouseEvent(with:.leftMouseDown,location:.zero,modifierFlags:[],timestamp:0,windowNumber:window?.windowNumber ?? 0,context:nil,eventNumber:0,clickCount:1,pressure:1) else { return }
        mouseDown(with:event)
        verifySmoke(helpPopover == nil && hoverWork == nil,"First mouse down dismisses help and cancels pending help before dragging")
    }
    override func mouseExited(with event:NSEvent) { closeHelp() }
    override func viewWillMove(toWindow newWindow:NSWindow?) { if newWindow == nil { closeHelp() }; super.viewWillMove(toWindow:newWindow) }
    deinit { hoverWork?.cancel(); if let monitor = helpEventMonitor { NSEvent.removeMonitor(monitor) }; if let panel = helpPopover { panel.parent?.removeChildWindow(panel); panel.close() } }
    override var isFlipped: Bool { true }
    init(_ action: StudioAction, favorite: Bool, toggle: @escaping () -> Void) {
        value = action; super.init(frame: NSRect(x: 0, y: 0, width: 252, height: 58))
        let fullDescription = action.detail.isEmpty || action.detail == action.name ? action.name : action.name + "\n\n" + action.detail
        setAccessibilityHelp(fullDescription)
        setAccessibilityElement(true); setAccessibilityRole(.group); setAccessibilityLabel("\(action.name)、\(action.detail)、ドラッグして割り当て")
        let icon = NSImageView(frame: NSRect(x: 9, y: 16, width: 22, height: 22))
        icon.image = NSImage(systemSymbolName: action.symbol, accessibilityDescription: action.name); icon.contentTintColor = Studio.assignmentColor(action.kind); addSubview(icon)
        let name = Studio.label(action.name, 12); name.frame = NSRect(x: 43, y: 9, width: 166, height: 19); addSubview(name)
        let detail = Studio.label(action.detail, 10, true); detail.frame = NSRect(x: 43, y: 30, width: 166, height: 16); addSubview(detail)
        if action.id.hasPrefix("summary:") {
            let wraps = (action.name as NSString).size(withAttributes:[.font:name.font ?? NSFont.systemFont(ofSize:12)]).width > 166
            name.frame = NSRect(x:43,y:wraps ? 7 : 17,width:166,height:wraps ? 42 : 20); name.maximumNumberOfLines = wraps ? 2 : 1; name.lineBreakMode = .byTruncatingTail; name.setAccessibilityHelp(action.name)
            detail.isHidden = true; icon.contentTintColor = Studio.assignmentColor(action.kind)
        }
        for child in [icon, name, detail] as [NSView] { child.setAccessibilityHelp(fullDescription) }
        star = StudioButton(favorite ? "★" : "☆", action: toggle); star.frame = NSRect(x: 214, y: 13, width: 28, height: 30)
        star.setAccessibilityLabel(favorite ? "\(action.name)をお気に入りから削除" : "\(action.name)をお気に入りに追加"); addSubview(star)
    }
    var removeCustom: (() -> Void)? {
        didSet {
            guard removeCustom != nil else { return }
            star.title = ""; star.image = NSImage(systemSymbolName:"trash",accessibilityDescription:"削除")
            star.setAccessibilityLabel("\(value.name)を一覧から削除")
            star.toolTip = "一覧から削除（割り当て済みの動作は残ります）"
        }
    }
    var manageCustom: ((Int) -> Void)?
    @objc private func manageCustomItem(_ sender: NSMenuItem) { closeHelp(); manageCustom?(sender.tag) }
    @objc private func deleteCustomItem() { closeHelp(); removeCustom?() }
    override func menu(for event: NSEvent) -> NSMenu? {
        guard removeCustom != nil else { return nil }
        closeHelp()
        let menu = NSMenu()
        for (tag,title) in ["内容を編集…","名前を変更…","使用先を確認…"].enumerated() {
            let entry = NSMenuItem(title:title,action:#selector(manageCustomItem(_:)),keyEquivalent:"")
            entry.tag = tag; entry.target = self; menu.addItem(entry)
        }
        menu.addItem(.separator())
        let item = NSMenuItem(title:"一覧から削除",action:#selector(deleteCustomItem),keyEquivalent:"")
        item.target = self; menu.addItem(item); return menu
    }
    func revealCreated() {
        wantsLayer = true
        layer?.cornerRadius = 8
        layer?.backgroundColor = Studio.accent.withAlphaComponent(0.16).cgColor
        guard !NSWorkspace.shared.accessibilityDisplayShouldReduceMotion else { return }
        let pulse = CABasicAnimation(keyPath:"backgroundColor")
        pulse.fromValue = Studio.accent.withAlphaComponent(0.55).cgColor
        pulse.toValue = Studio.accent.withAlphaComponent(0.16).cgColor
        pulse.duration = 0.7; layer?.add(pulse,forKey:"created-action")
    }
    required init?(coder: NSCoder) { fatalError("init(coder:) has not been implemented") }
    override func layout() {
        super.layout()
        star.frame.origin.x = max(0,bounds.width-38)
        for field in subviews.compactMap({ $0 as? NSTextField }) { field.frame.size.width = max(0,bounds.width-86) }
    }
    override func hitTest(_ point: NSPoint) -> NSView? {
        guard let hit = super.hitTest(point) else { return nil }
        return hit === star ? star : self
    }
    var onChoose: (() -> Void)?
    private var choosing = false
    override func mouseDown(with event: NSEvent) { closeHelp(); choosing = true }
    override func mouseUp(with event: NSEvent) {
        let shouldChoose = choosing && bounds.contains(convert(event.locationInWindow,from:nil))
        choosing = false
        if shouldChoose { onChoose?() }
    }
    override func mouseDragged(with event: NSEvent) {
        choosing = false; closeHelp()
        var payload = value
        if event.modifierFlags.contains(.option) || event.modifierFlags.contains(.control) { payload.origin = nil }
        guard let data = try? JSONEncoder().encode(payload), let json = String(data: data, encoding: .utf8) else { return }
        let pasteboard = NSPasteboardItem(); pasteboard.setString(json, forType: Studio.dragType)
        let item = NSDraggingItem(pasteboardWriter: pasteboard)
        let image = NSImage(size: NSSize(width: 210, height: 42))
        image.lockFocus(); Studio.key.setFill(); NSBezierPath(roundedRect: NSRect(x: 0, y: 0, width: 210, height: 42), xRadius: 8, yRadius: 8).fill()
        (value.name as NSString).draw(at: NSPoint(x: 12, y: 22), withAttributes: [.font: NSFont.systemFont(ofSize: 12, weight: .semibold), .foregroundColor: Studio.text])
        (value.category as NSString).draw(at: NSPoint(x: 12, y: 7), withAttributes: [.font: NSFont.systemFont(ofSize: 10), .foregroundColor: Studio.muted]); image.unlockFocus()
        let point = convert(event.locationInWindow, from: nil)
        item.setDraggingFrame(NSRect(x: point.x + 22, y: point.y + 24, width: 210, height: 42), contents: image)
        beginDraggingSession(with: [item], event: event, source: self)
    }
    func draggingSession(_ session: NSDraggingSession, sourceOperationMaskFor context: NSDraggingContext) -> NSDragOperation { .copy }
}

private final class CustomOutputPicker: NSPopUpButton {
    var onChange: (() -> Void)?
    @objc func changed() { onChange?() }
}

// Apple JIS Return has a narrow lower stem; its empty corner is not a hit target.
private final class MacKeyboardKey: StudioButton {
    var jisReturn = false
    override var tileCornerRadius: CGFloat { 5 }
    override func keyOutline(_ rect: NSRect) -> NSBezierPath {
        guard jisReturn else { return super.keyOutline(rect) }
        let notch = bounds.width * (0.25 / 1.5)
        let shoulder = bounds.height * (50.0 / 106.0)
        let points = [NSPoint(x:rect.minX,y:rect.minY), NSPoint(x:rect.maxX,y:rect.minY),
                      NSPoint(x:rect.maxX,y:rect.maxY), NSPoint(x:rect.minX+notch,y:rect.maxY),
                      NSPoint(x:rect.minX+notch,y:shoulder), NSPoint(x:rect.minX,y:shoulder)]
        let path = NSBezierPath(); let radius = tileCornerRadius
        for i in points.indices {
            let p = points[i], prev = points[(i+points.count-1)%points.count], next = points[(i+1)%points.count]
            func near(_ q:NSPoint) -> NSPoint {
                let length = hypot(q.x-p.x,q.y-p.y)
                return NSPoint(x:p.x+(q.x-p.x)*radius/length,y:p.y+(q.y-p.y)*radius/length)
            }
            let start = near(prev), end = near(next)
            if i == 0 { path.move(to:start) } else { path.line(to:start) }
            let k: CGFloat = 0.5522847498307936
            let c1 = NSPoint(x:start.x+(p.x-start.x)*k,y:start.y+(p.y-start.y)*k)
            let c2 = NSPoint(x:end.x+(p.x-end.x)*k,y:end.y+(p.y-end.y)*k)
            path.curve(to:end,controlPoint1:c1,controlPoint2:c2)
        }
        path.close(); return path
    }
    override func draw(_ dirtyRect:NSRect) {
        NSGraphicsContext.saveGraphicsState()
        keyOutline(bounds.insetBy(dx:1,dy:1)).addClip()
        super.draw(dirtyRect)
        NSGraphicsContext.restoreGraphicsState()
    }
    override func hitTest(_ point:NSPoint) -> NSView? {
        guard keyOutline(bounds).contains(convert(point,from:superview)) else { return nil }
        return super.hitTest(point)
    }
    override func updateDrop(at point:NSPoint) -> NSDragOperation {
        guard keyOutline(bounds).contains(point) else { hoveringDrop = false; return [] }
        return super.updateDrop(at:point)
    }
    override func drawFocusRingMask() { keyOutline(bounds.insetBy(dx:2,dy:2)).fill() }
    override func layout() {
        super.layout()
        guard jisReturn, let layer = layer else { return }
        let outline = keyOutline(bounds)
        if #available(macOS 14.0, *) {
            let mask = CAShapeLayer(); mask.path = outline.cgPath; layer.mask = mask
            return
        }
        // macOS 13 has only the original four path element kinds.
        let cg = CGMutablePath()
        var p = [NSPoint](repeating:.zero,count:3)
        for i in 0..<outline.elementCount {
            switch outline.element(at:i,associatedPoints:&p) {
            case .moveTo: cg.move(to:p[0])
            case .lineTo: cg.addLine(to:p[0])
            case .curveTo: cg.addCurve(to:p[2],control1:p[0],control2:p[1])
            case .closePath: cg.closeSubpath()
            default: break
            }
        }
        let mask = CAShapeLayer(); mask.path = cg; layer.mask = mask
    }
}

// Each assignment carries its own physical gesture diagram.
private final class TrackpadGestureButton: StudioButton {
    var gestureCode = 0
    var fingers = 1
    override func draw(_ dirtyRect: NSRect) {
        NSGraphicsContext.saveGraphicsState()
        defer { NSGraphicsContext.restoreGraphicsState() }
        let transform = NSAffineTransform()
        transform.scaleX(by:bounds.width/302,yBy:bounds.height/94)
        transform.concat()
        let bounds = NSRect(x:0,y:0,width:302,height:94)
        let box = NSBezierPath(roundedRect:bounds.insetBy(dx:1,dy:1),xRadius:12,yRadius:12)
        (selected || hoveringDrop ? Studio.selected : Studio.key).setFill(); box.fill()
        (selected || hoveringDrop ? Studio.accent : Studio.border).setStroke()
        box.lineWidth = selected || hoveringDrop ? 2 : 1; box.stroke()
        let pad = NSRect(x:14,y:17,width:86,height:62)
        let outline = NSBezierPath(roundedRect:pad,xRadius:8,yRadius:8)
        Studio.background.setFill(); outline.fill()
        Studio.muted.withAlphaComponent(0.6).setStroke(); outline.lineWidth = 1; outline.stroke()
        var center = NSPoint(x:pad.midX,y:pad.midY)
        let code = gestureCode
        var arrow = ""
        if (414...421).contains(code) {
            let corner = (code-414)/2
            center = NSPoint(x:corner % 2 == 0 ? pad.minX+14 : pad.maxX-14,
                             y:corner < 2 ? pad.minY+14 : pad.maxY-14)
        } else if code >= 426 {
            let edge = (code-426)%8
            center = NSPoint(x:edge >= 4 ? (edge < 6 ? pad.minX+12 : pad.maxX-12) : pad.midX,
                             y:edge < 4 ? (edge < 2 ? pad.minY+12 : pad.maxY-12) : pad.midY)
            arrow = ["←","→","←","→","↑","↓","↑","↓"][edge]
        } else if (410...413).contains(code) {
            arrow = ["↙↗","↗↙","↻","↺"][code-410]
        } else {
            let offset = (270...273).contains(code) ? code-270 : (422...425).contains(code) ? code-422 : (code-400)%5-1
            if offset >= 0 && offset < 4 { arrow = ["↑","↓","←","→"][offset] }
        }
        if arrow.isEmpty {
            for i in 0..<fingers {
                let dot = NSRect(x:center.x-4+CGFloat(i)*10-CGFloat(fingers-1)*5,y:center.y-5,width:8,height:10)
                Studio.accent.setFill(); NSBezierPath(ovalIn:dot).fill()
            }
            if (414...421).contains(code) && code % 2 == 1 {
                Studio.accent.setStroke()
                NSBezierPath(ovalIn:NSRect(x:center.x-9,y:center.y-10,width:18,height:20)).stroke()
            }
        } else {
            // Geometric strokes stay centered independently of font bearings.
            func vectorArrow(from start: NSPoint, to end: NSPoint) {
                let path = NSBezierPath()
                path.lineWidth = 2.2; path.lineCapStyle = .round; path.lineJoinStyle = .round
                path.move(to:start); path.line(to:end)
                let angle = atan2(end.y-start.y,end.x-start.x)
                for side in [-1.0,1.0] {
                    path.move(to:NSPoint(x:end.x-6*cos(angle)+side*6*sin(angle),
                                        y:end.y-6*sin(angle)-side*6*cos(angle)))
                    path.line(to:end)
                }
                Studio.accent.setStroke(); path.stroke()
            }
            if code == 410 || code == 411 {
                // Closing points inward; opening points outward, symmetrically.
                for side in [-1.0,1.0] {
                    let inner = NSPoint(x:center.x+side*4,y:center.y-side*4)
                    let outer = NSPoint(x:center.x+side*16,y:center.y-side*16)
                    vectorArrow(from:code == 410 ? outer : inner,to:code == 410 ? inner : outer)
                }
            } else if code == 412 || code == 413 {
                let symbol = code == 412 ? "arrow.clockwise" : "arrow.counterclockwise"
                let icon = NSImage(systemSymbolName:symbol,accessibilityDescription:nil)?
                    .withSymbolConfiguration(.init(paletteColors:[Studio.accent]))
                icon?.draw(in:NSRect(x:center.x-13,y:center.y-13,width:26,height:26),
                           from:.zero,operation:.sourceOver,fraction:1,respectFlipped:true,hints:nil)
            } else {
                let delta: NSPoint
                switch arrow {
                case "↑": delta = NSPoint(x:0,y:-10)
                case "↓": delta = NSPoint(x:0,y:10)
                case "←": delta = NSPoint(x:-10,y:0)
                default: delta = NSPoint(x:10,y:0)
                }
                if (422...425).contains(code) {
                    // Show the starting edge, unlike a normal center scroll.
                    let start = NSPoint(x:center.x-delta.x*2,y:center.y-delta.y*2)
                    let end = NSPoint(x:center.x+delta.x*0.5,y:center.y+delta.y*0.5)
                    vectorArrow(from:start,to:end)
                    Studio.accent.setFill()
                    NSBezierPath(ovalIn:NSRect(x:start.x-2,y:start.y-2,width:4,height:4)).fill()
                } else {
                    vectorArrow(from:NSPoint(x:center.x-delta.x,y:center.y-delta.y),
                                to:NSPoint(x:center.x+delta.x,y:center.y+delta.y))
                }
            }
            let dotsY = code >= 426 ? pad.midY : pad.maxY-8
            for i in 0..<fingers {
                Studio.muted.setFill()
                NSBezierPath(ovalIn:NSRect(x:pad.midX-CGFloat(fingers-1)*4+CGFloat(i)*8-2,
                                          y:dotsY-2,width:4,height:4)).fill()
            }
        }

        let paragraph = NSMutableParagraphStyle(); paragraph.lineBreakMode = .byTruncatingTail
        (title as NSString).draw(in:NSRect(x:114,y:24,width:max(1,bounds.width-128),height:22),
            withAttributes:[.font:NSFont.systemFont(ofSize:13,weight:.semibold),.foregroundColor:Studio.text,.paragraphStyle:paragraph])
        (subtitle as NSString).draw(in:NSRect(x:114,y:51,width:max(1,bounds.width-128),height:22),
            withAttributes:[.font:NSFont.systemFont(ofSize:11),.foregroundColor:subtitle == "未割り当て" ? Studio.muted : Studio.accent,.paragraphStyle:paragraph])
    }
}

private final class TrackpadSurface: NSView {
    var showsEdges = false
    var edgeWidth = 0.12 { didSet { needsDisplay = true } }
    override func hitTest(_ point: NSPoint) -> NSView? { nil }
    override func draw(_ dirtyRect: NSRect) {
        let plate = bounds.insetBy(dx:1,dy:1)
        let outline = NSBezierPath(roundedRect:plate,xRadius:22,yRadius:22)
        Studio.key.setFill(); outline.fill()
        Studio.border.setStroke(); outline.lineWidth = 1; outline.stroke()
        guard showsEdges else { return }
        NSGraphicsContext.saveGraphicsState()
        outline.addClip()
        Studio.accent.withAlphaComponent(0.08).setFill()
        let w = plate.width * edgeWidth, h = plate.height * edgeWidth
        for strip in [NSRect(x:plate.minX,y:plate.minY,width:plate.width,height:h),
                      NSRect(x:plate.minX,y:plate.maxY-h,width:plate.width,height:h),
                      NSRect(x:plate.minX,y:plate.minY+h,width:w,height:plate.height-2*h),
                      NSRect(x:plate.maxX-w,y:plate.minY+h,width:w,height:plate.height-2*h)] {
            NSBezierPath(rect:strip).fill()
        }
        NSGraphicsContext.restoreGraphicsState()
    }
}

final class KeyboardCanvas: NSView {
    private(set) var keys: [UInt16: StudioButton] = [:]
    private(set) var orderedCodes: [UInt16] = []
    private var placements: [(NSView, NSRect)] = []
    let additionalMouse = NSPopUpButton()
    private(set) var mouseGrid: [UInt16: StudioButton] = [:]
    private var mouseGridViews: [NSView] = []
    private var mouseGridPlacements: [(NSView, NSRect)] = []
    private(set) var showsMouseGrid = false
    private var mouseConfiguration = Configuration()
    private var mouseDeviceID: String?
    var onMouseGridVisibilityChanged: (() -> Void)?
    func showMouseGrid(_ show: Bool) {
        showsMouseGrid = show
        for (view, _) in placements { view.isHidden = show }
        mouseGridViews.forEach { $0.isHidden = !show }
        onMouseGridVisibilityChanged?()
        needsLayout = true
    }
    private func rebuildMouseGrid() {
        mouseGridViews.forEach { $0.removeFromSuperview() }
        mouseGridViews.removeAll(); mouseGridPlacements.removeAll(); mouseGrid.removeAll()
        func place(_ view: NSView, _ rect: NSRect) {
            addSubview(view); mouseGridViews.append(view); mouseGridPlacements.append((view,rect))
        }
        place(Studio.label("マウスボタン",18,true),NSRect(x:0,y:0,width:610,height:28))
        place(Studio.label("Fn1などは「押して選択」で確認。各レイヤーのTAP／HOLDを個別に設定できます。",12),NSRect(x:0,y:34,width:810,height:24))
        let capture = StudioButton("ボタンを押して選択") { [weak self] in self?.onCaptureMouse?() }
        place(capture,NSRect(x:0,y:68,width:210,height:36))
        let roles = StudioButton("レイヤーボタンを設定") { [weak self] in self?.onMouseLayerButtons?() }
        place(roles,NSRect(x:220,y:68,width:210,height:36))
        let physical = (0...31).compactMap { mouseConfiguration.mouseInput(Int64($0), deviceID: mouseDeviceID) }
        let codes = physical + [270,271,272,273]
        for (index,code) in codes.enumerated() {
            let button = StudioButton(mouseConfiguration.inputName(code)) { [weak self] in self?.onSelect?(code,NSApp.currentEvent?.modifierFlags ?? []) }
            button.fill = Studio.key; button.fitKeyLabel = true; button.font = .systemFont(ofSize:12)
            button.tapOnlyDrop = true
            button.contextBuilder = { [weak self] in self?.onMenu?(code) }
            button.enableDrop { [weak self, weak button] json in self?.onDrop?(code,json,button?.dropTrigger ?? .tap) ?? false }
            mouseGrid[code] = button
            place(button,NSRect(x:(index % 6)*137,y:125+(index / 6)*79,width:130,height:70))
        }
        showMouseGrid(showsMouseGrid)
    }
    private var mouseTarget: StudioButton?
    private var mouseTargetCode: UInt16?
    var onSelect: ((UInt16, NSEvent.ModifierFlags) -> Void)?
    var onDrop: ((UInt16, String, Trigger) -> Bool)?
    var onCaptureMouse: (() -> Void)?
    var onMouseLayout: (() -> Void)?
    var onMouseLayerButtons: (() -> Void)?
    var onBlank: (() -> Void)?
    var onMenu: ((UInt16) -> NSMenu?)?
    override var isFlipped: Bool { true }
    override init(frame: NSRect) {
        super.init(frame: frame)
        rebuild(.jis)
    }
    private var trackpadMode = false
    private enum TouchGroup: Int, CaseIterable {
        case corners, two, three, four, pinch, edge
        var title: String { ["四隅のタップ","2本指","3本指","4本指","ピンチ・回転","端をなぞる"][rawValue] }
        var icon: String { ["square.dashed","circle.grid.2x1","ellipsis","circle.grid.2x2","arrow.up.left.and.arrow.down.right","rectangle.dashed"][rawValue] }
    }
    private var touchGroup = TouchGroup.corners
    private var doubleTap = false
    private var edgeFingers = 1
    private var padSurface: TrackpadSurface?
    var onTrackpadPageChange: (() -> Void)?
    var onTrackpadWidth: ((Double) -> Void)?
    private var edgeWidthSlider: NSSlider?
    private var trackpadButtons: [UInt16:String] = [:]

    private func showTouchGroup(_ group: TouchGroup) {
        touchGroup = group
        rebuild(keyboardLayout)
        onTrackpadPageChange?()
    }

    private func revealTouchInput(_ code: UInt16) {
        switch code {
        case 426...441: touchGroup = .edge; edgeFingers = code < 434 ? 1 : 2
        case 414...421: touchGroup = .corners; doubleTap = code % 2 == 1
        case 270...273,422...425: touchGroup = .two
        case 400...404: touchGroup = .three
        case 405...409: touchGroup = .four
        case 410...413: touchGroup = .pinch
        default: break
        }
    }

    private func buildTrackpad() {
        showsMouseGrid = false
        mouseGrid.removeAll(); mouseGridViews.removeAll(); mouseGridPlacements.removeAll()
        mouseTarget = nil; trackpadButtons.removeAll(); edgeWidthSlider = nil
        func place(_ node: NSView, _ rect: NSRect) { addSubview(node); placements.append((node,rect)) }
        func label(_ title: String, _ size: CGFloat, at rect: NSRect) { place(Studio.label(title,size),rect) }
        func target(_ title: String, _ code: Int, _ index: Int, fingers: Int) {
            let button = TrackpadGestureButton(title) { [weak self] in self?.onSelect?(UInt16(code),[]) }
            button.gestureCode = code; button.fingers = fingers
            button.tapOnlyDrop = true; button.supportsHold = false
            button.contextBuilder = { [weak self] in self?.onMenu?(UInt16(code)) }
            button.enableDrop { [weak self] json in self?.onDrop?(UInt16(code),json,.tap) ?? false }
            keys[UInt16(code)] = button; orderedCodes.append(UInt16(code)); trackpadButtons[UInt16(code)] = title
            place(button,NSRect(x:180+(index%2)*314,y:110+(index/2)*104,width:302,height:94))
        }
        label("トラックパッド",22,at:NSRect(x:0,y:8,width:650,height:34))
        for group in TouchGroup.allCases {
            let item = StudioButton(group.title,symbol:group.icon) { [weak self] in self?.showTouchGroup(group) }
            item.font = .systemFont(ofSize:10,weight:.medium)
            item.leftAligned = true; item.selected = group == touchGroup
            item.toolTip = group.title
            place(item,NSRect(x:0,y:110+group.rawValue*48,width:160,height:38))
        }
        label(touchGroup.title,17,at:NSRect(x:180,y:65,width:274,height:28))
        if touchGroup == .corners || touchGroup == .edge {
            let isEdge = touchGroup == .edge
            let options = isEdge ? ["1本指","2本指"] : ["1回","2回"]
            for (index,title) in options.enumerated() {
                let item = StudioButton(title) { [weak self] in
                    guard let self = self else { return }
                    if isEdge { self.edgeFingers = index+1 } else { self.doubleTap = index == 1 }
                    self.showTouchGroup(self.touchGroup)
                }
                item.selected = isEdge ? index+1 == edgeFingers : (index == 1) == doubleTap
                place(item,NSRect(x:630+index*84,y:62,width:78,height:32))
            }
        }
        padSurface = nil
        switch touchGroup {
        case .corners:
            for (i,title) in ["左上をタップ","右上をタップ","左下をタップ","右下をタップ"].enumerated() {
                target(doubleTap ? title.replacingOccurrences(of:"タップ",with:"2回タップ") : title,414+i*2+(doubleTap ? 1 : 0),i,fingers:1)
            }
        case .edge:
            for (i,title) in ["上端を左へ","上端を右へ","下端を左へ","下端を右へ","左端を上へ","左端を下へ","右端を上へ","右端を下へ"].enumerated() {
                target(title,426+(edgeFingers-1)*8+i,i,fingers:edgeFingers)
            }
            label("端の幅",12,at:NSRect(x:180,y:562,width:72,height:28))
            let slider = NSSlider(value:mouseConfiguration.edgeWidth,minValue:0.06,maxValue:0.22,target:self,action:#selector(edgeWidthChanged(_:)))
            slider.trackFillColor = Studio.accent; slider.isContinuous = false
            slider.setAccessibilityLabel("端の認識幅"); edgeWidthSlider = slider
            place(slider,NSRect(x:253,y:562,width:204,height:28))
        case .two:
            for (i,title) in ["上へスクロール","下へスクロール","左へスクロール","右へスクロール"].enumerated() {
                target(title,270+i,i,fingers:2)
            }
            for (i,title) in ["下端から上へ","上端から下へ","右端から左へ","左端から右へ"].enumerated() {
                target(title,422+i,i+4,fingers:2)
            }
        case .three,.four:
            let base = touchGroup == .three ? 400 : 405
            for (i,title) in ["タップ","上へスワイプ","下へスワイプ","左へスワイプ","右へスワイプ"].enumerated() {
                target(title,base+i,i,fingers:touchGroup == .three ? 3 : 4)
            }
        case .pinch:
            for (i,title) in ["指を閉じる","指を広げる","右に回転","左に回転"].enumerated() {
                target(title,410+i,i,fingers:2)
            }
        }
        let rows = (orderedCodes.count+1)/2
        let footerY = CGFloat(110+rows*104+6)
        let hint = touchGroup == .edge ? "角から始めるときは、最初に動かした方向で辺を判定します。" : "操作を選ぶか、右のActionをドラッグして割り当て"
        place(Studio.label(hint,12,true),NSRect(x:180,y:footerY,width:620,height:26))
        let settings = StudioButton("macOSのトラックパッド設定",symbol:"gearshape") {
            if let url = URL(string:"x-apple.systempreferences:com.apple.Trackpad-Settings.extension") { NSWorkspace.shared.open(url) }
        }
        settings.leftAligned = true
        place(settings,NSRect(x:180,y:touchGroup == .edge ? 603 : footerY+40,width:300,height:32))
    }

    @objc private func edgeWidthChanged(_ slider: NSSlider) { onTrackpadWidth?(slider.doubleValue) }

    private(set) var keyboardLayout: KeyboardLayout = .jis
    func rebuild(_ layout: KeyboardLayout) {
        keyboardLayout = layout
        subviews.forEach { $0.removeFromSuperview() }; keys.removeAll(); orderedCodes.removeAll(); placements.removeAll()
        if trackpadMode { buildTrackpad(); needsLayout = true; return }
        // Apple compact keyboards: one shared pitch preserves stagger and key widths.
        // JIS has Control beside A and Caps Lock at bottom left; US reverses them.
        let functionRow: [(String, Int, CGFloat)] = [("esc",53,1.5),("F1",122,1),("F2",120,1),("F3",99,1),("F4",118,1),("F5",96,1),("F6",97,1),("F7",98,1),("F8",100,1),("F9",101,1),("F10",109,1),("F11",103,1),("F12",111,1),("Touch ID",-1,1)]
        let rows: [[(String, Int, CGFloat)]]
        if layout == .jis {
            rows = [functionRow,
                [("1",18,1.25),("2",19,1),("3",20,1),("4",21,1),("5",23,1),("6",22,1),("7",26,1),("8",28,1),("9",25,1),("0",29,1),("-",27,1),("^",24,1),("¥",93,1),("delete",51,1.25)],
                [("tab",48,1),("Q",12,1),("W",13,1),("E",14,1),("R",15,1),("T",17,1),("Y",16,1),("U",32,1),("I",34,1),("O",31,1),("P",35,1),("@",33,1),("[",30,1),("return",36,1.5)],
                [("⌃ control",-1,1.25),("A",0,1),("S",1,1),("D",2,1),("F",3,1),("G",5,1),("H",4,1),("J",38,1),("K",40,1),("L",37,1),(";",41,1),(":",39,1),("]",42,1)],
                [("⇧ shift",-1,1.75),("Z",6,1),("X",7,1),("C",8,1),("V",9,1),("B",11,1),("N",45,1),("M",46,1),(",",43,1),(".",47,1),("/",44,1),("_",94,1),("⇧ shift",-1,1.75)],
                [("⇪",-1,1),("⌥",-1,1),("⌘",-1,1.25),("英数",102,1.25),("Space",49,3.5),("かな",104,1.25),("⌘",-1,1.25),("fn",-1,1)]
            ]
        } else {
            rows = [functionRow,
                [("`",50,1),("1",18,1),("2",19,1),("3",20,1),("4",21,1),("5",23,1),("6",22,1),("7",26,1),("8",28,1),("9",25,1),("0",29,1),("-",27,1),("=",24,1),("delete",51,1.5)],
                [("tab",48,1.5),("Q",12,1),("W",13,1),("E",14,1),("R",15,1),("T",17,1),("Y",16,1),("U",32,1),("I",34,1),("O",31,1),("P",35,1),("[",33,1),("]",30,1),("\\",42,1)],
                [("⇪ caps lock",-1,1.75),("A",0,1),("S",1,1),("D",2,1),("F",3,1),("G",5,1),("H",4,1),("J",38,1),("K",40,1),("L",37,1),(";",41,1),("'",39,1),("return",36,1.75)],
                [("⇧ shift",-1,2.25),("Z",6,1),("X",7,1),("C",8,1),("V",9,1),("B",11,1),("N",45,1),("M",46,1),(",",43,1),(".",47,1),("/",44,1),("⇧ shift",-1,2.25)],
                [("fn",-1,1),("⌃",-1,1),("⌥",-1,1),("⌘",-1,1.25),("Space",49,5),("⌘",-1,1.25),("⌥",-1,1)]
            ]
        }
        let pitch: CGFloat = 825 / 14.5
        for (r, row) in rows.enumerated() {
            var column: CGFloat = 0
            for (name, code, units) in row {
                addKey(name, code, NSRect(x:column * pitch,y:CGFloat(r) * 56,width:units * pitch - 5,height:layout == .jis && code == 36 ? 106 : 50))
                column += units
            }
        }
        // Compact Mac inverted-T arrows are part of the bottom row, not a separate PC block.
        addKey("←",123,NSRect(x:11.5*pitch,y:308,width:pitch-5,height:22))
        addKey("↑",126,NSRect(x:12.5*pitch,y:280,width:pitch-5,height:22))
        addKey("↓",125,NSRect(x:12.5*pitch,y:308,width:pitch-5,height:22))
        addKey("→",124,NSRect(x:13.5*pitch,y:308,width:pitch-5,height:22))
        for (title, x) in [("ナビゲーション", CGFloat(0)), ("テンキー", CGFloat(185)), ("マウス", mouseConfiguration.mouseButtonLayout(for: self.mouseDeviceID) != nil ? CGFloat(480) : CGFloat(640))] {
            let label = Studio.label(title, 10, true); addSubview(label); placements.append((label, NSRect(x: x, y: 345, width: 150, height: 20)))
        }
        for (name, code, x, y) in [("Home",115,0,375),("PgUp",116,55,375),("Del",117,110,375),("End",119,0,426),("PgDn",121,55,426)] {
            addKey(name, code, NSRect(x: x, y: y, width: 48, height: 45))
        }
        for (r, names) in [["Clear", "=", "÷", "×"], ["7", "8", "9", "−"], ["4", "5", "6", "+"], ["1", "2", "3", "Enter"], ["0", "", ".", ""]].enumerated() {
            for (c, name) in names.enumerated() where !name.isEmpty {
                let codes = [[71,81,75,67],[89,91,92,78],[86,87,88,69],[83,84,85,76],[82,-1,65,-1]]
                addKey(name, codes[r][c], NSRect(x: 185 + c * 50, y: 375 + r * 51, width: name == "0" ? 95 : 45, height: name == "Enter" ? 96 : 45))
            }
        }
        if let custom = mouseConfiguration.mouseButtonLayout(for: self.mouseDeviceID) {
            let outline = MouseSilhouette(frame:.zero); addSubview(outline)
            placements.append((outline,NSRect(x:420,y:375,width:400,height:260)))
            for original in custom.items {
                let item = original.squared
                guard let code = MouseLayoutInput.code(item.source,configuration:mouseConfiguration,deviceID:mouseDeviceID) else { continue }
                let caption: String
                switch item.source {
                case 0: caption = "左"
                case 1: caption = "右"
                case 2: caption = "中央"
                case 270: caption = "↑"
                case 271: caption = "↓"
                case 272: caption = "←"
                case 273: caption = "→"
                case 340: caption = "モード切替"
                default: caption = code == 260 ? "進む" : (code == 259 ? "戻る" : "ボタン \(item.source)")
                }
                addKey(caption,Int(code),NSRect(x:425+item.x*0.65,y:375+item.y*0.65,width:item.width*0.65,height:item.height*0.65))
            }
        } else {
        for (name, x, y, w, h) in [("左",640,375,48,48),("●",695,430,48,48),("右",750,375,48,48),("▲",695,375,48,48),("▼",695,485,48,48),("進む",640,430,48,48),("戻る",640,485,48,48)] {
            let codes = ["左":256,"●":258,"右":257,"▲":270,"▼":271,"進む":260,"戻る":259]
            addKey(name, codes[name]!, NSRect(x: x, y: y, width: w, height: h))
        }
        addKey("◀", 272, NSRect(x: 750, y: 430, width: 48, height: 48))
        addKey("▶", 273, NSRect(x: 750, y: 485, width: 48, height: 48))
        }
        let customMouse = mouseConfiguration.mouseButtonLayout(for: self.mouseDeviceID) != nil
        let controlsX: CGFloat = customMouse ? 0 : 420
        let controlsWidth: CGFloat = customMouse ? 165 : 200
        let layoutButton = StudioButton("",symbol:"slider.horizontal.3") { [weak self] in self?.onMouseLayout?() }
        layoutButton.fill = .clear
        layoutButton.setAccessibilityLabel("マウスボタンレイアウト")
        layoutButton.toolTip = "マウスボタンの配置とサイズを編集"
        addSubview(layoutButton); placements.append((layoutButton,NSRect(x:customMouse ? 732 : 764,y:341,width:28,height:28)))
        let capture = StudioButton("ボタンを押して選択") { [weak self] in self?.onCaptureMouse?() }
        capture.fill = Studio.key
        addSubview(capture); placements.append((capture,NSRect(x:controlsX,y:customMouse ? 564 : 535,width:controlsWidth,height:38)))
        let target = StudioButton("選択したマウスボタン") { [weak self] in
            if let code = self?.mouseTargetCode { self?.onSelect?(code,[]) }
        }
        target.fill = Studio.key; target.tapOnlyDrop = true
        target.enableDrop { [weak self, weak target] json in
            guard let self = self, let code = self.mouseTargetCode else { return false }
            return self.onDrop?(code,json,target?.dropTrigger ?? .tap) ?? false
        }
        mouseTarget = target
        addSubview(target); placements.append((target,NSRect(x:controlsX,y:customMouse ? 607 : 580,width:controlsWidth,height:customMouse ? 32 : 45)))
        additionalMouse.removeAllItems(); additionalMouse.addItems(withTitles:["追加マウスボタンを選択…"] + Keys.extraMouseInputs.map { $0.name })
        additionalMouse.target = self; additionalMouse.action = #selector(additionalMouseSelected)
        additionalMouse.setAccessibilityLabel("追加マウスボタン")
        let allButtons = StudioButton("全マウスボタンを設定…") { [weak self] in self?.showMouseGrid(true) }
        allButtons.fill = Studio.key
        addSubview(allButtons); placements.append((allButtons,NSRect(x:controlsX,y:customMouse ? 521 : 485,width:controlsWidth,height:38)))
        let layerButtons = StudioButton("レイヤーボタンを設定") { [weak self] in self?.onMouseLayerButtons?() }
        layerButtons.fill = Studio.key
        layerButtons.toolTip = "マウスごとに進む／戻るレイヤーの起動ボタンを登録"
        addSubview(layerButtons)
        placements.append((layerButtons,NSRect(x:controlsX,y:customMouse ? 478 : 435,width:controlsWidth,height:38)))
        rebuildMouseGrid()
        needsLayout = true
    }
    @objc private func additionalMouseSelected() {
        let index = additionalMouse.indexOfSelectedItem - 1
        guard Keys.extraMouseInputs.indices.contains(index) else { return }
        onSelect?(Keys.extraMouseInputs[index].code,[])
    }
    required init?(coder: NSCoder) { fatalError("init(coder:) has not been implemented") }
    private func addKey(_ name: String, _ code: Int, _ rect: NSRect) {
        let button = MacKeyboardKey(name) { [weak self] in if code >= 0 { self?.onSelect?(UInt16(code), NSApp.currentEvent?.modifierFlags ?? []) } }
        button.jisReturn = keyboardLayout == .jis && code == 36
        button.fill = Studio.key; button.fitKeyLabel = true; button.font = .systemFont(ofSize: name.count > 7 ? 9 : 11)
        button.isEnabled = code >= 0 && code != 49
        if button.isEnabled {
            keys[UInt16(code)] = button; orderedCodes.append(UInt16(code))
            button.tapOnlyDrop = true; button.supportsHold = !(270...273).contains(code)
            button.contextBuilder = { [weak self] in self?.onMenu?(UInt16(code)) }
            button.enableDrop { [weak self, weak button] json in self?.onDrop?(UInt16(code), json, button?.dropTrigger ?? .tap) ?? false }
        } else { button.toolTip = code == 49 ? "Spaceはレイヤーキーです" : "修飾キー・Touch IDは直接割り当て対象外です。レイヤーは左側から選択できます" }
        addSubview(button); placements.append((button, rect))
    }
    override func layout() {
        super.layout()
        for group in [placements, mouseGridPlacements] {
            let left = group.map { $0.1.minX }.min() ?? 0
            let right = group.map { $0.1.maxX }.max() ?? 820
            let contentWidth = max(1, right - left)
            let scale = min(trackpadMode ? 1.15 : CGFloat.greatestFiniteMagnitude, min(bounds.width / contentWidth, bounds.height / 640))
            let inset = (bounds.width - contentWidth * scale) / 2
            for (view, rect) in group {
                view.frame = NSRect(x: inset + (rect.minX-left)*scale, y: rect.minY*scale, width: rect.width*scale, height: rect.height*scale)
            }
        }
    }
    func animateAssignments(_ inputs: Set<UInt16>) {
        for code in inputs { if let button = keys[code] ?? mouseGrid[code] { Studio.assignmentCommit(button) } }
    }
    func update(_ mappings: [Mapping], layer: Layer, selected: Set<UInt16>, configuration: Configuration, mouseDeviceID: String? = nil) {
        let changedMode = trackpadMode != (layer == .trackpad)
        trackpadMode = layer == .trackpad
        if trackpadMode, let code = selected.sorted().first, keys[code] == nil {
            revealTouchInput(code); rebuild(keyboardLayout)
        } else if changedMode { rebuild(keyboardLayout) }
        if trackpadMode {
            padSurface?.edgeWidth = configuration.edgeWidth
            edgeWidthSlider?.doubleValue = configuration.edgeWidth
        }
        if mouseConfiguration != configuration || self.mouseDeviceID != mouseDeviceID {
            let layoutChanged = mouseConfiguration.mouseButtonLayout(for: self.mouseDeviceID) != configuration.mouseButtonLayout(for: mouseDeviceID) ||
                mouseConfiguration.layerButtons(for: self.mouseDeviceID) != configuration.layerButtons(for: mouseDeviceID)
            self.mouseDeviceID = mouseDeviceID
            mouseConfiguration = configuration
            if !trackpadMode { if layoutChanged { rebuild(keyboardLayout) } else { rebuildMouseGrid() } }
        }
        mouseTargetCode = selected.sorted().first(where: { $0 >= 256 })
        mouseTarget?.title = mouseTargetCode.map { configuration.inputName($0) } ?? "ボタンを押して選択してください"
        mouseTarget?.selected = mouseTargetCode != nil
        mouseTarget?.supportsTap = mouseTargetCode.map { InputPolicy.allows($0,layer:layer) } ?? false
        mouseTarget?.supportsHold = mouseTargetCode.map { InputPolicy.allows($0,layer:layer,trigger:.hold) } ?? false
        mouseTarget?.isEnabled = mouseTarget?.supportsTap == true || mouseTarget?.supportsHold == true

        for (code, button) in Array(keys) + Array(mouseGrid) + (mouseTargetCode.flatMap { code in mouseTarget.map { [(code,$0)] } } ?? []) {
            let mapping = mappings.first { $0.layer == layer && $0.input == code && $0.trigger == .tap }
            button.supportsTap = InputPolicy.allows(code, layer: layer)
            button.supportsHold = InputPolicy.allows(code, layer: layer, trigger: .hold)
            button.isEnabled = button.supportsTap || button.supportsHold
            if mouseGrid[code] === button {
                button.title = configuration.inputName(code)
                button.subtitle = mapping?.description(layout:keyboardLayout) ?? (button.isEnabled ? "未割り当て" : "通常操作を保持")
            }
            button.tapAssignmentKind = mapping?.action
            button.holdAssignmentKind = mappings.first { $0.layer == layer && $0.input == code && $0.trigger == .hold }?.action
            button.tapAssignmentDescription = mapping?.targetDescription(layout:keyboardLayout)
            button.holdAssignmentDescription = mappings.first { $0.layer == layer && $0.input == code && $0.trigger == .hold }?.targetDescription(layout:keyboardLayout)
            button.hasHoldAssignment = mappings.contains { $0.layer == layer && $0.input == code && $0.trigger == .hold }
            button.holdAssignmentColor = mappings.first { $0.layer == layer && $0.input == code && $0.trigger == .hold }.map { Studio.assignmentFill($0.action) }
            button.preservesAssignmentFill = mapping != nil
            button.fill = mapping.map { Studio.assignmentFill($0.action) } ?? Studio.key
            button.labelColor = mapping.map { Studio.assignmentText($0.action) }
            if trackpadMode, let caption = trackpadButtons[code] {
                let assigned = mapping?.targetDescription(layout:keyboardLayout)
                button.title = caption
                button.subtitle = assigned ?? "未割り当て"
                button.setAccessibilityLabel(configuration.inputName(code) + "、" + (assigned ?? "未割り当て"))
            }
            button.selected = selected.contains(code); button.toolTip = mouseConfiguration.inputDetail(code, deviceID: mouseDeviceID) + (mapping.map { " — " + $0.description(layout:keyboardLayout) } ?? "")
        }
    }
    override func mouseDown(with event: NSEvent) { onBlank?() }
}

final class StudioSurface: NSView {
    override var isFlipped: Bool { true }
    var arrange: (() -> Void)?
    var separators: [NSRect] = []
    var isPanel = false
    var onBlank: (() -> Void)?
    override func mouseDown(with event: NSEvent) { if let onBlank = onBlank { onBlank() } else { super.mouseDown(with:event) } }
    var transparent = false
    override func layout() { super.layout(); arrange?(); if isPanel { Studio.themeControls(self) } }
    override func draw(_ dirtyRect: NSRect) {
        if transparent { return }
        if isPanel {
            let path = NSBezierPath(roundedRect:bounds.insetBy(dx:0.5,dy:0.5),xRadius:12,yRadius:12)
            Studio.key.setFill(); path.fill(); Studio.border.setStroke(); path.lineWidth = 1; path.stroke()
        } else { Studio.background.setFill(); bounds.fill(); Studio.border.setFill(); for rect in separators { rect.fill() } }
    }
}

// Keep inspector forms inside the visible clip area, including legacy scrollbars.
private final class InspectorFormScrollView: NSScrollView {
    override func tile() {
        super.tile()
        guard let document = documentView else { return }
        let width = contentSize.width
        if document.frame.width != width { document.setFrameSize(NSSize(width:width,height:document.frame.height)) }
        document.needsLayout = true
    }
}

private final class MacroTextField: NSTextField {
    var changed: (() -> Void)?
    override func textDidChange(_ notification: Notification) { super.textDidChange(notification); changed?() }
}

private struct LibraryPreferences: Codable {
    var favorites: [StudioAction] = []
    var recent: [StudioAction] = []
    var created: [StudioAction]? = nil
    var lightTheme: Bool? = nil
}

private final class MacroPointWindow: NSWindow {
    override var canBecomeKey: Bool { true }
}
private final class MacroPointSurface: NSView {
    var picked: ((CGPoint?) -> Void)?
    override var acceptsFirstResponder: Bool { true }
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }
    override func resetCursorRects() { addCursorRect(bounds,cursor:.crosshair) }
    override func mouseDown(with event: NSEvent) {} // Consume the entire capture click.
    override func mouseUp(with event: NSEvent) {
        guard let point = event.cgEvent?.location else { picked?(nil); return }
        picked?(point)
    }
    override func keyDown(with event: NSEvent) { if event.keyCode == 53 { picked?(nil) } }
    override func rightMouseDown(with event: NSEvent) {}
    override func rightMouseUp(with event: NSEvent) { picked?(nil) }
}

final class WorkspaceController: NSObject, NSSearchFieldDelegate {
    private var macroPointWindows: [NSWindow] = []
    let view = StudioSurface()
    let keyboard = KeyboardCanvas()
    private let sidebar = StudioSurface(), toolbar = StudioSurface(), center = StudioSurface(), inspector = StudioSurface()
    private let footer = Studio.label("● 入力停止中", 11, true)
    private let runtime = Studio.label("実行中：標準", 11, true)
    private let profileContext = Studio.label("", 11, true)
    private var runtimeProfileName = "標準"
    private var activeMouseDeviceID: String?
    private var nav: [StudioButton] = []
    private var normalButton: StudioButton!, spaceButton: StudioButton!
    private var mouseButtons: [Layer: StudioButton] = [:]
    private let layoutPicker = NSPopUpButton()
    private let mouseScopePicker = NSPopUpButton()
    private var mouseScopeIDs: [String?] = [nil]
    private var selectedMouseDeviceID: String?
    var editingMouseID: String? { currentLayer == .trackpad ? nil : selectedMouseDeviceID }
    private func editingMappings(_ profile: String? = nil) -> [Mapping] {
        config.mappings(for:profile ?? profileID,mouseDevice:editingMouseID)
    }
    private func refreshMouseScopePicker() {
        let names = config.mouseDeviceNames ?? [:]
        let connected = MouseDeviceTracker.shared.devices
        let ids = Set(names.keys).union(connected.keys).union((config.mouseLayerButtonsByDevice ?? [:]).keys).union((config.mouseButtonLayoutsByDevice ?? [:]).keys).union(selectedMouseDeviceID.map { [$0] } ?? [])
        mouseScopeIDs = [nil] + ids.sorted().map { Optional($0) }
        mouseScopePicker.removeAllItems()
        for id in mouseScopeIDs {
            if let id = id { mouseScopePicker.addItem(withTitle: connected[id] ?? names[id] ?? id) }
            else { mouseScopePicker.addItem(withTitle:"すべてのマウス") }
        }
        mouseScopePicker.selectItem(at:mouseScopeIDs.firstIndex(where: { $0 == selectedMouseDeviceID }) ?? 0)
        mouseScopePicker.isHidden = section != "keyboard" || currentLayer == .trackpad
    }
    @objc private func mouseScopeChanged() {
        let index = mouseScopePicker.indexOfSelectedItem
        guard mouseScopeIDs.indices.contains(index) else { return }
        selectMouseScope(mouseScopeIDs[index])
    }
    func selectMouseScope(_ deviceID: String?) {
        selectedMouseDeviceID = deviceID
        refreshMouseScopePicker()
        if let id = selectedMouseDeviceID, let name = MouseDeviceTracker.shared.devices[id], config.mouseDeviceNames?[id] != name {
            var candidate = config; candidate.mouseDeviceNames = candidate.mouseDeviceNames ?? [:]; candidate.mouseDeviceNames?[id] = name
            _ = transact(candidate)
        }
        refreshProfileContext()
        keyboard.update(editingMappings(),layer:currentLayer,selected:selected,configuration:config,mouseDeviceID:editingMouseID)
        renderInspector()
    }
    private var section = "keyboard"
    private var editorDeckIDs: [String] { config.visibleDeckIDs(profile:profileID) }
    private var deckGallery = true
    private var deckListMode = false
    private var deckMultiSelect = false
    private(set) var currentLayer: Layer = .normal
    private var selectedTrigger: Trigger = .tap
    private(set) var selected: Set<UInt16> = []
    private var selectedDeck: String?
    private var selectedDecks = Set<String>()
    private var deckAnchor: String?
    private var deckClipboard: [DeckItem] = []
    private let deckDrawer = DeckSettingsDrawer()
    private var deckDrawerOpen = false
    private var deckDrawerWide = false
    private var deckEditor: NSView?
    private var deckEditorOrder: [String] = []
    private var deckEditorCells: [String: NSView] = [:]
    private var deckEditorButtons: [String: DeckTile] = [:]
    private var deckRendered: [String: DeckRenderState] = [:]
    private var deckEditorLabels: [String: NSTextField] = [:]
    private var libraryOpen = false
    private var editable = true
    private let search = NSSearchField()
    private let category = NSPopUpButton()
    private let collections = NSSegmentedControl(labels:["すべて","お気に入り","最近","作成済み"],trackingMode:.selectOne,target:nil,action:nil)
    private let actionsScroll = NSScrollView()
    private let actionRows = StudioSurface()
    private let profileControl: NSPopUpButton
    private let profiles: NSView
    private let settings: NSView
    private let mappingList: NSView
    private var config = Configuration()
    private var profileID = "default"
    private var committing = false
    private var anchorKey: UInt16?
    private var footerToggle: StudioButton!
    private var undoNotice: StudioButton!
    private var undoNoticeGeneration = 0
    private var undoStack: [Configuration] = [], redoStack: [Configuration] = []
    private var preferences = LibraryPreferences()
    private let preferencesURL: URL
    var commit: ((Configuration) -> Bool)?
    var onPlayMacro: ((String) -> Void)?
    var onStopMacro: (() -> Void)?
    var onRecordMacro: ((String, Bool, Bool, Bool) -> Bool)?
    var onStopRecording: (() -> Void)?
    private var selectedMacroID: String?
    private var macroEditorMode = 0
    private var macroSelectedSteps = Set<String>()
    private var recordingMacroID: String?
    private var macroStepList: MacroStepsView?
    private weak var recordingLabel: NSTextField?
    private var macroProgress: InputEngine.MacroProgress?
    private weak var macroPlayButton: StudioButton?
    private weak var macroStopButton: StudioButton?
    func updateMacroProgress(_ progress: InputEngine.MacroProgress) {
        macroProgress = progress; refreshMacroProgress()
    }
    private func refreshMacroProgress() {
        let progress = macroProgress.flatMap { $0.id == selectedMacroID ? $0 : nil }
        if recordingMacroID == nil {
            let message = progress?.message ?? "\(config.macros.first { $0.id == selectedMacroID }?.steps.count ?? 0) 手順"
            recordingLabel?.stringValue = message; recordingLabel?.toolTip = message
            recordingLabel?.textColor = progress?.failed == true ? .systemRed : (progress?.running == true ? Studio.accent : Studio.muted)
            recordingLabel?.setAccessibilityLabel(message)
        }
        let reason: String?
        if recordingMacroID != nil { reason = "記録を終了すると再生できます" }
        else if macroProgress?.running == true { reason = "再生中です。停止してから再実行できます" }
        else if config.macros.first(where: { $0.id == selectedMacroID })?.steps.isEmpty != false { reason = "手順を追加すると再生できます" }
        else { reason = nil }
        macroPlayButton?.isEnabled = reason == nil
        macroPlayButton?.toolTip = reason ?? "選択したマクロを再生"
        macroPlayButton?.setAccessibilityHelp(reason ?? "選択したマクロを再生")
        macroStopButton?.toolTip = recordingMacroID != nil || macroProgress?.running == true ? "再生・記録を停止" : "再生・記録中に停止できます"
        macroStepList?.showExecution(stepID:progress?.stepID,failed:progress?.failed == true)
        macroStopButton?.isEnabled = recordingMacroID != nil || macroProgress?.running == true
    }
    private var statusHistoryButton: StudioButton!


    var onSelectMapping: ((String, Layer, UInt16) -> Void)?
    var onCaptureMouse: (() -> Void)?
    var onCancelMouseCapture: (() -> Void)?
    private(set) var selectedLayoutID = "default"
    var onToggleDeckLayout: ((String) -> Void)?
    var onToggleDeck: (() -> Void)?
    var onChooseDeckColor: (([String]) -> Void)?
    var onChooseDeckIcon: (([String]) -> Void)?
    var onRenameDeck: ((String) -> Void)?
    var onEditDeck: ((String, DeckEdit) -> Void)?
    var onPermissionSetup: (() -> Void)?
    private lazy var permissionBanner: StudioButton = {
        let button = StudioButton("入力を使う準備が必要です   →   macOSの操作許可を設定", symbol:"lock.open") { [weak self] in self?.onPermissionSetup?() }
        button.fill = Studio.selected
        button.isHidden = true
        return button
    }()
    func setPermissionRequired(_ required: Bool) {
        permissionBanner.isHidden = !required
        view.needsLayout = true
    }
    var onStopToggle: (() -> Void)?
    var onShowStatus: (() -> Void)?
    var actionIssue: ((Mapping) -> String?)?
    var onInspectAction: ((Mapping) -> Void)?
    func refreshActionIssues() { if NSEvent.pressedMouseButtons == 0 && !selected.isEmpty && !libraryOpen { renderInspector() } }
    private var navigationHistory: [(String, Layer)] = []
    private var restoringNavigation = false
    private var navigationMonitor: Any?
    private lazy var backNavigation = StudioButton("", symbol:"arrow.left") { [weak self] in _ = self?.goBack() }
    private func updateBackNavigation() {
        let available = !navigationHistory.isEmpty || libraryOpen || (section == "deck" && !deckGallery) || (section == "keyboard" && keyboard.showsMouseGrid)
        backNavigation.isHidden = !available
        backNavigation.isEnabled = available
    }
    @discardableResult private func goBack() -> Bool {
        defer { updateBackNavigation() }
        if section == "keyboard" && keyboard.showsMouseGrid { keyboard.showMouseGrid(false); return true }
        if libraryOpen { libraryOpen = false; renderInspector(); return true }
        if section == "deck" && !deckGallery {
            if deckDrawerOpen { deckDrawerOpen = false; deckEditor?.needsLayout = true; return true }
            deckGallery = true; selectedDeck = nil; selectedDecks.removeAll(); selected.removeAll(); renderCenter(); renderInspector(); return true
        }
        guard let previous = navigationHistory.popLast() else { return false }
        restoringNavigation = true
        currentLayer = previous.1; selected.removeAll(); selectedDeck = nil
        showSection(previous.0)
        restoringNavigation = false
        return true
    }
    private func rememberNavigation() {
        if !restoringNavigation {
            navigationHistory.append((section,currentLayer))
            if navigationHistory.count > 100 { navigationHistory.removeFirst() }
        }
    }
    deinit { if let navigationMonitor = navigationMonitor { NSEvent.removeMonitor(navigationMonitor) }; MouseDeviceTracker.shared.onDevicesChanged = nil }
    private var awaitingRecovery = false
    var onSave: (() -> Void)?
    var onTheme: ((NSAppearance?) -> Void)?
    var onStatus: ((String) -> Void)?

    init(profileControl: NSPopUpButton, profiles: NSView, settings: NSView, mappingList: NSView, preferencesURL: URL) {
        self.profileControl = profileControl; self.profiles = profiles; self.settings = settings
        self.mappingList = mappingList; self.preferencesURL = preferencesURL
        super.init()
        if let data = try? Data(contentsOf: preferencesURL), let saved = try? JSONDecoder().decode(LibraryPreferences.self, from: data) { preferences = saved }
        if preferences.created == nil {
            preferences.created = preferences.favorites.filter { isCustomLibraryAction($0) }
            preferences.favorites.removeAll { isCustomLibraryAction($0) }
            savePreferences()
        }
        for part in [sidebar, toolbar, center, inspector] { view.addSubview(part) }
        view.addSubview(footer); view.addSubview(runtime); view.addSubview(permissionBanner)
        statusHistoryButton = StudioButton("", symbol:"clock.arrow.circlepath") { [weak self] in self?.onShowStatus?() }
        statusHistoryButton.toolTip = "動作履歴・停止や失敗の理由を確認"
        statusHistoryButton.setAccessibilityLabel("動作履歴・停止や失敗の理由を確認")
        view.addSubview(statusHistoryButton)
        footerToggle = StudioButton("● 入力停止中") { [weak self] in guard let self = self else { return }; if self.awaitingRecovery { self.onShowStatus?() } else { self.onStopToggle?() } }
        footerToggle.font = .systemFont(ofSize: 11); footerToggle.leftAligned = true
        footerToggle.contextBuilder = { [weak self] in
            let menu = NSMenu(); let item = NSMenuItem(title:"最近の動作状況を確認…",action:nil,keyEquivalent:"")
            let action = StudioButton("") { [weak self] in self?.onShowStatus?() }; item.view = action; action.title = "最近の動作状況を確認…"; action.frame = NSRect(x:0,y:0,width:230,height:30); menu.addItem(item); return menu
        }
        footerToggle.setAccessibilityLabel("入力変換の有効・停止"); view.addSubview(footerToggle)
        undoNotice = StudioButton("元に戻す") { [weak self] in self?.undo() }; undoNotice.fill = Studio.selected
        undoNotice.isHidden = true; view.addSubview(undoNotice)
        view.arrange = { [weak self] in self?.layout() }
        buildSidebar(); buildToolbar()
        MouseDeviceTracker.shared.onDevicesChanged = { [weak self] in self?.refreshMouseScopePicker() }
        refreshMouseScopePicker()
        navigationMonitor = NSEvent.addLocalMonitorForEvents(matching:[.keyDown,.otherMouseUp]) { [weak self] event in
            guard let self = self, let window = self.view.window, event.window === window, window.attachedSheet == nil else { return event }
            let backMouse = event.type == .otherMouseUp && event.buttonNumber == self.config.backButton
            let flags = event.modifierFlags.intersection(.deviceIndependentFlagsMask).subtracting([.capsLock,.numericPad,.function])
            let backShortcut = event.type == .keyDown && flags == .command && (event.charactersIgnoringModifiers == "[" || event.keyCode == (self.config.keyboardLayout == .jis ? 30 : 33))
            if (backMouse || backShortcut) && self.goBack() { return nil }
            return event
        }
        keyboard.onMouseGridVisibilityChanged = { [weak self] in self?.updateBackNavigation() }
        keyboard.onTrackpadPageChange = { [weak self] in
            guard let self = self else { return }
            self.selected.removeAll(); self.anchorKey = nil
            self.libraryOpen = true
            self.keyboard.update(self.editingMappings(),layer:.trackpad,selected:[],configuration:self.config)
            self.renderInspector()
        }
        keyboard.onTrackpadWidth = { [weak self] width in
            guard let self = self, self.editable else { return }
            var candidate = self.config
            candidate.trackpadEdgeWidth = width
            _ = self.transact(candidate)
        }
        keyboard.onSelect = { [weak self] code, flags in self?.selectKey(code, flags: flags) }
        keyboard.onDrop = { [weak self] code, json, trigger in self?.receiveDrop(json, key: code, deckID: nil, trigger: trigger) ?? false }
        keyboard.onMenu = { [weak self] code in self?.keyMenu(code) }
        keyboard.onBlank = { [weak self] in self?.clearKeySelection() }
        center.onBlank = { [weak self] in self?.clearKeySelection() }
        view.onBlank = { [weak self] in self?.clearKeySelection() }
        search.placeholderString = "Actionを検索"; search.delegate = self
        category.addItems(withTitles:["すべて"] + StudioAction.browsingCategories)
        collections.selectedSegment = 0; collections.target = self; collections.action = #selector(collectionChanged)
        collections.setAccessibilityLabel("アクション一覧・お気に入り・最近使ったもの")
        category.target = self; category.action = #selector(filterChanged)
        actionsScroll.documentView = actionRows; actionsScroll.hasVerticalScroller = true; actionsScroll.drawsBackground = false
        renderCenter(); renderInspector()
    }
    private func button(_ title: String, symbol: String = "", action: @escaping () -> Void) -> StudioButton {
        StudioButton(title, symbol: symbol, action: action)
    }
    private func buildSidebar() {
        let brand = Studio.label("RELYR", 23); brand.font = .systemFont(ofSize: 23, weight: .semibold)
        brand.frame = NSRect(x: 18, y: 18, width: 145, height: 30); sidebar.addSubview(brand)
        let tagline = Studio.label("Key Layers & Actions", 10, true); tagline.frame = NSRect(x: 18, y: 52, width: 150, height: 18); sidebar.addSubview(tagline)
        func heading(_ title: String, _ y: CGFloat) { let text = Studio.label(title, 9, true); text.frame = NSRect(x: 18, y: y, width: 150, height: 17); sidebar.addSubview(text) }
        func entry(_ title: String, _ symbol: String, _ y: CGFloat, enabled: Bool = true, action: @escaping () -> Void) -> StudioButton {
            let b = button(title, symbol: symbol, action: action); b.leftAligned = true; b.font = .systemFont(ofSize: 12, weight: .semibold)
            b.frame = NSRect(x: 12, y: y, width: 160, height: 42); b.isEnabled = enabled
            if !enabled { b.toolTip = "Mac版では未対応です" }
            sidebar.addSubview(b); nav.append(b); return b
        }
        heading("KEY LAYER", 88)
        normalButton = entry("デフォルト", "square.3.layers.3d", 110) { [weak self] in self?.showLayer(.normal) }
        spaceButton = entry("Space", "space", 156) { [weak self] in self?.showLayer(.space) }
        mouseButtons[.control] = entry("Control", "control", 202) { [weak self] in self?.showLayer(.control) }
        mouseButtons[.control]?.toolTip = "Controlを押しながら使用。未割り当ては通常のControl操作"
        heading("MOUSE LAYER", 260)
        mouseButtons[.mouseRight] = entry("右クリック", "computermouse", 281) { [weak self] in self?.showLayer(.mouseRight) }
        mouseButtons[.mouseForward] = entry("進む", "arrow.up.right", 327) { [weak self] in self?.showLayer(.mouseForward) }
        mouseButtons[.mouseBack] = entry("戻る", "arrow.down.left", 373) { [weak self] in self?.showLayer(.mouseBack) }
        mouseButtons[.trackpad] = entry("トラックパッド", "rectangle.and.hand.point.up.left", 419) { [weak self] in self?.showLayer(.trackpad) }
        heading("SYSTEM", 470)
        mouseButtons[.menuBar] = entry("メニューバー", "menubar.rectangle", 491) { [weak self] in self?.showLayer(.menuBar) }
        let bottom: [(String, String, String)] = [("マクロ", "record.circle", "macros"), ("プロファイル", "line.3.horizontal", "profiles"), ("ジェスチャー", "hand.draw", "gestures"), ("Deckパネル", "square.grid.3x3", "deck"), ("設定", "gearshape", "settings")]
        for (i, item) in bottom.enumerated() {
            let b = entry(item.0, item.1, 530 + CGFloat(i) * 42, enabled: item.2 != "unsupported") { [weak self] in self?.showSection(item.2) }
            b.identifier = NSUserInterfaceItemIdentifier("nav-\(item.2)-\(i)")
        }
    }
    private func buildToolbar() {
        backNavigation.toolTip = "前の画面に戻る"; backNavigation.setAccessibilityLabel("前の画面に戻る")
        backNavigation.identifier = NSUserInterfaceItemIdentifier("navigation-back")
        backNavigation.frame = NSRect(x:0,y:8,width:38,height:38)
        toolbar.addSubview(backNavigation)
        toolbar.addSubview(profileContext)
        toolbar.addSubview(profileControl)
        profileControl.frame = NSRect(x: 12, y: 13, width: 172, height: 28)
        profileControl.setAccessibilityLabel("編集プロファイル")
        layoutPicker.addItems(withTitles: KeyboardLayout.allCases.map { $0.rawValue })
        layoutPicker.frame = NSRect(x: 200, y: 13, width: 100, height: 28)
        layoutPicker.target = self; layoutPicker.action = #selector(layoutChanged)
        layoutPicker.setAccessibilityLabel("キーボード配列"); toolbar.addSubview(layoutPicker)
        mouseScopePicker.frame = NSRect(x:492,y:13,width:180,height:28)
        mouseScopePicker.target = self; mouseScopePicker.action = #selector(mouseScopeChanged)
        mouseScopePicker.toolTip = "マウスごとに割り当てを編集。キーボードの共通設定とアプリ別プロファイルはそのまま使います。"
        mouseScopePicker.setAccessibilityLabel("編集するマウス")
        toolbar.addSubview(mouseScopePicker)
        let list = button("", symbol: "list.bullet") { [weak self] in self?.showSection("list") }; list.toolTip = "保存済みの割り当て"; list.frame = NSRect(x: 310, y: 8, width: 38, height: 38); toolbar.addSubview(list)
        let undo = button("", symbol: "arrow.uturn.backward") { [weak self] in self?.undo() }; undo.identifier = NSUserInterfaceItemIdentifier("history-undo"); undo.toolTip = "元に戻す"; undo.frame = NSRect(x: 352, y: 8, width: 38, height: 38); toolbar.addSubview(undo)
        let redo = button("", symbol: "arrow.uturn.forward") { [weak self] in self?.redo() }; redo.identifier = NSUserInterfaceItemIdentifier("history-redo"); redo.toolTip = "やり直す"; redo.frame = NSRect(x: 394, y: 8, width: 38, height: 38); toolbar.addSubview(redo)
        for control in toolbar.subviews where control !== backNavigation { control.frame.origin.x += 44 }
        for (identifier, symbol, light) in [("light", "sun.max", true), ("dark", "moon", false)] {
            let theme = button("", symbol: symbol) { [weak self] in self?.setTheme(light: light) }
            theme.identifier = NSUserInterfaceItemIdentifier(identifier); theme.toolTip = light ? "ライトテーマ" : "ダークテーマ"; theme.setAccessibilityLabel(theme.toolTip!); toolbar.addSubview(theme)
        }
        let save = button("自動保存") { [weak self] in self?.onSave?() }; save.fill = Studio.assigned; save.identifier = NSUserInterfaceItemIdentifier("save"); save.toolTip = "変更は自動保存されます。クリックすると現在の設定を再保存します。"; save.setAccessibilityLabel("変更は自動保存。クリックして再保存"); toolbar.addSubview(save)
    }
    @objc private func layoutChanged() {
        guard editable else { return }
        var candidate = config; candidate.keyboardLayout = KeyboardLayout.allCases[layoutPicker.indexOfSelectedItem]
        let old = config
        committing = true; let saved = commit?(candidate) ?? false; committing = false
        if saved { undoStack.append(old); redoStack.removeAll(); update(candidate, profileID: profileID) }
        else { layoutPicker.selectItem(withTitle: config.keyboardLayout.rawValue) }
    }
    private func layout() {
        let width = view.bounds.width, height = view.bounds.height
        let side: CGFloat = width < 1120 ? 174 : 184, right: CGFloat = (section == "profiles" || section == "settings") ? 0 : 276
        inspector.isHidden = right == 0
        sidebar.frame = NSRect(x: 0, y: 0, width: side, height: height - 32)
        toolbar.frame = NSRect(x: side + 16, y: 0, width: width - side - 34, height: 70)
        let setupHeight: CGFloat = permissionBanner.isHidden ? 0 : 60
        permissionBanner.frame = NSRect(x:side+28,y:72,width:max(0,width-side-56),height:48)
        center.frame = NSRect(x: side + 28, y: 76 + setupHeight, width: width - side - right - (right == 0 ? 56 : 76), height: height - 124 - setupHeight)
        inspector.frame = NSRect(x: width - right - 12, y: 76 + setupHeight, width: right, height: height - 124 - setupHeight)
        footerToggle.frame = NSRect(x: 10, y: height - 31, width: 154, height: 26)
        footer.frame = NSRect(x: 180, y: height - 27, width: max(0, width - 614), height: 19)
        statusHistoryButton.frame = NSRect(x:width-426,y:height-32,width:32,height:28)
        undoNotice.frame = NSRect(x: width - 360, y: height - 32, width: 100, height: 28)
        view.separators = [NSRect(x: side, y: 25, width: 1, height: height - 70)]
        if right > 0 { view.separators.append(NSRect(x: inspector.frame.minX - 8, y: 25, width: 1, height: height - 70)) }
        view.needsDisplay = true
        runtime.frame = NSRect(x: width - 250, y: height - 27, width: 230, height: 19)
        profileContext.frame = NSRect(x:44,y:48,width:max(0,toolbar.bounds.width-60),height:18)
        let trackpad = section == "keyboard" && currentLayer == .trackpad
        layoutPicker.isHidden = trackpad
        toolbar.subviews.first { ($0 as? NSButton)?.toolTip == "保存済みの割り当て" }?.frame.origin.x = trackpad ? 244 : 354
        toolbar.subviews.first { $0.identifier?.rawValue == "history-undo" }?.frame.origin.x = trackpad ? 286 : 396
        toolbar.subviews.first { $0.identifier?.rawValue == "history-redo" }?.frame.origin.x = trackpad ? 328 : 438
        refreshProfileContext()
        for b in nav where b.identifier != nil {
            let index = Int(b.identifier!.rawValue.split(separator: "-").last!) ?? 0
            b.frame.origin.y = max(550, height - 262) + CGFloat(index) * 42
        }
        toolbar.subviews.first { $0.identifier?.rawValue == "save" }?.frame = NSRect(x: toolbar.bounds.width - 184, y: 7, width: 82, height: 38)
        toolbar.subviews.first { $0.identifier?.rawValue == "light" }?.frame = NSRect(x: toolbar.bounds.width - 92, y: 8, width: 38, height: 38)
        toolbar.subviews.first { $0.identifier?.rawValue == "dark" }?.frame = NSRect(x: toolbar.bounds.width - 48, y: 8, width: 38, height: 38)
        if section == "keyboard" { keyboard.frame = NSRect(x:0,y:0,width:center.bounds.width,height:max(1,center.bounds.height - 36)); keyboard.needsLayout = true; menuTrackpad.frame = NSRect(x:0,y:center.bounds.height - 30,width:center.bounds.width,height:26);  }
        else if let child = center.subviews.first { child.frame = center.bounds }
        layoutInspector(); Studio.themeControls(toolbar); Studio.themeControls(inspector)
    }
    func update(_ configuration: Configuration, profileID: String) {
        let previousConfiguration = config
        let sameProfile = self.profileID == profileID

        if !committing && config.ignoringDeckPresentation() != configuration.ignoringDeckPresentation() { undoStack.removeAll(); redoStack.removeAll(); undoNotice.isHidden = true }
        config = configuration
        refreshMouseScopePicker()
        if !config.deckLayoutIDs.contains(selectedLayoutID) { selectedLayoutID = "default"; deckEditor = nil }
        if keyboard.keyboardLayout != config.keyboardLayout { keyboard.rebuild(config.keyboardLayout) }
        menuTrackpad.state = config.menuBarTrackpadEnabled == true ? .on : .off
        layoutPicker.selectItem(withTitle: config.keyboardLayout.rawValue)
        if self.profileID != profileID { selected.removeAll(); selectedDeck = nil }
        if let resolved = config.resolvedDeck(selectedLayoutID,profile:profileID),resolved != selectedLayoutID { selectedLayoutID = resolved; deckEditor = nil; selectedDecks.removeAll(); selectedDeck = nil }
        self.profileID = profileID
        refreshProfileContext()
        keyboard.update(editingMappings(), layer: currentLayer, selected: selected, configuration: config, mouseDeviceID:editingMouseID)
        if section == "deck" || section == "gestures" || section == "macros" {
            let previous = (center.subviews.first as? NSScrollView)?.contentView.bounds.origin
            renderCenter()
            if let previous = previous, let scroll = center.subviews.first as? NSScrollView, let document = scroll.documentView {
                scroll.contentView.scroll(to:NSPoint(x:0,y:max(0,min(previous.y,document.bounds.height-scroll.contentView.bounds.height))))
                scroll.reflectScrolledClipView(scroll.contentView)
            }
        }
        renderInspector()
        if sameProfile {
            let changed = Set(configuration.mappings(for:profileID).filter { mapping in
                mapping.layer == currentLayer && !previousConfiguration.mappings(for:profileID).contains(mapping)
            }.map { $0.input })
            keyboard.animateAssignments(changed)
            if section == "deck" {
                for id in Studio.changedDeckAssignments(from:previousConfiguration,to:configuration) {
                    if let tile = deckEditorButtons[id] { Studio.assignmentCommit(tile) }
                }
            }
        }
    }
    func setStatus(_ message: String, enabled: Bool, requested: Bool? = nil, runtimeProfile: String? = nil) {
        awaitingRecovery = !enabled && (requested ?? message.hasPrefix("一時停止"))
        footerToggle.toolTip = awaitingRecovery ? "復帰を待っている理由を確認。停止は設定の「停止する」から行います。" : "入力変換の有効・停止"
        footerToggle.title = "● " + (enabled ? "エンジン稼働中" : ((requested ?? message.hasPrefix("一時停止")) ? "復帰待ち・理由を見る" : "入力停止中"))
        footerToggle.needsDisplay = true
        footer.stringValue = message
        footer.toolTip = message
        footer.textColor = enabled ? Studio.accent : Studio.muted
        if let runtimeProfile = runtimeProfile { setRuntime(runtimeProfile) }
    }
    func applySavedTheme() {
        let light = preferences.lightTheme == true
        view.window?.appearance = NSAppearance(named: light ? .aqua : .darkAqua)
        onTheme?(view.window?.appearance)
        (toolbar.subviews.first { $0.identifier?.rawValue == "light" } as? StudioButton)?.selected = light
        (toolbar.subviews.first { $0.identifier?.rawValue == "dark" } as? StudioButton)?.selected = !light
    }
    func setTheme(light: Bool) {
        preferences.lightTheme = light; applySavedTheme(); savePreferences()
        func redraw(_ node: NSView) { node.needsDisplay = true; node.subviews.forEach(redraw) }; redraw(view)
    }
    func setEditable(_ value: Bool) { editable = value; footerToggle.isEnabled = value }
    func setRuntime(_ value: String) {
        runtimeProfileName = value; refreshProfileContext()
    }
    func setActiveMouse(_ id: String?) {
        activeMouseDeviceID = id; refreshProfileContext()
    }
    private func refreshProfileContext() {
        let edited = config.profileName(profileID)
        let differs = edited != runtimeProfileName
        let mouseName = selectedMouseDeviceID.map { MouseDeviceTracker.shared.devices[$0] ?? config.mouseDeviceNames?[$0] ?? $0 } ?? "すべてのマウス"
        let activeName = activeMouseDeviceID.map { MouseDeviceTracker.shared.devices[$0] ?? config.mouseDeviceNames?[$0] ?? $0 } ?? "未検出"
        runtime.stringValue = "実行中：\(runtimeProfileName)・\(activeName)"
        runtime.toolTip = runtime.stringValue
        let context = currentLayer == .trackpad && section == "keyboard"
            ? "編集：\(edited)    実行中：\(runtimeProfileName)"
            : "編集：\(edited)・\(mouseName)    実行中：\(runtimeProfileName)・\(activeName)"
        profileContext.stringValue = context + (differs ? "  （編集と実行が異なります）" : "")
        profileContext.textColor = differs ? Studio.accent : Studio.muted
        profileContext.lineBreakMode = .byTruncatingTail
        profileContext.toolTip = profileContext.stringValue + "。編集対象を選んでも実行プロファイルは切り替わりません。"
        profileContext.setAccessibilityLabel(profileContext.stringValue)
    }
    func showLayer(_ value: Layer) { if currentLayer != value || section != "keyboard" { rememberNavigation() }; restoringNavigation = true; defer { restoringNavigation = false }; currentLayer = value; if value == .trackpad { libraryOpen = true; collections.selectedSegment = 0 }; selectedTrigger = .tap; selected.removeAll(); selectedDeck = nil; showSection("keyboard") }
    func showSection(_ value: String) { if section != value { rememberNavigation() }; onCancelMouseCapture?(); section = value; mouseScopePicker.isHidden = value != "keyboard" || currentLayer == .trackpad; if value == "deck" { deckGallery = true; libraryOpen = false; selectedDeck = nil; selected.removeAll() }; if value == "profiles" || value == "settings" || value == "macros" { libraryOpen = false; selected.removeAll(); selectedDeck = nil }; renderCenter(); renderInspector(); view.needsLayout = true }
    private lazy var menuTrackpad: NSButton = {
        let button = StudioToggle(title:"メニューバーではトラックパッドにも割り当てを適用",target:self,action:#selector(menuTrackpadChanged))
        button.toolTip = "メニューバー上だけ2本指スクロールで割り当てを実行します。通常のスワイプや他のレイヤーには適用しません。"
        return button
    }()
    @objc private func menuTrackpadChanged() {
        var candidate = config; candidate.menuBarTrackpadEnabled = menuTrackpad.state == .on
        _ = transact(candidate)
        menuTrackpad.state = config.menuBarTrackpadEnabled == true ? .on : .off
    }
    private func renderCenter() {
        Studio.transition(center)
        center.subviews.forEach { $0.removeFromSuperview() }
        for button in nav {
            if let id = button.identifier?.rawValue, id.hasPrefix("nav-") { button.selected = id.split(separator:"-").dropFirst().first.map(String.init) == section }
        }
        normalButton.selected = section == "keyboard" && currentLayer == .normal
        spaceButton.selected = section == "keyboard" && currentLayer == .space
        for (layer, button) in mouseButtons { button.selected = section == "keyboard" && currentLayer == layer }
        switch section {
        case "macros": center.addSubview(makeMacroEditor())
        case "gestures": center.addSubview(makeGestureEditor())
        case "profiles": center.addSubview(profiles)
        case "settings": center.addSubview(settings)
        case "list": center.addSubview(mappingList)
        case "deck": center.addSubview(deckGallery ? makeDeckGallery() : makeDeckEditor())
        default: if currentLayer == .menuBar { center.addSubview(menuTrackpad); menuTrackpad.state = config.menuBarTrackpadEnabled == true ? .on : .off; menuTrackpad.isEnabled = editable }; center.addSubview(keyboard); keyboard.update(editingMappings(), layer: currentLayer, selected: selected, configuration: config, mouseDeviceID:editingMouseID)
        }
        center.subviews.first?.frame = center.bounds
    }
    private func makeDeckGallery() -> NSView {
        let surface = StudioSurface(), scroll = NSScrollView(), grid = StudioSurface()
        let heading = Studio.label("Deckパネル",22,true); surface.addSubview(heading)
        scroll.drawsBackground = false; scroll.hasVerticalScroller = true; scroll.documentView = grid; surface.addSubview(scroll)
        var cards: [NSView] = []
        for id in editorDeckIDs {
            let card = DeckLayoutCard("") { [weak self] in self?.selectDeckLayout(id) }
            card.fill = Studio.key; card.layout = config.deckLayout(id); card.count = config.deckSlotCount(id)
            let slots = config.deckPositions(id)
            for item in config.deckItems(id) { if let slot = slots[item.id] { card.positions[slot] = Studio.color(item.colorHex) ?? Studio.accent } }
            card.setAccessibilityLabel("\(card.layout.name)を編集"); card.toolTip = "クリックしてレイアウトを編集"
            grid.addSubview(card); cards.append(card)
        }
        let create = button("＋ 新規レイアウト") { [weak self] in self?.manageDeckLayout(0) }; create.fill = Studio.key
        create.isEnabled = config.deckLayoutIDs.count < 16; grid.addSubview(create); cards.append(create)
        surface.arrange = { [weak surface] in
            guard let surface = surface else { return }
            heading.frame = NSRect(x:0,y:0,width:surface.bounds.width,height:36)
            scroll.frame = NSRect(x:0,y:50,width:surface.bounds.width,height:max(1,surface.bounds.height-50))
            let columns = max(1,Int(surface.bounds.width/250)), width = max(120,(surface.bounds.width-CGFloat(columns-1)*16)/CGFloat(columns))
            grid.frame = NSRect(x:0,y:0,width:surface.bounds.width,height:max(scroll.bounds.height,CGFloat((cards.count+columns-1)/columns)*206))
            for (i,card) in cards.enumerated() { card.frame = NSRect(x:CGFloat(i%columns)*(width+16),y:CGFloat(i/columns)*206,width:width,height:190) }
        }
        return surface
    }
    private func addDeckEditorCell(_ item: DeckItem, to grid: NSView, layout: DeckLayout) {
            guard let mapping = config.mapping(for: item) else { return }
            let b = DeckTile(mapping.targetDescription(layout: config.keyboardLayout)) { [weak self] in
                guard let self = self else { return }; self.selectDeck(item.id, flags: self.deckEditorButtons[item.id]?.lastClickModifiers ?? [], toggle: self.deckMultiSelect)
            }
            deckRendered[item.id] = DeckRenderState(item:item,mapping:mapping,keyboardLayout:config.keyboardLayout,profileName:config.profileName(item.profileID))
            b.selected = selectedDecks.contains(item.id); b.hoverAnimation = layout.hoverAnimation != false
            b.fill = Studio.color(item.colorHex) ?? Studio.key; b.image = Studio.deckImage(mapping,iconPNG:item.iconPNG); b.toolTip = item.monitor.map { "\($0.title)：\($0.caption)" } ?? "\(item.title) — \(mapping.description(layout: config.keyboardLayout))"; b.setAccessibilityLabel(item.title)
            b.enableDrop { [weak self] json in self?.receiveDrop(json, key: self?.config.deck.first(where: { $0.id == item.id })?.input, deckID: item.id) ?? false }
            let cell = StudioSurface(); cell.addSubview(b); b.frame = NSRect(x: 0, y: 0, width: 54, height: 52)
            let name = NSTextField(wrappingLabelWithString:config.deckTitle(item)); Studio.configureDeckLabel(name,text:config.deckTitle(item)); name.maximumNumberOfLines = 1; name.lineBreakMode = .byTruncatingTail; name.alignment = .center; name.frame = NSRect(x: 0, y: 53, width: 54, height: 14); name.isHidden = !layout.labels; cell.addSubview(name)
            let menu = NSMenu()
            for (name, tag) in [("名前を変更…",0),("前へ移動",-1),("後へ移動",1),("Deckから削除",2),("コピー",3),("複製",4),("貼り付け",5),("色：青",10),("色：緑",11),("色：橙",12),("色をリセット",13),("色を選択…",14),("アイコンを選択…",20),("アイコンを戻す",21),("登録ファイルをコピー",30),("Finderで表示",31),("ファイルをプレビュー",32),("クリップボードのファイルを登録",33)] {
                let command = NSMenuItem(title: name, action: #selector(deckMenu(_:)), keyEquivalent: ""); command.target = self; command.tag = tag; command.representedObject = item.id; menu.addItem(command)
            }
            b.dragAnchorID = item.id
            b.dragIDs = { [weak self] in
                guard let self = self else { return [] }
                if !self.selectedDecks.contains(item.id) { self.selectDeck(item.id,flags:[]) }
                return self.config.deck.filter { self.selectedDecks.contains($0.id) }.map { $0.id }
            }
            b.movePreviewRoot = grid
            b.movePreviewContext = { [weak self] in
                guard let self = self, let item = self.config.deck.first(where: { $0.id == item.id }), let slot = self.config.deckPositions(item.layoutID ?? "default")[item.id] else { return nil }
                return (self.config,item.layoutID ?? "default",slot)
            }
            b.moveItems = { [weak self] ids, anchor, target in self?.moveDeckItems(ids,layout:item.layoutID ?? "default",anchor:anchor,slot:target) ?? false }
            b.receiveFile = { [weak self] url in self?.receiveDeckFile(url,to:item.id) ?? false }
            b.fileURL = { [weak self] in
                guard let self = self, let current = self.config.deck.first(where: { $0.id == item.id }), let action = self.config.mapping(for:current), [.file,.launch].contains(action.action) else { return nil }
                return URL(fileURLWithPath:action.application)
            }
            b.enableDeckDrops()
            b.menu = menu
            b.contextBuilder = { [weak self] in
                guard let self = self else { return nil }
                let action = self.config.deck.first { $0.id == item.id }.flatMap { self.config.mapping(for:$0) }
                for command in menu.items {
                    if (30...32).contains(command.tag) { command.isHidden = action.map { $0.action != .file && $0.action != .launch } ?? true }
                    if command.tag == 5 { command.isEnabled = !self.deckClipboard.isEmpty }
                    if command.tag == 33 { command.isEnabled = DeckFilePreview.clipboardFile() != nil }
                }
                menu.autoenablesItems = false; return menu
            }
            grid.addSubview(cell); deckEditorCells[item.id] = cell; deckEditorButtons[item.id] = b; deckEditorLabels[item.id] = name
    }
    private func makeDeckEditor() -> NSView {
        let layout = config.deckLayout(selectedLayoutID)
        let positions = config.deckPositions(selectedLayoutID)
        let items = config.deckItems(selectedLayoutID).filter { positions[$0.id,default:0] < config.deckSlotCount(selectedLayoutID) }
        let ids = items.sorted { positions[$0.id, default:0] < positions[$1.id, default:0] }.map { $0.id }
        if let cached = deckEditor,let grid = (cached.subviews.first { $0 is NSScrollView } as? NSScrollView)?.documentView {
            let removed = Set(deckEditorOrder).subtracting(ids)
            DeckFilePreview.shared.close(owners:removed)
            for id in removed { deckEditorCells.removeValue(forKey:id)?.removeFromSuperview(); deckEditorButtons.removeValue(forKey:id); deckEditorLabels.removeValue(forKey:id); deckRendered.removeValue(forKey:id) }
            for item in items where deckEditorCells[item.id] == nil { addDeckEditorCell(item,to:grid,layout:layout) }
            selectedDecks.formIntersection(Set(ids))
            if let id = selectedDeck,!ids.contains(id) { selectedDeck = nil }
            deckEditorOrder = ids
            for index in 0...1 {
                (cached.subviews.first { $0.identifier?.rawValue == "deck-behavior-\(index)" } as? NSPopUpButton)?.selectItem(at:DeckDisplayBehavior.allCases.firstIndex(of:(index == 0 ? config.deckAfterAction : config.deckOnLeave) ?? .stay) ?? 0)
            }
            if let picker = cached.subviews.first(where: { $0.identifier?.rawValue == "deck-layout-picker" }) as? NSPopUpButton {
                picker.removeAllItems(); picker.addItems(withTitles:editorDeckIDs.map { config.deckLayout($0).name }); picker.selectItem(at:editorDeckIDs.firstIndex(of:selectedLayoutID) ?? 0)
            }
            (cached.subviews.first { $0.identifier?.rawValue == "deck-columns" } as? NSPopUpButton)?.selectItem(at: layout.columns - 1)
            (cached.subviews.first { $0.identifier?.rawValue == "deck-labels" } as? NSButton)?.state = layout.labels ? .on : .off
            for item in items {
                if let mapping = config.mapping(for: item) {
                    deckEditorLabels[item.id]?.isHidden = !layout.labels
                    deckEditorButtons[item.id]?.selected = selectedDecks.contains(item.id)
                    deckEditorButtons[item.id]?.hoverAnimation = layout.hoverAnimation != false
                    if let label = deckEditorLabels[item.id] { Studio.configureDeckLabel(label,text:config.deckTitle(item)) }
                    let state = DeckRenderState(item:item,mapping:mapping,keyboardLayout:config.keyboardLayout,profileName:config.profileName(item.profileID))
                    guard deckRendered[item.id] != state else { continue }
                    deckRendered[item.id] = state
                    deckEditorButtons[item.id]?.title = mapping.targetDescription(layout: config.keyboardLayout)
                    deckEditorButtons[item.id]?.image = Studio.deckImage(mapping,iconPNG:item.iconPNG)
                    deckEditorButtons[item.id]?.toolTip = item.monitor.map { "\($0.title)：\($0.caption)" } ?? "\(item.title) — \(mapping.description(layout: config.keyboardLayout))"
                    deckEditorButtons[item.id]?.setAccessibilityLabel(item.title)
                    deckEditorButtons[item.id]?.fill = Studio.color(item.colorHex) ?? Studio.key
                    if let label = deckEditorLabels[item.id] { Studio.configureDeckLabel(label,text:config.deckTitle(item)) }
                    deckEditorLabels[item.id]?.isHidden = !layout.labels
                }
            }
            deckDrawer.update(config,layout:layout,profile:profileID)
            (cached.subviews.first { $0.identifier?.rawValue == "deck-inline-name" } as? NSTextField)?.stringValue = layout.name
            cached.needsLayout = true
            return cached
        }
        deckEditorOrder = ids; deckRendered.removeAll(); deckEditorCells.removeAll(); deckEditorButtons.removeAll(); deckEditorLabels.removeAll()
        let panel = StudioSurface()
        let back = button("", symbol:"chevron.left") { [weak self] in self?.deckGallery = true; self?.libraryOpen = false; self?.selectedDeck = nil; self?.renderCenter(); self?.renderInspector() }
        back.setAccessibilityLabel("レイアウト一覧へ戻る"); back.toolTip = "レイアウト一覧へ戻る"; panel.addSubview(back)
        let name = NSTextField(string:layout.name); name.isBordered = false; name.drawsBackground = false; name.font = .systemFont(ofSize:16,weight:.semibold); name.textColor = Studio.text
        name.identifier = NSUserInterfaceItemIdentifier("deck-inline-name"); name.target = self; name.action = #selector(renameDeckInline(_:)); name.toolTip = "Deck名を編集し、Returnで確定"; name.setAccessibilityLabel("Deckの名前"); panel.addSubview(name)
        let show = button("Deckを表示", symbol:"arrow.up.right") { [weak self] in if let self = self { self.onToggleDeckLayout?(self.selectedLayoutID) } }
        show.toolTip = "Deckを表示"; show.setAccessibilityLabel("Deckを表示"); panel.addSubview(show)
        var commands: [NSButton] = [show]
        for (operation,title,hint) in [(0,"plus","新しいDeck"),(1,"square.on.square","Deckを複製"),(3,"trash","Deckを削除")] {
            let control = button("", symbol:title) { [weak self] in self?.manageDeckLayout(operation) }
            control.toolTip = hint; control.setAccessibilityLabel(hint); control.isEnabled = operation != 3 || selectedLayoutID != "default"; panel.addSubview(control); commands.append(control)
        }
        let multiSelect = StudioButton(frame: .zero)
        multiSelect.title = "複数選択"
        multiSelect.identifier = NSUserInterfaceItemIdentifier("deck-multi-select")
        multiSelect.fill = Studio.key
        multiSelect.symbol = deckMultiSelect ? "checkmark.circle.fill" : "checkmark.circle"
        multiSelect.setButtonType(.toggle)
        multiSelect.state = deckMultiSelect ? .on : .off
        multiSelect.selected = deckMultiSelect
        multiSelect.setAccessibilityLabel("複数選択")
        multiSelect.toolTip = "クリックで選択を追加・解除し、選んだボタンをまとめてドラッグ"
        multiSelect.target = self
        multiSelect.action = #selector(toggleDeckMultiSelect(_:))
        panel.addSubview(multiSelect)
        let customize = button("カスタマイズ", symbol:"slider.horizontal.3") { [weak self] in self?.editDeckPresentation() }; customize.toolTip = "Deckをカスタマイズ"; customize.setAccessibilityLabel("Deckをカスタマイズ"); panel.addSubview(customize)
        let monitor = NSPopUpButton(); monitor.addItems(withTitles:["モニターを追加…"] + DeckMonitor.allCases.map { $0.title }); monitor.target = self; monitor.action = #selector(addDeckMonitor(_:)); monitor.setAccessibilityLabel("モニターを追加"); panel.addSubview(monitor)
        deckDrawer.update(config,layout:layout,profile:profileID); panel.addSubview(deckDrawer)
        deckDrawer.closeDrawer = { [weak self] in self?.deckDrawerOpen = false; self?.deckEditor?.needsLayout = true }
        deckDrawer.changeLayout = { [weak self] value in guard let self = self,value.id == self.selectedLayoutID else { return }; var candidate = self.config; candidate.setDeckLayout(value); _ = self.transact(candidate) }
        deckDrawer.changeBehavior = { [weak self] index,behavior in guard let self = self else { return }; var candidate = self.config; if index == 0 { candidate.deckAfterAction = behavior } else { candidate.deckOnLeave = behavior }; _ = self.transact(candidate) }
        deckDrawer.changeLink = { [weak self] enabled in self?.setDeckProfileLink(enabled) }
        deckDrawer.changeDefault = { [weak self] enabled in
            guard let self = self else { return }; var candidate = self.config
            if candidate.deckDefaults == nil { candidate.deckDefaults = [:] }
            if enabled { candidate.deckDefaults?[self.profileID] = self.selectedLayoutID } else { candidate.deckDefaults?.removeValue(forKey:self.profileID) }
            _ = self.transact(candidate)
        }
        let scroll = NSScrollView(); scroll.hasVerticalScroller = true; scroll.hasHorizontalScroller = true; scroll.drawsBackground = false
        let grid = StudioSurface(); scroll.documentView = grid; panel.addSubview(scroll)
        grid.identifier = NSUserInterfaceItemIdentifier("deck-editor-grid")
        for item in items { addDeckEditorCell(item,to:grid,layout:layout) }
        let mode = NSSegmentedControl(labels:["グリッド","リスト"],trackingMode:.selectOne,target:self,action:#selector(deckViewChanged(_:)))
        mode.selectedSegment = deckListMode ? 1 : 0; mode.frame = NSRect(x:0,y:112,width:200,height:28); panel.addSubview(mode)
        var emptyTiles: [Int:DeckTile] = [:]
        var summaries: [Int:NSTextField] = [:]
        panel.arrange = { [weak self, weak panel] in
            guard let self = self, let panel = panel else { return }
            let width = panel.bounds.width
            let compact = width < 660
            back.frame = NSRect(x:0,y:0,width:44,height:40)
            let multiSelectWidth: CGFloat = 104
            let multiSelectX = width - multiSelectWidth
            multiSelect.frame = NSRect(x:multiSelectX,y:0,width:multiSelectWidth,height:40)
            let commandY: CGFloat = compact ? 48 : 0
            let commandX: CGFloat = compact ? 0 : multiSelectX-412
            let nameEnd = compact ? multiSelectX : commandX
            name.frame = NSRect(x:56,y:7,width:max(0,nameEnd-68),height:28)
            for (index,control) in commands.enumerated() {
                let x = index == 0 ? commandX : commandX+128+CGFloat(index-1)*52
                control.frame = NSRect(x:x,y:commandY,width:index == 0 ? 120 : 44,height:40)
                if let button = control as? StudioButton { button.fill = Studio.key }
            }
            customize.frame = NSRect(x:commandX+284,y:commandY,width:116,height:40)
            customize.fill = Studio.key
            let optionsY: CGFloat = compact ? 100 : 52
            mode.frame = NSRect(x:0,y:optionsY,width:150,height:32)
            monitor.frame = NSRect(x:162,y:optionsY,width:min(230,max(140,width-162)),height:32)
            if self.deckDrawerWide { if width < 760 { self.deckDrawerWide = false } } else if width > 820 { self.deckDrawerWide = true }
            self.deckDrawer.isHidden = !self.deckDrawerOpen
            let top: CGFloat = compact ? 144 : 96
            var preview = NSRect(x:0,y:top,width:width,height:max(1,panel.bounds.height-top))
            if self.deckDrawerOpen {
                if self.deckDrawerWide {
                    self.deckDrawer.frame = NSRect(x:width-288,y:top,width:288,height:preview.height)
                    preview.size.width = max(1,width-304)
                } else {
                    let height = min(300,max(170,(panel.bounds.height-top)*0.48))
                    self.deckDrawer.frame = NSRect(x:0,y:top,width:width,height:height)
                    preview.origin.y += height+12; preview.size.height = max(1,panel.bounds.height-preview.minY)
                }
            }
            scroll.frame = preview
            let layout = self.config.deckLayout(self.selectedLayoutID), columns = layout.columns
            let positions = self.config.deckPositions(self.selectedLayoutID), count = self.config.deckSlotCount(self.selectedLayoutID)
            let stepX = DeckMetrics.stepX(labels:layout.labels), stepY = DeckMetrics.stepY(labels:layout.labels)
            grid.frame = NSRect(x:0,y:0,width:max(panel.bounds.width,CGFloat(columns)*stepX),height:max(scroll.bounds.height,CGFloat((count+columns-1)/columns)*stepY+12))
            for (id,cell) in self.deckEditorCells { if let slot = positions[id] { Studio.place(cell,at:NSRect(x:CGFloat(slot%columns)*stepX,y:CGFloat(slot/columns)*stepY+12,width:54,height:layout.labels ? 66 : 52),animated:!self.deckListMode) } }
            let occupied = Set(positions.values), layoutID = self.selectedLayoutID
            for slot in Array(emptyTiles.keys) where slot >= count || occupied.contains(slot) { emptyTiles.removeValue(forKey:slot)?.removeFromSuperview() }
            for slot in 0..<count where !occupied.contains(slot) && emptyTiles[slot] == nil {
                let tile = DeckTile(String(slot+1)) { [weak self] in self?.selectedDeck = nil; self?.selectedDecks.removeAll(); self?.selected.removeAll(); self?.openLibrary() }
                tile.fill = Studio.key; tile.labelColor = Studio.muted; tile.toolTip = "空きマス \(slot+1) — Actionやファイルをドラッグ"
                tile.frame = NSRect(x:CGFloat(slot%columns)*stepX,y:CGFloat(slot/columns)*stepY+12,width:54,height:52)
                tile.enableDrop { [weak self] json in self?.receiveDeckAction(json,layout:layoutID,slot:slot) ?? false }
                tile.receiveFile = { [weak self] url in self?.receiveDeckFile(url,layout:layoutID,slot:slot) ?? false }
                tile.movePreviewRoot = grid
                tile.emptyMoveSlot = slot
                tile.movePreviewContext = { [weak self] in self.map { ($0.config,layoutID,slot) } }
                tile.moveItems = { [weak self] ids, anchor, target in self?.moveDeckItems(ids,layout:layoutID,anchor:anchor,slot:target) ?? false }
                tile.contextBuilder = { [weak self] in
                    guard let self = self else { return nil }
                    let menu = NSMenu(); menu.autoenablesItems = false
                    for (title,tag,enabled) in [("コピーした割り当てを貼り付け",5,!self.deckClipboard.isEmpty),("クリップボードのファイルを登録",33,DeckFilePreview.clipboardFile() != nil)] {
                        let item = NSMenuItem(title:title,action:#selector(WorkspaceController.emptyDeckContext(_:)),keyEquivalent:""); item.target = self; item.tag = tag; item.representedObject = ["layout":layoutID,"slot":slot] as [String:Any]; item.isEnabled = enabled; menu.addItem(item)
                    }
                    return menu
                }
                tile.enableDeckDrops(); grid.addSubview(tile); emptyTiles[slot] = tile
            }
            for (slot,tile) in emptyTiles { tile.frame = NSRect(x:CGFloat(slot%columns)*stepX,y:CGFloat(slot/columns)*stepY+12,width:54,height:52) }
            for slot in Array(summaries.keys) where slot >= count { summaries.removeValue(forKey:slot)?.removeFromSuperview() }
            for summary in summaries.values { summary.isHidden = !self.deckListMode }
            if self.deckListMode {
                grid.frame = NSRect(x:0,y:0,width:scroll.bounds.width,height:max(scroll.bounds.height,CGFloat(count)*72+12))
                let bySlot = Dictionary(uniqueKeysWithValues:positions.map { ($0.value,$0.key) })
                for slot in 0..<count {
                    let y = CGFloat(slot)*72+12
                    if let id = bySlot[slot],let cell = self.deckEditorCells[id] {
                        cell.frame = NSRect(x:0,y:y,width:54,height:52); self.deckEditorLabels[id]?.isHidden = true
                    } else { emptyTiles[slot]?.frame = NSRect(x:0,y:y,width:54,height:52) }
                    let summary = summaries[slot] ?? Studio.label("",12)
                    if summaries[slot] == nil { summaries[slot] = summary; grid.addSubview(summary) }
                    summary.maximumNumberOfLines = 2; summary.lineBreakMode = .byTruncatingTail; summary.isHidden = false
                    if let id = bySlot[slot],let item = self.config.deck.first(where:{ $0.id == id }),let mapping = self.config.mapping(for:item) { summary.stringValue = "\(slot+1). \(item.title)\n\(mapping.targetDescription(layout:self.config.keyboardLayout))" }
                    else { summary.stringValue = "\(slot+1). 未割り当て — Actionやファイルをドラッグ" }
                    summary.frame = NSRect(x:70,y:y+6,width:max(40,scroll.bounds.width-86),height:42)
                }
                grid.bounds = NSRect(origin:.zero,size:grid.frame.size)
            } else {
                for label in self.deckEditorLabels.values { label.isHidden = !layout.labels }
                let natural = NSSize(width:CGFloat(columns)*stepX,height:CGFloat((count+columns-1)/columns)*stepY+12)
                // Keep labels and targets readable; use native scrolling for large layouts.
                let scale: CGFloat = 1
                grid.frame = NSRect(x:0,y:0,width:natural.width*scale,height:natural.height*scale)
                grid.bounds = NSRect(origin:.zero,size:natural)
            }
        }
        deckEditor = panel
        return panel
    }
    @objc private func deckViewChanged(_ sender: NSSegmentedControl) {
        deckListMode = sender.selectedSegment == 1; deckEditor?.needsLayout = true
    }
    private func editDeckPresentation() { deckDrawerOpen.toggle(); deckEditor?.needsLayout = true }
    @objc private func renameDeckInline(_ sender: NSTextField) {
        let name = sender.stringValue.trimmingCharacters(in:.whitespacesAndNewlines)
        guard !name.isEmpty else { sender.stringValue = config.deckLayout(selectedLayoutID).name; return }
        var candidate = config; var value = candidate.deckLayout(selectedLayoutID); value.name = name; candidate.setDeckLayout(value); _ = transact(candidate)
    }
    private func setDeckProfileLink(_ enabled: Bool) {
        let id = selectedLayoutID, editorProfile = profileID
        let apply: () -> Void = { [weak self] in
            guard let self = self,self.config.deckLayoutIDs.contains(id) else { return }
            do {
                var candidate = self.config; var destination = id
                if enabled { try candidate.enableDeckProfiles(id,profile:editorProfile) }
                else { destination = candidate.disableDeckProfiles(id) }
                if self.transact(candidate),destination != self.selectedLayoutID { self.selectDeckLayout(destination) }
            } catch { self.onStatus?(error.localizedDescription) }
            self.deckDrawer.update(self.config,layout:self.config.deckLayout(self.selectedLayoutID),profile:self.profileID)
        }
        if !enabled,config.deckLayout(id).profileGroupID != nil,let window = view.window {
            let confirm = NSAlert(); confirm.messageText = "プロファイル別Deckを共通に戻しますか？"
            confirm.informativeText = "標準プロファイルのDeckを共通で使用し、同じグループの他のDeckを削除します。元に戻す操作で復元できます。"
            confirm.addButton(withTitle:"共通に戻す"); confirm.addButton(withTitle:"キャンセル")
            confirm.beginSheetModal(for:window) { response in if response == .alertFirstButtonReturn { apply() } }
        } else { apply() }
    }
    @objc private func addDeckMonitor(_ sender: NSPopUpButton) {
        let index = sender.indexOfSelectedItem-1; guard DeckMonitor.allCases.indices.contains(index) else { return }
        let monitor = DeckMonitor.allCases[index]; var candidate = config
        candidate.deck.append(DeckItem(title:monitor.title,profileID:"default",layer:.normal,input:0,directAction:Mapping(layer:.normal,input:0,action:monitor == .autoExtract ? .system : .disabled,payload:monitor == .autoExtract ? SystemCommand.toggleAutoExtract.rawValue : ""),monitor:monitor))
        _ = transact(candidate); sender.selectItem(at:0)
    }
    @objc private func deckLayoutSelected(_ sender: NSPopUpButton) { selectDeckLayout(editorDeckIDs[sender.indexOfSelectedItem]) }
    func selectDeckLayout(_ id: String) {
        guard config.deckLayoutIDs.contains(id) else { return }
        deckGallery = false; selectedLayoutID = id; deckMultiSelect = false; selectedDecks.removeAll(); selectedDeck = nil; deckAnchor = nil; deckEditor = nil
        if section == "deck" { renderCenter(); renderInspector() }
    }
    @objc private func deckBehaviorChanged(_ sender: NSPopUpButton) {
        var candidate = config
        if sender.tag == 0 { candidate.deckAfterAction = DeckDisplayBehavior.allCases[sender.indexOfSelectedItem] }
        else { candidate.deckOnLeave = DeckDisplayBehavior.allCases[sender.indexOfSelectedItem] }
        _ = transact(candidate)
    }
    private func manageDeckLayout(_ operation: Int) {
        var candidate = config
        if operation == 3 {
            guard selectedLayoutID != "default" else { return }
            let group = candidate.deckLayout(selectedLayoutID).profileGroupID
            let ids = group.map { value in (candidate.deckLayouts ?? []).filter { $0.profileGroupID == value }.map { $0.id } } ?? [selectedLayoutID]
            for id in ids where id != "default" { candidate.removeDeckLayout(id,replacement:"default") }
            if ids.contains("default") { var shared = candidate.deckLayout("default"); shared.profileGroupID = nil; shared.linkedProfileID = nil; candidate.setDeckLayout(shared) }
            deckEditor = nil; _ = transact(candidate); return
        }
        guard let window = view.window else { return }
        let alert = NSAlert(); alert.messageText = operation == 2 ? "Deckの名前" : "Deckを作成"
        let name = NSTextField(string:operation == 2 ? config.deckLayout(selectedLayoutID).name : "Deck \(config.deckLayoutIDs.count+1)")
        name.frame = NSRect(x:0,y:0,width:280,height:28); alert.accessoryView = name
        alert.addButton(withTitle:"保存"); alert.addButton(withTitle:"キャンセル")
        let sourceID = selectedLayoutID
        alert.beginSheetModal(for:window) { [weak self] response in
            guard let self = self,response == .alertFirstButtonReturn else { return }
            var candidate = self.config
            var layout = operation == 2 ? self.config.deckLayout(sourceID) : DeckLayout(columns:9)
            layout.name = name.stringValue.trimmingCharacters(in:.whitespacesAndNewlines)
            if operation == 1 {
                let source = self.config.deckLayout(sourceID); let identity = layout.id, name = layout.name; layout = source; layout.id = identity; layout.name = name; layout.expanded = nil; layout.collapsed = nil; layout.pinned = false; layout.profileGroupID = nil; layout.linkedProfileID = nil
                candidate.deck += self.config.deckItems(sourceID).map { item in var copy = item; copy.id = UUID().uuidString; copy.layoutID = layout.id; return copy }
            }
            candidate.setDeckLayout(layout); self.deckEditor = nil
            if self.transact(candidate) { self.selectDeckLayout(layout.id) }
        }
    }
    @discardableResult func moveDeckItems(_ ids: [String], layout: String, anchor: String, slot: Int) -> Bool {
        var candidate = config
        guard candidate.moveDeck(Set(ids),layout:layout,anchoredAt:anchor,toSlot:slot),transact(candidate) else { return false }
        selectedDecks = Set(ids); selectedDeck = ids.first
        for (id,button) in deckEditorButtons { button.selected = selectedDecks.contains(id) }; return true
    }
    @discardableResult private func placeDeckAction(_ mapping: Mapping, title: String, layout: String, slot: Int, monitor: DeckMonitor? = nil) -> Bool {
        guard config.deckLayoutIDs.contains(layout),(0..<config.deckSlotCount(layout)).contains(slot),!config.deckPositions(layout).values.contains(slot) else { return false }
        var candidate = config
        // Freeze existing implicit positions before filling a hole.
        let positions = candidate.deckPositions(layout)
        for index in candidate.deck.indices where positions[candidate.deck[index].id] != nil { candidate.deck[index].slot = positions[candidate.deck[index].id] }
        candidate.deck.append(DeckItem(title:title,profileID:"default",layer:.normal,input:0,directAction:mapping,layoutID:layout,monitor:monitor,slot:slot))
        return transact(candidate)
    }
    @discardableResult func receiveDeckAction(_ json: String, layout: String, slot: Int) -> Bool {
        guard editable,let data = json.data(using:.utf8),let value = try? JSONDecoder().decode(StudioAction.self,from:data),value.kind != .gesture else { return false }
        guard config.deckLayoutIDs.contains(layout),(0..<config.deckSlotCount(layout)).contains(slot),!config.deckPositions(layout).values.contains(slot) else { return false }
        if !value.parameterized { return placeDeckAction(value.mapping(layer:.normal,input:0),title:value.name,layout:layout,slot:slot,monitor:value.deckMonitor) }
        resolve(value) { [weak self] resolved in _ = self?.placeDeckAction(resolved.mapping(layer:.normal,input:0),title:resolved.name,layout:layout,slot:slot,monitor:resolved.deckMonitor) }
        return true
    }
    @discardableResult func receiveDeckFile(_ url: URL, layout: String, slot: Int) -> Bool {
        guard url.isFileURL,FileManager.default.fileExists(atPath:url.path) else { return false }
        return placeDeckAction(Mapping(layer:.normal,input:0,action:url.pathExtension.lowercased() == "app" ? .launch : .file,application:url.path),title:url.deletingPathExtension().lastPathComponent,layout:layout,slot:slot)
    }
    @discardableResult func receiveDeckFile(_ url: URL, to id: String?) -> Bool {
        guard url.isFileURL, FileManager.default.fileExists(atPath:url.path) else { return false }
        var candidate = config
        let mapping = Mapping(layer:.normal,input:0,action:url.pathExtension.lowercased() == "app" ? .launch : .file,application:url.path)
        if let id = id {
            guard let index = config.deck.firstIndex(where: { $0.id == id }) else { return false }
            candidate.deck[index].directAction = mapping
            candidate.deck[index].monitor = nil
            candidate.deck[index].title = url.deletingPathExtension().lastPathComponent
        } else {
            candidate.deck.append(DeckItem(title:url.deletingPathExtension().lastPathComponent,profileID:"default",layer:.normal,input:0,directAction:mapping))
        }
        return transact(candidate)
    }
    @discardableResult func editDeckItem(_ id: String, edit: DeckEdit) -> Bool {
        var candidate = config
        guard let index = candidate.deck.firstIndex(where: { $0.id == id }) else { return false }
        switch edit {
        case .rename(let title): candidate.deck[index].title = title
        case .remove: candidate.deck.remove(at:index)
        case .color(let color): candidate.deck[index].colorHex = color
        case .icon(let data): candidate.deck[index].iconPNG = data
        case .move(let offset):
            let layout = candidate.deck[index].layoutID ?? "default", positions = candidate.deckPositions(candidate.deck[index].layoutID ?? "default")
            guard let source = positions[id],candidate.moveDeck([id],layout:layout,anchoredAt:id,toSlot:source+offset) else { return false }
        }
        return transact(candidate)
    }
    @discardableResult func editDeckItems(_ ids: [String], edit: DeckEdit) -> Bool {
        let selected = Set(ids)
        guard !selected.isEmpty,selected.isSubset(of:Set(config.deck.map { $0.id })) else { return false }
        var candidate = config
        switch edit {
        case .remove: candidate.deck.removeAll { selected.contains($0.id) }
        case .icon(let data):
            for index in candidate.deck.indices where selected.contains(candidate.deck[index].id) { candidate.deck[index].iconPNG = data }
        case .color(let color):
            for index in candidate.deck.indices where selected.contains(candidate.deck[index].id) { candidate.deck[index].colorHex = color }
        default: return false
        }
        return transact(candidate)
    }
    @objc private func deckColumnsChanged(_ sender: NSPopUpButton) {
        var candidate = config; var layout = config.deckLayout(selectedLayoutID); layout.columns = sender.indexOfSelectedItem + 1; candidate.setDeckLayout(layout); _ = transact(candidate)
    }
    @objc private func deckLabelsChanged(_ sender: NSButton) {
        var candidate = config; var layout = config.deckLayout(selectedLayoutID); layout.labels = sender.state == .on; candidate.setDeckLayout(layout); _ = transact(candidate)
    }
    @objc private func deckMenu(_ sender: NSMenuItem) {
        guard let id = sender.representedObject as? String else { return }
        if !selectedDecks.contains(id) { selectDeck(id, flags: []) }
        if sender.tag == 33 { if let url = DeckFilePreview.clipboardFile() { _ = receiveDeckFile(url,to:id) }; return }
        if (30...32).contains(sender.tag) {
            guard let item = config.deck.first(where:{ $0.id == id }),let mapping = config.mapping(for:item),[.file,.launch].contains(mapping.action) else { return }
            let url = URL(fileURLWithPath:mapping.application)
            guard FileManager.default.fileExists(atPath:url.path) else { onStatus?("登録ファイルが見つかりません。"); return }
            if sender.tag == 30 { NSPasteboard.general.clearContents(); NSPasteboard.general.writeObjects([url as NSURL]) }
            else if sender.tag == 31 { NSWorkspace.shared.activateFileViewerSelecting([url]) }
            else if let button = deckEditorButtons[id] { _ = DeckFilePreview.shared.show(url,anchor:button,owner:id) }; return
        }
        if sender.tag == 0 { onRenameDeck?(id); return }
        if sender.tag == 14 { onChooseDeckColor?(Array(selectedDecks)); return }
        if sender.tag == 20 { onChooseDeckIcon?(Array(selectedDecks)); return }
        if sender.tag == 21 { _ = editDeckItems(Array(selectedDecks),edit:.icon(nil)); return }
        var candidate = config
        switch sender.tag {
        case 2: candidate.deck.removeAll { selectedDecks.contains($0.id) }
        case 3: copyDeckAssignments(selectedDecks); return
        case 5: if let slot = config.deckPositions(selectedLayoutID)[id] { _ = pasteDeckAssignments(layout:selectedLayoutID,slot:slot) }; return
        case 4:
            let values = config.deck.filter { selectedDecks.contains($0.id) }
            for var value in values { value.id = UUID().uuidString; value.slot = nil; value.layoutID = selectedLayoutID == "default" ? nil : selectedLayoutID; candidate.deck.append(value) }
        case 10...13:
            let colors: [Int:String] = [10:"245A86",11:"187964",12:"A75F27"]
            for i in candidate.deck.indices where selectedDecks.contains(candidate.deck[i].id) { candidate.deck[i].colorHex = colors[sender.tag] }
        default:
            if let slot = config.deckPositions(selectedLayoutID)[id] {
                _ = moveDeckItems(Array(selectedDecks), layout:selectedLayoutID, anchor:id, slot:slot+sender.tag)
            }
            return
        }
        _ = transact(candidate)
    }
    @objc private func emptyDeckContext(_ sender: NSMenuItem) {
        guard let target = sender.representedObject as? [String:Any],let layout = target["layout"] as? String,let slot = target["slot"] as? Int else { return }
        if sender.tag == 5 { _ = pasteDeckAssignments(layout:layout,slot:slot) }
        else if sender.tag == 33,let url = DeckFilePreview.clipboardFile() { _ = receiveDeckFile(url,layout:layout,slot:slot) }
    }
    private func copyDeckAssignments(_ ids: Set<String>) {
        let positions = config.deckPositions(selectedLayoutID)
        deckClipboard = config.deck.filter { ids.contains($0.id) }.sorted { positions[$0.id,default:0] < positions[$1.id,default:0] }.map { item in
            var copy = item; copy.directAction = config.mapping(for:item); copy.profileID = "default"; return copy
        }
    }
    @discardableResult private func pasteDeckAssignments(layout: String, slot: Int) -> Bool {
        guard !deckClipboard.isEmpty,slot >= 0,slot+deckClipboard.count <= config.deckSlotCount(layout) else { return false }
        var candidate = config
        let positions = config.deckPositions(layout), targets = Dictionary(uniqueKeysWithValues:config.deckItems(layout).map { (positions[$0.id]!, $0.id) })
        for index in candidate.deck.indices { if let oldSlot = positions[candidate.deck[index].id] { candidate.deck[index].slot = oldSlot } }
        for (offset,original) in deckClipboard.enumerated() {
            var copy = original; copy.id = targets[slot+offset] ?? UUID().uuidString; copy.slot = slot+offset; copy.layoutID = layout
            if let index = candidate.deck.firstIndex(where:{ $0.id == copy.id }) { candidate.deck[index] = copy } else { candidate.deck.append(copy) }
        }
        return transact(candidate)
    }
    private var keyClipboard: [[Mapping]] = []
    @objc private func copyKeyAssignments() {
        keyClipboard = selected.sorted().map { code in editingMappings().filter { $0.layer == currentLayer && $0.input == code } }
    }
    private func pastedKeyConfiguration() -> Configuration? {
        let targets = selected.sorted()
        guard editable, !targets.isEmpty, !keyClipboard.isEmpty,
              keyClipboard.count == 1 || keyClipboard.count == targets.count,
              keyClipboard.contains(where:{ !$0.isEmpty }) else { return nil }
        var candidate = config
        for (offset,code) in targets.enumerated() {
            for source in keyClipboard[keyClipboard.count == 1 ? 0 : offset] {
                guard InputPolicy.allows(code,layer:currentLayer,trigger:source.trigger) else { return nil }
                var copy = source; copy.layer = currentLayer; copy.input = code
                let scoped = editingMouseID != nil && MouseDeviceOverride.supports(currentLayer,code)
                if scoped, let id = editingMouseID { candidate.setMouseDeviceMapping(copy,deviceID:id,profileID:profileID,layer:currentLayer,input:code,trigger:copy.trigger) }
                else {
                    let index = candidate.mappings(for:profileID).firstIndex { $0.layer == currentLayer && $0.input == code && $0.trigger == copy.trigger }
                    candidate.replaceMapping(copy,at:index,profileID:profileID)
                }
            }
        }
        return candidate
    }
    @objc private func pasteKeyAssignments() {
        guard let candidate = pastedKeyConfiguration() else { return }
        _ = transact(candidate)
    }
    @objc private func removeKeyAssignments() {
        var candidate = config
        for item in editingMappings() where item.layer == currentLayer && selected.contains(item.input) {
            if let id = editingMouseID, MouseDeviceOverride.supports(currentLayer,item.input) {
                candidate.setMouseDeviceMapping(nil,deviceID:id,profileID:profileID,layer:currentLayer,input:item.input,trigger:item.trigger)
            } else if let index = candidate.mappings(for:profileID).firstIndex(where:{ $0.layer == item.layer && $0.input == item.input && $0.trigger == item.trigger }) {
                candidate.removeMapping(at:index,profileID:profileID)
            }
        }
        _ = transact(candidate)
    }
    private func keyMenu(_ code: UInt16) -> NSMenu {
        if !selected.contains(code) { selectKey(code) }
        let menu = NSMenu(); menu.autoenablesItems = false
        let hasMappings = editingMappings().contains { $0.layer == currentLayer && selected.contains($0.input) }
        for (title,action,enabled) in [("割り当てをコピー（TAP・HOLD）",#selector(copyKeyAssignments),hasMappings),
                                      ("割り当てを貼り付け",#selector(pasteKeyAssignments),pastedKeyConfiguration() != nil),
                                      ("選択キーの割り当てを削除",#selector(removeKeyAssignments),editable && hasMappings)] {
            let item = NSMenuItem(title:title,action:action,keyEquivalent:""); item.target = self; item.isEnabled = enabled; menu.addItem(item)
        }
        menu.addItem(.separator())
        for operation in Configuration.ScopeOperation.allCases where selectedMouseDeviceID == nil {
            let item = NSMenuItem(title: operation.title, action: #selector(scopeCommand(_:)), keyEquivalent: "")
            item.target = self; item.tag = operation.rawValue; menu.addItem(item)
        }
        return menu
    }
    @objc private func scopeCommand(_ sender: NSMenuItem) {
        guard editable, selectedDeck == nil, let operation = Configuration.ScopeOperation(rawValue: sender.tag) else { return }
        _ = transact(config.applyingScope(operation, profile: profileID, layer: currentLayer, inputs: selected))
    }
    @objc private func toggleDeckMultiSelect(_ sender: StudioButton) {
        deckMultiSelect = sender.state == .on
        sender.selected = deckMultiSelect
        sender.symbol = deckMultiSelect ? "checkmark.circle.fill" : "checkmark.circle"
        if !deckMultiSelect, let id = selectedDeck { selectDeck(id, flags: []) }
    }
    private func selectDeck(_ id: String, flags: NSEvent.ModifierFlags, toggle: Bool = false) {
        guard let item = config.deck.first(where: { $0.id == id }) else { return }
        let additive = flags.contains(.control) || flags.contains(.command)
        let positions = config.deckPositions(selectedLayoutID)
        let ordered = config.deckItems(selectedLayoutID).sorted { positions[$0.id,default:0] < positions[$1.id,default:0] }
        if flags.contains(.shift), let anchor = deckAnchor,
           let a = ordered.firstIndex(where: { $0.id == anchor }), let b = ordered.firstIndex(where: { $0.id == id }) {
            let range = Set(ordered[min(a,b)...max(a,b)].map { $0.id })
            selectedDecks = additive ? selectedDecks.union(range) : range
        } else if additive || toggle {
            if selectedDecks.contains(id) { selectedDecks.remove(id) } else { selectedDecks.insert(id) }
            deckAnchor = id
        } else { selectedDecks = [id]; deckAnchor = id }
        let focused = selectedDecks.contains(id) ? item : ordered.first { selectedDecks.contains($0.id) }
        selectedDeck = focused?.id
        selected = focused.map { [$0.input] } ?? []
        for (id, button) in deckEditorButtons { button.selected = selectedDecks.contains(id) }
        renderInspector()
    }
    private func clearKeySelection() {
        guard section == "keyboard" else { return }
        selected.removeAll(); selectedDeck = nil; anchorKey = nil; selectedTrigger = .tap
        libraryOpen = false
        keyboard.update(editingMappings(),layer:currentLayer,selected:selected,configuration:config,mouseDeviceID:editingMouseID)
        renderInspector()
    }
    func selectKey(_ code: UInt16, flags: NSEvent.ModifierFlags = []) {
        libraryOpen = currentLayer == .trackpad && section == "keyboard"
        selectedDeck = nil
        let additive = flags.contains(.command) || flags.contains(.control)
        if flags.contains(.shift), let anchor = anchorKey {
            let codes = keyboard.orderedCodes
            if let a = codes.firstIndex(of: anchor), let b = codes.firstIndex(of: code) {
                let range = Set(codes[min(a,b)...max(a,b)].filter { InputPolicy.allows($0, layer: currentLayer) })
                selected = additive ? selected.union(range) : range
            }
        } else if additive {
            if selected.contains(code) { selected.remove(code) } else { selected.insert(code) }
            anchorKey = code
        } else { selected = [code]; anchorKey = code }
        onSelectMapping?(profileID, currentLayer, code)
        keyboard.update(editingMappings(), layer: currentLayer, selected: selected, configuration: config, mouseDeviceID:editingMouseID)
        if currentLayer != .trackpad && code >= 300 && keyboard.keys[code] == nil { keyboard.showMouseGrid(true) }
        renderInspector()
    }
    private func selectedAssignments(_ trigger: Trigger) -> [Mapping?] {
        selected.sorted().map { code in
            editingMappings().first { $0.layer == currentLayer && $0.input == code && $0.trigger == trigger }
        }
    }
    private func assignmentsDiffer(_ mappings: [Mapping?]) -> Bool {
        let normalized = mappings.map { mapping -> Mapping? in
            guard var value = mapping else { return nil }; value.input = 0; return value
        }
        guard let first = normalized.first else { return false }
        return normalized.dropFirst().contains { $0 != first }
    }
    private var inspectorWasLibrary: Bool?
    private func renderInspector() {
        updateBackNavigation()
        if inspectorWasLibrary != libraryOpen { Studio.transition(inspector) }
        inspectorWasLibrary = libraryOpen
        inspector.subviews.forEach { $0.removeFromSuperview() }
        if section == "macros" && !libraryOpen { inspector.addSubview(makeMacroTools()); return }
        if libraryOpen {
            let title = Studio.label("Action", 24); title.frame = NSRect(x: 14, y: 0, width: 180, height: 32); inspector.addSubview(title)
            let close = button("", symbol:"xmark") { [weak self] in self?.libraryOpen = false; self?.renderInspector() }; close.setAccessibilityLabel("Action一覧を閉じる"); close.toolTip = "Action一覧を閉じる"; close.frame = NSRect(x: 231, y: 0, width: 36, height: 36); inspector.addSubview(close)
            let note = Studio.label(currentLayer == .trackpad && section == "keyboard" ? (selected.isEmpty ? "操作を選ぶか、図へドラッグ" : "Actionをクリックして割り当て") : selected.isEmpty ? "ドラッグして割り当て" : "編集対象：\(selectedTrigger == .tap ? "短押し" : "長押し")", 11, true); note.frame = NSRect(x: 14, y: 35, width: 230, height: 18); inspector.addSubview(note)
            inspector.addSubview(search); inspector.addSubview(collections); inspector.addSubview(category); inspector.addSubview(actionsScroll)
            addCustomEntrances(y:182)
            reloadActions()
            if section == "keyboard", currentLayer == .trackpad, let code = selected.sorted().first {
                let summary = Studio.label(config.inputName(code),12)
                summary.identifier = NSUserInterfaceItemIdentifier("trackpad-selection")
                summary.toolTip = summary.stringValue
                inspector.addSubview(summary)
                let assignment = editingMappings().first { $0.layer == .trackpad && $0.input == code }
                let name = Studio.label(assignment?.targetDescription(layout:config.keyboardLayout) ?? "未割り当て",12,true)
                name.identifier = NSUserInterfaceItemIdentifier("trackpad-assignment")
                name.toolTip = name.stringValue
                name.setAccessibilityLabel(name.stringValue)
                inspector.addSubview(name)
                if assignment != nil {
                    let remove = button("",symbol:"trash") { [weak self] in self?.removeSelected() }
                    remove.identifier = NSUserInterfaceItemIdentifier("trackpad-remove")
                    remove.setAccessibilityLabel("この割り当てを削除")
                    remove.toolTip = "この割り当てを削除"
                    inspector.addSubview(remove)
                }
            }
        } else if let code = selected.sorted().first {
            let item = selectedDeck.flatMap { id in config.deck.first { $0.id == id } }
            let scroll = InspectorFormScrollView(frame: inspector.bounds)
            scroll.autoresizingMask = [.width, .height]; scroll.hasVerticalScroller = true; scroll.drawsBackground = false
            let content = StudioSurface(frame: NSRect(x: 0, y: 0, width: 274, height: item == nil ? 540 : 330))
            scroll.documentView = content; inspector.addSubview(scroll)
            content.arrange = { [weak content] in
                guard let content = content else { return }
                for child in content.subviews {
                    child.frame.size.width = max(0,content.bounds.width-child.frame.minX*2)
                    child.needsLayout = true
                }
            }
            let inputTitle = currentLayer == .normal ? config.inputName(code) : "\(currentLayer.title) ＋ \(config.inputName(code))"
            let title = Studio.label(item?.title ?? (selected.count > 1 ? "\(selected.count)キーを選択" : inputTitle), 18)
            title.frame = NSRect(x: 14, y: 8, width: 246, height: 32); title.toolTip = title.stringValue; content.addSubview(title)
            let scope = selectedMouseDeviceID.map { MouseDeviceTracker.shared.devices[$0] ?? config.mouseDeviceNames?[$0] ?? $0 } ?? "すべてのマウス"
            let context = Studio.label("編集：\(config.profileName(profileID))・\(scope)", 11, true)
            context.frame = NSRect(x: 14, y: 44, width: 246, height: 20); context.toolTip = context.stringValue; content.addSubview(context)
            for (index, trigger) in (item == nil ? Trigger.allCases : [.tap]).enumerated() {
                let card = StudioSurface(frame: NSRect(x: 10, y: 76 + CGFloat(index) * 212, width: 254, height: 200))
                card.isPanel = true; content.addSubview(card)
                card.arrange = { [weak card] in
                    guard let card = card else { return }
                    for child in card.subviews {
                        if child.frame.minY == 146 {
                            if child.identifier?.rawValue.hasPrefix("assignment-remove") == true { child.frame.origin.x = card.bounds.width-138 }
                            else if (child as? StudioButton)?.symbol == "square.grid.3x3" { child.frame.origin.x = card.bounds.width-56 }
                            else { child.frame.size.width = (child as? NSButton)?.title == "割り当てる" ? max(0,card.bounds.width-24) : max(60,card.bounds.width-156) }
                        } else if child is NSPopUpButton { child.frame.origin.x = card.bounds.width-126 }
                        else if child.frame.width > 140 { child.frame.size.width = max(0,card.bounds.width-child.frame.minX*2) }
                    }
                }
                let allowed = item != nil || selected.allSatisfy { InputPolicy.allows($0, layer: currentLayer, trigger: trigger) }
                let heading = button(item == nil ? (trigger == .tap ? "短押し" : "長押し") : "実行する操作", symbol: trigger == .tap ? "hand.tap" : "timer") { [weak self] in self?.selectedTrigger = trigger; self?.renderInspector() }
                heading.selected = selectedTrigger == trigger; heading.frame = NSRect(x: 10, y: 8, width: 234, height: 32); card.addSubview(heading)
                let mapping = item.flatMap { config.mapping(for: $0) } ?? editingMappings().first { $0.layer == currentLayer && $0.input == code && $0.trigger == trigger }
                let assignments = item == nil ? selectedAssignments(trigger) : [mapping]
                let mixed = assignmentsDiffer(assignments)
                let assignedCount = assignments.compactMap { $0 }.count
                if mixed {
                    let note = Studio.label("複数の割り当て", 14)
                    note.frame = NSRect(x:14,y:52,width:226,height:24); card.addSubview(note)
                    let count = Studio.label("登録済み \(assignedCount) ／ 選択 \(assignments.count)",11,true)
                    count.frame = NSRect(x:14,y:80,width:226,height:20); card.addSubview(count)
                } else if let mapping = mapping {
                    var action = StudioAction(id: "summary:\(mapping.action.rawValue):\(mapping.output):\(mapping.modifiers.rawValue):\(mapping.application):\(mapping.payload)", name: mapping.targetDescription(layout: config.keyboardLayout), category: "使用中", symbol: StudioAction.catalog.first(where: { $0.kind == mapping.action && $0.output == mapping.output && $0.modifiers == mapping.modifiers && $0.value == ((mapping.action == .launch || mapping.action == .file) ? mapping.application : mapping.payload) })?.symbol ?? [.key:"keyboard", .shortcut:"command", .text:"text.cursor", .launch:"app", .file:"folder", .url:"link", .macro:"play.rectangle", .gesture:"hand.draw", .profile:"person.crop.rectangle", .deck:"square.grid.3x3", .mouse:"computermouse", .window:"macwindow", .system:"gearshape", .disabled:"nosign"][mapping.action]!, kind: mapping.action, output: mapping.output, modifiers: mapping.modifiers, value: (mapping.action == .launch || mapping.action == .file) ? mapping.application : mapping.payload)
                    action.displayLayout = config.keyboardLayout
                    if item == nil && selected.count == 1 { action.origin = ActionOrigin(profile: profileID, mapping: mapping) }
                    let row = ActionRow(action, favorite: preferences.favorites.contains { $0.id == action.id }) { [weak self] in
                        guard let self = self else { return }; var favorite = action; favorite.origin = nil
                        if self.preferences.favorites.contains(where: { $0.id == favorite.id }) { self.preferences.favorites.removeAll { $0.id == favorite.id } }
                        else { self.preferences.favorites.append(favorite) }; self.savePreferences(); self.renderInspector()
                    }
                    row.frame = NSRect(x: 0, y: 44, width: 252, height: 58); row.toolTip = mapping.targetDescription(layout: config.keyboardLayout); card.addSubview(row)
                } else {
                    let fallback = currentLayer == .normal && (code == 259 || code == 260) ? (code == 259 ? "戻る（標準動作）" : "進む（標準動作）") : "元の入力"
                    let note = Studio.label(trigger == .tap ? fallback : "長押しの割り当てなし", 13, true)
                    note.frame = NSRect(x: 14, y: 58, width: 226, height: 24); card.addSubview(note)
                }
                if trigger == .tap, !mixed, let mapping = mapping, let issue = actionIssue?(mapping) {
                    let inspect = button("実行できない理由",symbol:"exclamationmark.circle") { [weak self] in self?.onInspectAction?(mapping) }
                    inspect.frame = NSRect(x:14,y:108,width:226,height:30); inspect.toolTip = issue
                    inspect.labelColor = .systemRed; card.addSubview(inspect)
                } else if trigger == .hold, !mixed, let mapping = mapping {
                    let label = Studio.label("長押し時間", 11, true); label.frame = NSRect(x: 14, y: 110, width: 90, height: 24); card.addSubview(label)
                    let duration = NSPopUpButton(frame: NSRect(x: 128, y: 108, width: 112, height: 28))
                    let times = Array(Set([50,100,200,300,500,700,1000,1500,2000,5000,10000,mapping.holdMilliseconds])).sorted()
                    duration.addItems(withTitles: times.map { "\($0) ms" }); duration.selectItem(withTitle: "\(mapping.holdMilliseconds) ms")
                    duration.setAccessibilityLabel("長押しを判定する時間"); duration.target = self; duration.action = #selector(holdDurationChanged(_:)); card.addSubview(duration)
                } else {
                    let note = Studio.label(selected.count > 1 ? "変更・解除は選択したキーすべてに反映" : (allowed ? (mapping == nil ? "操作を選んで割り当て" : "ドラッグで移動 · ⌥または⌃でコピー") : "通常のクリック操作は保護されています"), 10, true)
                    note.frame = NSRect(x: 14, y: 108, width: 226, height: 30); note.toolTip = note.stringValue; card.addSubview(note)
                }
                let change = button(assignedCount == 0 ? "割り当てる" : "変更", symbol: assignedCount == 0 ? "plus" : "pencil") { [weak self] in self?.selectedTrigger = trigger; self?.openLibrary() }
                change.frame = NSRect(x: 12, y: 146, width: 98, height: 40); change.isEnabled = allowed; change.fill = Studio.assigned; change.setAccessibilityLabel(assignedCount == 0 ? "\(inputTitle)の\(trigger == .tap ? "短押し" : "長押し")に割り当てる" : "割り当てを変更"); card.addSubview(change)
                let remove = button("解除", symbol: "trash") { [weak self] in self?.selectedTrigger = trigger; self?.removeSelected() }
                remove.frame = NSRect(x: 116, y: 146, width: 76, height: 40); remove.identifier = NSUserInterfaceItemIdentifier("assignment-remove-" + trigger.rawValue); remove.isEnabled = allowed && assignedCount > 0; remove.toolTip = item != nil ? "選択した項目をDeckパネルから削除" : "選択したキーの\(trigger.rawValue.uppercased())を解除"; remove.setAccessibilityLabel(remove.toolTip); card.addSubview(remove)
                let deck = button("", symbol: "square.grid.3x3") { [weak self] in self?.selectedTrigger = trigger; self?.addSelectedToDeck() }
                deck.frame = NSRect(x: 198, y: 146, width: 40, height: 40); deck.isEnabled = assignedCount > 0 && item == nil; deck.toolTip = "この操作をDeckパネルに追加"; deck.setAccessibilityLabel(deck.toolTip); card.addSubview(deck)
                if assignedCount == 0 { remove.isHidden = true; deck.isHidden = true }
            }
            let note = Studio.label("⌘Zで元に戻す", 11, true)
            note.frame = NSRect(x: 14, y: item == nil ? 506 : 294, width: 246, height: 24); content.addSubview(note)

        } else {
            let open = ActionLibraryLaunchButton("", symbol: "list.bullet") { [weak self] in self?.openLibrary() }; open.setAccessibilityLabel("Action一覧を開く"); open.toolTip = "Action一覧を開く"; open.fill = Studio.key; open.font = .systemFont(ofSize: 30); open.identifier = NSUserInterfaceItemIdentifier("library-launcher"); inspector.addSubview(open)
            let title = Studio.label("Actionを選択", 15); title.alignment = .center; title.identifier = NSUserInterfaceItemIdentifier("library-title"); inspector.addSubview(title)
            let note = Studio.label(section == "macros" ? "一覧からマクロ手順へドラッグ" : section == "gestures" ? "一覧からジェスチャーの方向へドラッグ" : section == "deck" ? "一覧からDeckのマスへドラッグ" : "一覧からキーへドラッグ", 11, true); note.alignment = .center; note.identifier = NSUserInterfaceItemIdentifier("library-note"); inspector.addSubview(note)
        }
        for scroll in inspector.subviews.compactMap({ $0 as? InspectorFormScrollView }) { scroll.tile(); scroll.documentView?.layoutSubtreeIfNeeded() }
        layoutInspector()
    }
    private let quickActionIDs = ["launch","shortcut","text"]
    private func addCustomEntrances(y: CGFloat) {
        for (index,id) in quickActionIDs.enumerated() {
            guard let action = StudioAction.catalog.first(where:{ $0.id == id }) else { continue }
            let purpose = section == "macros" ? "手順に追加" : selected.isEmpty ? "作成" : "割り当て"
            let entry = CustomActionEntry(action.name + "・" + purpose,symbol:action.symbol) { [weak self] in self?.createCustomAction(action) }; entry.template = action
            entry.identifier = NSUserInterfaceItemIdentifier("quick-action-" + id)
            entry.fill = Studio.key; entry.frame = NSRect(x:14,y:y+CGFloat(index)*42,width:246,height:36)
            entry.toolTip = "クリック：" + purpose + "。ドラッグ：移動先に割り当て。"
            entry.setAccessibilityLabel(action.name + "：ドラッグして割り当て、クリックして作成")
            inspector.addSubview(entry)
        }
    }
    private func createCustomAction(_ action: StudioAction) {
        if section == "macros", let id = selectedMacroID {
            resolve(action) { [weak self] resolved in self?.appendMacroStep(MacroStep(action:resolved.mapping(layer:.normal,input:0)),to:id) }
            return
        }

        if let key = selected.sorted().first {
            guard let data = try? JSONEncoder().encode(action), let json = String(data:data,encoding:.utf8) else { return }
            _ = receiveDrop(json,key:key,deckID:selectedDeck,trigger:selectedTrigger)
        } else {
            resolve(action) { [weak self] resolved in
                guard let self = self else { return }
                self.preferences.created?.removeAll { $0.id == resolved.id }
                self.preferences.created?.insert(resolved,at:0); self.savePreferences()
                self.createdActionID = resolved.id
                self.libraryOpen = true; self.search.stringValue = ""; self.category.selectItem(withTitle:"すべて"); self.collections.selectedSegment = 3
                self.renderInspector()
                self.actionsScroll.contentView.scroll(to:.zero); self.actionsScroll.reflectScrolledClipView(self.actionsScroll.contentView)
                self.onStatus?("一覧の先頭に追加しました。キーやDeckへドラッグできます。")
            }
        }
    }
    @objc private func holdDurationChanged(_ sender: NSPopUpButton) {
        guard let text = sender.titleOfSelectedItem?.split(separator: " ").first, let ms = Int(text) else { return }
        var candidate = config
        var mappings = candidate.mappings(for: profileID,mouseDevice:editingMouseID)
        for i in mappings.indices where mappings[i].layer == currentLayer && selected.contains(mappings[i].input) && mappings[i].trigger == .hold { mappings[i].holdMilliseconds = ms }
        if let id = editingMouseID {
            for mapping in mappings where mapping.layer == currentLayer && selected.contains(mapping.input) && mapping.trigger == .hold && MouseDeviceOverride.supports(mapping.layer,mapping.input) {
                candidate.setMouseDeviceMapping(mapping,deviceID:id,profileID:profileID,layer:mapping.layer,input:mapping.input,trigger:.hold)
            }
        } else { candidate.setMappings(mappings, for: profileID) }
        _ = transact(candidate)
    }
    private func layoutInspector() {
        search.frame = NSRect(x: 14, y: 65, width: 246, height: 32)
        collections.frame = NSRect(x:14,y:105,width:246,height:28)
        category.frame = NSRect(x: 14, y: 142, width: 246, height: 30)
        let listY = CGFloat(182 + quickActionIDs.count * 42 + 4)
        actionsScroll.frame = NSRect(x: 4, y: listY, width: 266, height: max(1, inspector.bounds.height - listY - 40))
        if let selection = inspector.subviews.first(where:{ $0.identifier?.rawValue == "trackpad-selection" }) {
            let bottom = inspector.bounds.height
            actionsScroll.frame.size.height = max(1,bottom-listY-106)
            selection.frame = NSRect(x:14,y:bottom-94,width:246,height:23)
            inspector.subviews.first { $0.identifier?.rawValue == "trackpad-assignment" }?.frame = NSRect(x:14,y:bottom-66,width:196,height:25)
            inspector.subviews.first { $0.identifier?.rawValue == "trackpad-remove" }?.frame = NSRect(x:226,y:bottom-71,width:34,height:34)
        }
        let y = max(100, inspector.bounds.height * 0.32)
        inspector.subviews.first { $0.identifier?.rawValue == "library-launcher" }?.frame = NSRect(x: (inspector.bounds.width - 68) / 2, y: y, width: 68, height: 68)
        inspector.subviews.first { $0.identifier?.rawValue == "library-title" }?.frame = NSRect(x: 16, y: y + 84, width: 238, height: 24)
        inspector.subviews.first { $0.identifier?.rawValue == "library-note" }?.frame = NSRect(x: 16, y: y + 115, width: 238, height: 20)
    }
    @objc private func filterChanged() { collections.selectedSegment = 0; renderInspector() }
    @objc private func collectionChanged() { search.stringValue = ""; category.selectItem(withTitle:"すべて"); reloadActions() }
    func controlTextDidChange(_ obj: Notification) { reloadActions() }
    private func usedActions() -> [StudioAction] {
        var result: [StudioAction] = []
        for mapping in config.mappings + config.profiles.flatMap({ $0.mappings }) {
            let value = (mapping.action == .launch || mapping.action == .file) ? mapping.application : mapping.payload
            let id = "used:\(mapping.action.rawValue):\(mapping.output):\(mapping.modifiers.rawValue):\(value)"
            if !result.contains(where: { $0.id == id }) { result.append(StudioAction(id: id, name: mapping.targetDescription(layout: config.keyboardLayout), category: "使用中", symbol: "keyboard", kind: mapping.action, output: mapping.output, modifiers: mapping.modifiers, value: value)) }
        }
        return result
    }
    private var createdActionID: String?
    private var deletedLibrary: LibraryPreferences?
    private func undoLibraryDeletion() {
        guard let saved = deletedLibrary else { return }
        preferences = saved; deletedLibrary = nil; savePreferences(); reloadActions()
    }
    private func replaceCustom(_ old: StudioAction, with replacement: StudioAction) {
        preferences.created = (preferences.created ?? []).map { $0.id == old.id ? replacement : $0 }
        preferences.favorites = preferences.favorites.map { $0.id == old.id ? replacement : $0 }
        preferences.recent = preferences.recent.map { $0.id == old.id ? replacement : $0 }
        savePreferences(); reloadActions()
    }
    private func manageCustom(_ value: StudioAction, operation: Int) {
        guard let window = view.window else { return }
        if operation == 0 {
            var template = value; template.parameterized = true
            resolve(template) { [weak self] result in
                var updated = result; updated.id = value.id
                self?.replaceCustom(value,with:updated)
            }; return
        }
        let alert = NSAlert()
        if operation == 1 {
            alert.messageText = "名前を変更"; alert.addButton(withTitle:"保存"); alert.addButton(withTitle:"キャンセル")
            let field = NSTextField(string:value.name); field.frame = NSRect(x:0,y:0,width:320,height:28); alert.accessoryView = field
            alert.beginSheetModal(for:window) { response in
                guard response == .alertFirstButtonReturn else { return }
                let name = field.stringValue.trimmingCharacters(in:.whitespacesAndNewlines); guard !name.isEmpty else { return }
                var updated = value; updated.name = name; self.replaceCustom(value,with:updated)
            }
        } else {
            alert.messageText = "使用先：" + value.name
            let locations = customUsage(value)
            alert.informativeText = locations.isEmpty ? "まだ割り当てられていません。" : locations.joined(separator:"\n")
            alert.addButton(withTitle:"閉じる"); alert.beginSheetModal(for:window)
        }
    }
    private func customUsage(_ value: StudioAction) -> [String] {
        func matches(_ mapping: Mapping) -> Bool {
            let target = value.mapping(layer:mapping.layer,input:mapping.input)
            return mapping.action == target.action && mapping.output == target.output && mapping.modifiers == target.modifiers && mapping.payload == target.payload && mapping.application == target.application
        }
        var result: [String] = []
        for (id,name) in [("default","標準")] + config.profiles.map({ ($0.id,$0.name) }) {
            for m in config.mappings(for:id) where matches(m) {
                result.append("\(name)・\(m.layer.rawValue)・\(Keys.name(m.input,layout:config.keyboardLayout))・\(m.trigger.rawValue.uppercased())")
            }
        }
        for item in config.deck {
            if let mapping = config.mapping(for:item), matches(mapping) { result.append("Deckパネル：" + item.title) }
        }
        return result
    }
    private func isCustomLibraryAction(_ value: StudioAction) -> Bool {
        !value.parameterized && ["text:","shortcut:","launch:","file:"].contains { value.id.hasPrefix($0) }
    }
    private func removeCustomLibraryAction(_ value: StudioAction) {
        deletedLibrary = preferences
        preferences.created?.removeAll { $0.id == value.id }
        preferences.favorites.removeAll { $0.id == value.id }
        preferences.recent.removeAll { $0.id == value.id }
        if createdActionID == value.id { createdActionID = nil }
        savePreferences(); reloadActions()
        onStatus?("一覧から削除しました。割り当て済みの動作は残っています。")
    }
    private var expandedActionGroups = Set<String>()
    private func reloadActions() {
        let term = search.stringValue.trimmingCharacters(in:.whitespacesAndNewlines)
        if !term.isEmpty { category.selectItem(withTitle:"すべて"); collections.selectedSegment = 0 }
        actionRows.subviews.forEach { $0.removeFromSuperview() }
        inspector.subviews.filter { ["clear-recent","undo-library-delete"].contains($0.identifier?.rawValue ?? "") }.forEach { $0.removeFromSuperview() }
        if libraryOpen && collections.selectedSegment == 2 {
            let clear = button("履歴をクリア") { [weak self] in self?.preferences.recent.removeAll(); self?.savePreferences(); self?.reloadActions() }
            clear.identifier = NSUserInterfaceItemIdentifier("clear-recent"); clear.frame = NSRect(x: 140, y: max(190, inspector.bounds.height - 34), width: 120, height: 30); inspector.addSubview(clear)
        }
        if deletedLibrary != nil {
            let undo = button("削除を取り消す",symbol:"arrow.uturn.backward") { [weak self] in self?.undoLibraryDeletion() }
            undo.identifier = NSUserInterfaceItemIdentifier("undo-library-delete")
            undo.frame = NSRect(x:14,y:max(190,inspector.bounds.height-34),width:130,height:30); inspector.addSubview(undo)
        }
        let selectedCategory = category.titleOfSelectedItem ?? "最近使ったもの"
        var values: [StudioAction]
        switch collections.selectedSegment {
        case 1: values = preferences.favorites
        case 2: values = preferences.recent
        case 3: values = preferences.created ?? []
        default:
            let visibleKeys = Set(Keys.outputs(for:config.keyboardLayout).map(\.code))
            values = (preferences.created ?? []) + StudioAction.catalog.filter { !$0.id.hasPrefix("key-") || visibleKeys.contains($0.output) } + profileActions + macroActions + gestureActions + editorDeckIDs.map { StudioAction(id:"deck-layout-" + $0,name:"Deckパネル：" + config.deckLayout($0).name,category:"Deckパネル",symbol:"square.grid.3x3",kind:.deck,value:$0) } + usedActions()
        }
        if section == "deck",collections.selectedSegment == 0 { values += StudioAction.monitorActions }
        if section != "deck" { values.removeAll { $0.deckMonitor != nil } }
        if selectedCategory != "すべて" { values = values.filter { $0.browsingCategory == selectedCategory } }
        if section == "gestures" || (section == "keyboard" && currentLayer == .trackpad) { values.removeAll { $0.kind == .gesture } }
        if section == "keyboard", currentLayer == .trackpad {
            let catalog = StudioAction.catalog
            values.removeAll { value in
                value.id.hasPrefix("used:") && catalog.contains {
                    $0.kind == value.kind && $0.output == value.output && $0.modifiers == value.modifiers && $0.value == value.value && !$0.parameterized
                }
            }
        }
        values = values.map { value in
            var value = value; value.displayLayout = config.keyboardLayout
            if value.kind == .key && value.id.hasPrefix("key-") { value.name = Keys.name(value.output, layout: config.keyboardLayout) }
            return value
        }
        if !term.isEmpty {
            values = values.filter { $0.matchesSearch(term) }
        }
        if collections.selectedSegment == 0 && term.isEmpty {
            values = values.enumerated().sorted {
                func rank(_ value: StudioAction) -> Int { value.id.hasPrefix("key-") ? 2 : value.name.hasPrefix("マウスボタン番号") ? 1 : 0 }
                let a = rank($0.element), b = rank($1.element)
                return a == b ? $0.offset < $1.offset : a < b
            }.map(\.element)
        }
        if values.isEmpty {
            let note = Studio.label(collections.selectedSegment == 2 ? "まだ履歴がありません。「すべて」から選択" : "該当するActionはありません", 10, true)
            note.frame = NSRect(x: 12, y: 16, width: 246, height: 24); actionRows.addSubview(note)
        }
        let grouped = collections.selectedSegment == 0 && term.isEmpty
        func groupName(_ value: StudioAction) -> String {
            if value.id.hasPrefix("key-") { return "単独キー" }
            if value.name.hasPrefix("マウスボタン番号") { return "マウスボタン番号" }
            return value.browsingCategory
        }
        var groups: [String] = []
        for value in values { let name = groupName(value); if !groups.contains(name) { groups.append(name) } }
        var y: CGFloat = 0
        func addRow(_ value: StudioAction) {
            let row = ActionRow(value, favorite: preferences.favorites.contains { $0.id == value.id }) { [weak self] in
                guard let self = self else { return }
                if self.isCustomLibraryAction(value) { self.removeCustomLibraryAction(value); return }
                if self.preferences.favorites.contains(where: { $0.id == value.id }) { self.preferences.favorites.removeAll { $0.id == value.id } }
                else { self.preferences.favorites.append(value) }
                self.savePreferences(); self.reloadActions()
            }
            if section == "keyboard", currentLayer == .trackpad, selected.count == 1, let key = selected.first {
                row.onChoose = { [weak self] in
                    guard let json = try? JSONEncoder().encode(value), let string = String(data:json,encoding:.utf8) else { return }
                    _ = self?.receiveDrop(string,key:key,deckID:nil)
                }
            }
            if isCustomLibraryAction(value) {
                row.removeCustom = { [weak self] in self?.removeCustomLibraryAction(value) }
                row.manageCustom = { [weak self] operation in self?.manageCustom(value,operation:operation) }
            }
            row.frame.origin = NSPoint(x:0,y:y); actionRows.addSubview(row); y += 58
            if value.id == createdActionID { row.revealCreated(); createdActionID = nil }
        }
        if grouped {
            for name in groups {
                let members = values.filter { groupName($0) == name }
                let collapsible = name == "単独キー" || name == "マウスボタン番号"
                let expanded = expandedActionGroups.contains(name)
                if collapsible {
                    let header = button("\(name)（\(members.count)）",symbol:expanded ? "chevron.down" : "chevron.right") { [weak self] in
                        guard let self = self else { return }
                        if self.expandedActionGroups.contains(name) { self.expandedActionGroups.remove(name) } else { self.expandedActionGroups.insert(name) }
                        self.reloadActions()
                    }
                    header.leftAligned = true; header.setAccessibilityLabel(name + (expanded ? "を折り畳む" : "を展開"))
                    header.frame = NSRect(x:4,y:y,width:244,height:36); actionRows.addSubview(header)
                } else {
                    let heading = Studio.label(name,11,true)
                    heading.frame = NSRect(x:12,y:y+8,width:228,height:24); actionRows.addSubview(heading)
                }
                y += 38
                if !collapsible || expanded { members.forEach(addRow) }
            }
        } else { values.forEach(addRow) }
        actionRows.frame = NSRect(x:0,y:0,width:252,height:max(70,y))
    }

    func showSaveResult(_ success: Bool) {
        guard let button = toolbar.subviews.first(where: { $0.identifier?.rawValue == "save" }) as? StudioButton else { return }
        button.title = success ? "保存済み" : "保存失敗"
        button.toolTip = success ? "変更を保存しました。クリックで再保存できます。" : "保存できませんでした。エラーを確認してください。クリックで現在の設定を再保存します。"
        button.setAccessibilityLabel(button.toolTip)
    }
    private func savePreferences() {
        do { let data = try JSONEncoder().encode(preferences); try data.write(to: preferencesURL, options: .atomic) }
        catch { onStatus?("Action履歴を保存できません：\(error.localizedDescription)") }
    }
    @discardableResult func receiveDrop(_ json: String, key: UInt16?, deckID: String?, trigger: Trigger = .tap) -> Bool {
        guard editable, let data = json.data(using: .utf8), let value = try? JSONDecoder().decode(StudioAction.self, from: data),
              key == nil || Keys.inputs.contains(where: { $0.code == key }) else { return false }
        if let monitor = value.deckMonitor {
            guard let id = deckID,let index = config.deck.firstIndex(where:{ $0.id == id }) else { return false }
            var candidate = config; candidate.deck[index].monitor = monitor; candidate.deck[index].directAction = value.mapping(layer:.normal,input:0); candidate.deck[index].title = monitor.title
            return transact(candidate)
        }
        if value.kind == .gesture && (currentLayer == .trackpad || deckID != nil || key == nil || trigger == .hold || key.map { (270...273).contains($0) } == true) { onStatus?("ジェスチャーはキー／マウスボタンのTAPへ割り当ててください。"); return false }
        if let id = deckID, !config.deck.contains(where: { $0.id == id }) { return false }
        let targetProfile = deckID.flatMap { id in config.deck.first { $0.id == id }?.profileID } ?? profileID
        let targetLayer = deckID.flatMap { id in config.deck.first { $0.id == id }?.layer } ?? currentLayer
        let targetTrigger = deckID.flatMap { id in config.deck.first { $0.id == id }?.trigger } ?? trigger
        let targets: Set<UInt16> = key.map { selected.contains($0) && deckID == nil ? selected : [$0] } ?? []
        guard targets.allSatisfy({ InputPolicy.allows($0, layer: targetLayer, trigger: targetTrigger) }) else { return false }
        resolve(value) { [weak self] resolved in
            guard let self = self else { return }
            if let id = deckID, let index = self.config.deck.firstIndex(where: { $0.id == id }), self.config.deck[index].directAction != nil {
                var candidate = self.config
                candidate.deck[index].directAction = resolved.mapping(layer: .normal, input: 0)
                candidate.deck[index].monitor = nil
                _ = self.transact(candidate)
            } else if key == nil {
                var candidate = self.config
                candidate.deck.append(DeckItem(title: resolved.name, profileID: "default", layer: .normal, input: 0, directAction: resolved.mapping(layer: .normal, input: 0)))
                _ = self.transact(candidate)
            }
            else { self.apply(resolved, targets: targets, profile: targetProfile, layer: targetLayer, addDeck: false, trigger: targetTrigger) }
        }
        return true
    }
    private func resolve(_ value: StudioAction, completion: @escaping (StudioAction) -> Void) {
        guard value.parameterized, let window = view.window else { completion(value); return }
        if value.kind == .launch || value.kind == .file {
            let panel = NSOpenPanel(); if value.kind == .launch { panel.allowedContentTypes = [.applicationBundle] }; panel.canChooseDirectories = value.kind == .file; panel.allowsMultipleSelection = false
            panel.title = value.kind == .launch ? "起動するアプリを選択" : "開くファイル・フォルダを選択"
            panel.prompt = "選択"
            panel.directoryURL = URL(fileURLWithPath: "/Applications")
            panel.beginSheetModal(for: window) { response in
                guard response == .OK, let url = panel.url else { return }
                var result = value; result.value = url.path; result.name = url.deletingPathExtension().lastPathComponent; result.parameterized = false; result.id += ":" + result.value; completion(result)
            }; return
        }
        let alert = NSAlert(); alert.messageText = value.name
        if value.kind == .shortcut { alert.informativeText = "出力するキーを選び、組み合わせる修飾キーをONにしてください。例：Command＋Shift＋S。マウス操作も選べます。" }
        else if value.kind == .text { alert.informativeText = "入力したい文字や定型文を下の欄に記入してください。改行もそのまま入力されます。" }
        alert.addButton(withTitle: selected.isEmpty ? "作成" : "割り当て"); alert.addButton(withTitle: "キャンセル")
        let content = NSView(frame: NSRect(x: 0, y: 0, width: 460, height: value.kind == .shortcut ? 150 : 140))
        let text = NSTextView(frame: content.bounds); text.isRichText = false; text.isAutomaticQuoteSubstitutionEnabled = false; text.isAutomaticDashSubstitutionEnabled = false
        let mouseChoices = StudioAction.catalog.filter { $0.modifiers.isEmpty && ($0.kind == .mouse || ($0.kind == .system && ["scrollUp","scrollDown","scrollLeft","scrollRight"].contains($0.value))) }
        let outputChoices = Keys.outputs(for:config.keyboardLayout)
        let output = CustomOutputPicker(frame: NSRect(x: 0, y: 112, width: 450, height: 28)); output.addItems(withTitles: outputChoices.map { Keys.name($0.code, layout: config.keyboardLayout) } + mouseChoices.map { $0.name })
        let flags: [(String, Modifiers)] = [("⌘ Command",.command),("⌃ Control",.control),("⌥ Option",.option),("⇧ Shift",.shift),("fn",.fn)]
        var checks: [NSButton] = []
        if value.kind == .shortcut {
            content.addSubview(output)
            for (i, flag) in flags.enumerated() { let b = StudioToggle(title: flag.0, target: nil, action: nil); b.frame = NSRect(x: (i % 3) * 150, y: 65 - (i / 3) * 36, width: 148, height: 26); content.addSubview(b); checks.append(b) }
        } else { content.addSubview(text) }
        text.string = value.value
        if let index = outputChoices.firstIndex(where:{ $0.code == value.output }) { output.selectItem(at:index) }
        for (index,check) in checks.enumerated() { check.state = value.modifiers.contains(flags[index].1) ? .on : .off }
        output.setAccessibilityLabel("出力するキーまたはマウス操作")
        text.setAccessibilityLabel("入力する文字列・定型文")
        output.target = output; output.action = #selector(CustomOutputPicker.changed)
        alert.accessoryView = content
        alert.beginSheetModal(for: window) { response in
            guard response == .alertFirstButtonReturn else { return }
            var result = value; result.parameterized = false
            if value.kind == .shortcut {
                let index = output.indexOfSelectedItem
                if index < outputChoices.count { result.output = outputChoices[index].code }
                else { result = mouseChoices[index - outputChoices.count] }
                result.modifiers = []; result.modifiers.formUnion(checks.enumerated().reduce(Modifiers()) { $1.element.state == .on ? $0.union(flags[$1.offset].1) : $0 })
            } else { result.value = value.kind == .url ? text.string.trimmingCharacters(in: .whitespacesAndNewlines) : text.string }
            result.id += ":\(result.output):\(result.modifiers.rawValue):\(result.value)"; completion(result)
        }
    }
    private func chooseDeckKey(_ action: StudioAction, profile: String, layer: Layer) {
        guard let window = view.window else { return }
        let alert = NSAlert(); alert.messageText = "Deckと共有するキー"; alert.informativeText = "選んだキーにも同じActionを割り当てます。Mac版のDeckはキー割り当てを参照します。"
        let choices = Keys.inputs.filter { InputPolicy.allows($0.code, layer: layer) }
        let picker = NSPopUpButton(frame: NSRect(x: 0, y: 0, width: 260, height: 28)); picker.addItems(withTitles: choices.map { $0.name })
        alert.accessoryView = picker; alert.addButton(withTitle: "追加"); alert.addButton(withTitle: "キャンセル")
        alert.beginSheetModal(for: window) { [weak self] response in
            if response == .alertFirstButtonReturn { self?.apply(action, targets: [choices[picker.indexOfSelectedItem].code], profile: profile, layer: layer, addDeck: true) }
        }
    }
    private func apply(_ action: StudioAction, targets: Set<UInt16>, profile: String, layer: Layer, addDeck: Bool, trigger: Trigger = .tap) {
        let mouseScope = layer == .trackpad ? nil : selectedMouseDeviceID
        guard !targets.isEmpty, targets.allSatisfy({ InputPolicy.allows($0, layer: layer, trigger: trigger) }), !action.parameterized, profile == "default" || config.profiles.contains(where: { $0.id == profile }) else { return }
        if mouseScope == nil, let origin = action.origin {
            guard config.mappings(for: origin.profile).contains(origin.mapping) else { return }
        }
        var candidate = config
        for code in targets {
            let scoped = mouseScope != nil && MouseDeviceOverride.supports(layer,code) && !addDeck
            let index = candidate.mappings(for: profile,mouseDevice:scoped ? mouseScope : nil).firstIndex { $0.layer == layer && $0.input == code && $0.trigger == trigger }
            var mapped = action.mapping(layer: layer, input: code); mapped.trigger = trigger
            if let index = index { mapped.holdMilliseconds = candidate.mappings(for: profile,mouseDevice:scoped ? mouseScope : nil)[index].holdMilliseconds }
            if scoped, let id = mouseScope { candidate.setMouseDeviceMapping(mapped,deviceID:id,profileID:profile,layer:layer,input:code,trigger:trigger) }
            else { candidate.replaceMapping(mapped, at: index, profileID: profile) }
            if addDeck { candidate.deck.append(DeckItem(title: action.name, profileID: profile, layer: layer, input: code, trigger: trigger)) }
        }
        if mouseScope == nil, let origin = action.origin,
           !(origin.profile == profile && origin.mapping.layer == layer && origin.mapping.trigger == trigger && targets.contains(origin.mapping.input)),
           let index = candidate.mappings(for: origin.profile).firstIndex(of: origin.mapping), let destination = targets.sorted().first {
            // Existing Deck references follow the moved action, within the same atomic undo.
            for i in candidate.deck.indices where candidate.deck[i].directAction == nil && candidate.deck[i].profileID == origin.profile && candidate.deck[i].layer == origin.mapping.layer && candidate.deck[i].input == origin.mapping.input && (candidate.deck[i].trigger ?? .tap) == origin.mapping.trigger {
                candidate.deck[i].profileID = profile; candidate.deck[i].layer = layer; candidate.deck[i].input = destination; candidate.deck[i].trigger = trigger
            }
            candidate.removeMapping(at: index, profileID: origin.profile)
        }
        if transact(candidate) {
            selectedTrigger = trigger
            var recent = action; recent.origin = nil
            preferences.recent.removeAll { $0.id == action.id }; preferences.recent.insert(recent, at: 0); preferences.recent = Array(preferences.recent.prefix(40)); savePreferences()
            onStatus?("\(targets.count)件に「\(action.name)」を割り当てました · ↶で元に戻せます")
            if section == "keyboard", profileID == profile, currentLayer == layer {
                selected = targets; anchorKey = targets.sorted().first; selectedDeck = nil
                libraryOpen = currentLayer == .trackpad
                keyboard.update(editingMappings(),layer:currentLayer,selected:selected,configuration:config, mouseDeviceID:editingMouseID)
                renderInspector()
                keyboard.animateAssignments(targets)
            } else { reloadActions() }
        }
    }
    func saveMouseLayout(_ layout: MouseButtonLayout, deviceID: String?, deviceName: String) -> Bool {
        var candidate = config
        if let deviceID = deviceID {
            candidate.mouseButtonLayoutsByDevice = candidate.mouseButtonLayoutsByDevice ?? [:]
            candidate.mouseButtonLayoutsByDevice?[deviceID] = layout
            if !deviceName.isEmpty {
                candidate.mouseDeviceNames = candidate.mouseDeviceNames ?? [:]
                candidate.mouseDeviceNames?[deviceID] = String(deviceName.prefix(80))
            }
        } else {
            candidate.commonMouseButtonLayout = layout
        }
        return candidate == config || transact(candidate)
    }
    func setReverseMouseScroll(_ enabled: Bool) {
        var candidate = config; candidate.reverseMouseScroll = enabled; _ = transact(candidate)
    }
    @discardableResult private func transact(_ proposed: Configuration) -> Bool {
        var candidate = proposed
        let existing = Set(config.deck.map { $0.id })
        if selectedLayoutID != "default", candidate.deckLayoutIDs.contains(selectedLayoutID) {
            for index in candidate.deck.indices where !existing.contains(candidate.deck[index].id) && candidate.deck[index].layoutID == nil { candidate.deck[index].layoutID = selectedLayoutID }
        }
        // Deletion leaves a real empty cell instead of shifting every following button.
        for id in config.deckLayoutIDs {
            let positions = config.deckPositions(id)
            if config.deckItems(id).contains(where:{ old in !candidate.deck.contains(where:{ $0.id == old.id }) }) {
                for index in candidate.deck.indices where positions[candidate.deck[index].id] != nil && candidate.deck[index].slot == nil { candidate.deck[index].slot = positions[candidate.deck[index].id] }
            }
        }
        let original = config
        committing = true; defer { committing = false }
        guard candidate != original, commit?(candidate) == true else { return false }
        defer { refreshHistoryControls() }
        undoStack.append(original); if undoStack.count > 50 { undoStack.removeFirst() }; redoStack.removeAll()
        undoNoticeGeneration += 1; let ticket = undoNoticeGeneration; undoNotice.isHidden = false
        DispatchQueue.main.asyncAfter(deadline: .now() + 5) { [weak self] in
            guard let self = self, self.undoNoticeGeneration == ticket else { return }; self.undoNotice.isHidden = true
        }
        return true
    }
    private func historyDescription(_ target: Configuration?) -> String {
        guard let target = target else { return "履歴なし" }
        if target.deck != config.deck || target.deckLayouts != config.deckLayouts { return "Deckの編集" }
        if target.profiles != config.profiles { return "プロファイル・割り当ての編集" }
        if target.gestures != config.gestures { return "ジェスチャーの編集" }
        if target.macros != config.macros { return "マクロの編集" }
        if target.mappings != config.mappings { return "キー・レイヤーの割り当て" }
        return "設定の変更"
    }
    var undoTitle: String { "元に戻す：" + historyDescription(undoStack.last) }
    var redoTitle: String { "やり直す：" + historyDescription(redoStack.last) }
    private func refreshHistoryControls() {
        for (id,title,enabled) in [("history-undo",undoTitle,canUndo),("history-redo",redoTitle,canRedo)] {
            if let button = toolbar.subviews.first(where: { $0.identifier?.rawValue == id }) as? NSButton {
                button.toolTip = title; button.setAccessibilityLabel(title); button.isEnabled = enabled
            }
        }
        undoNotice.toolTip = undoTitle
    }
    var canUndo: Bool { !undoStack.isEmpty }
    var canRedo: Bool { !redoStack.isEmpty }
    func undo() {
        defer { refreshHistoryControls() }
        guard let previous = undoStack.last else { return }; let original = config
        committing = true; defer { committing = false }; undoNotice.isHidden = true
        if commit?(previous.preservingDeckPresentation(from:config)) == true { undoStack.removeLast(); redoStack.append(original); onStatus?("割り当てを元に戻しました") }
    }
    func redo() {
        defer { refreshHistoryControls() }
        guard let next = redoStack.last else { return }; let original = config
        committing = true; defer { committing = false }
        if commit?(next.preservingDeckPresentation(from:config)) == true { redoStack.removeLast(); undoStack.append(original); onStatus?("割り当てをやり直しました") }
    }
    private func removeSelected() {
        var candidate = config
        if let id = selectedDeck {
            candidate.deck.removeAll { selectedDecks.contains($0.id) || $0.id == id }
        } else {
            for code in selected {
                if let id = editingMouseID, MouseDeviceOverride.supports(currentLayer,code) {
                    candidate.setMouseDeviceMapping(nil,deviceID:id,profileID:profileID,layer:currentLayer,input:code,trigger:selectedTrigger)
                } else if let index = candidate.mappings(for: profileID).firstIndex(where: { $0.layer == currentLayer && $0.input == code && $0.trigger == selectedTrigger }) {
                    candidate.removeMapping(at:index,profileID:profileID)
                }
            }
        }
        _ = transact(candidate)
    }
    private func addSelectedToDeck() {
        var candidate = config
        for code in selected {
            if let mapping = candidate.mappings(for: profileID).first(where: { $0.layer == currentLayer && $0.input == code && $0.trigger == selectedTrigger }) {
                candidate.deck.append(DeckItem(title: mapping.targetDescription(layout: config.keyboardLayout), profileID: profileID, layer: currentLayer, input: code, trigger: selectedTrigger))
            }
        }
        _ = transact(candidate)
    }
    func verifyEditor() {
        let motionButton = StudioButton("Motion") {}; motionButton.frame = NSRect(x:0,y:0,width:50,height:40)
        Studio.verificationAccessibility = (true,false)
        Studio.assignmentCommit(motionButton)
        verifySmoke(motionButton.layer?.animation(forKey:"relyr-assignment-commit") == nil,"Reduce motion omits assignment bounce")
        Studio.transition(motionButton)
        verifySmoke(motionButton.layer?.animation(forKey:"studio-transition") == nil,"Reduce motion omits screen transitions")
        for appearanceName in [NSAppearance.Name.aqua,.darkAqua] {
            NSAppearance(named:appearanceName)!.performAsCurrentDrawingAppearance {
                for contrast in [false,true] {
                    Studio.verificationAccessibility = (true,contrast)
                    motionButton.frame.size = NSSize(width:160,height:40)
                    for enabled in [false,true] {
                        motionButton.isEnabled = enabled
                        _ = motionButton.bitmapImageRepForCachingDisplay(in:motionButton.bounds)
                        let toggle = StudioThemeSwitch(frame:NSRect(x:0,y:0,width:36,height:22))
                        toggle.isEnabled = enabled
                        for state in [NSControl.StateValue.off,.on] {
                            toggle.state = state
                            if let bitmap = toggle.bitmapImageRepForCachingDisplay(in:toggle.bounds) { toggle.cacheDisplay(in:toggle.bounds,to:bitmap) }
                        }
                    }
                }
            }
        }
        Studio.verificationAccessibility = nil
        view.addSubview(motionButton); motionButton.layoutSubtreeIfNeeded(); motionButton.verifyMotion()
        let destination = NSRect(x:40,y:50,width:motionButton.frame.width,height:motionButton.frame.height)
        Studio.place(motionButton,at:destination)
        verifySmoke(motionButton.frame == destination,"Reorder commits destination before visual movement completes")
        Studio.verificationAccessibility = (true,false)
        Studio.place(motionButton,at:destination.offsetBy(dx:10,dy:0))
        verifySmoke(motionButton.layer?.animation(forKey:"studio-reorder") == nil,"Reduced motion cancels reorder animation")
        Studio.verificationAccessibility = nil; motionButton.removeFromSuperview()
        verifySmoke(Studio.changedDeckAssignments(from:config,to:config).isEmpty,"Unchanged Deck does not replay assignment animation")

        for kind: ActionType in [.key,.text,.macro,.launch,.profile,.gesture,.shortcut,.disabled] {
            let color = Studio.assignmentFill(kind).usingColorSpace(.sRGB)!
            func linear(_ x: CGFloat) -> CGFloat { x <= 0.04045 ? x/12.92 : pow((x+0.055)/1.055,2.4) }
            let luminance = 0.2126*linear(color.redComponent) + 0.7152*linear(color.greenComponent) + 0.0722*linear(color.blueComponent)
            verifySmoke(Studio.assignmentText(kind) == .white,"Every assignment uses the same white foreground")
            verifySmoke(1.05/(luminance+0.05) >= 4.5,"Small assignment labels retain readable contrast")
            verifySmoke(color.alphaComponent == 1,"Assignment fill is opaque")
        }

        setStatus("一時停止：検証",enabled:false,requested:true)
        verifySmoke(awaitingRecovery && footerToggle.title.contains("理由を見る"),"Recovery status offers diagnostics instead of stopping")
        setStatus("停止中",enabled:false,requested:false)
        verifySmoke(!awaitingRecovery,"Explicit stop retains normal enable action")

        let assignmentBaseline = config
        showLayer(.space); selected = [0]; libraryOpen = true
        apply(StudioAction(id:"immediate-clear-test",name:"確認",category:"文字",symbol:"textformat",kind:.text,value:"test"),targets:[0],profile:profileID,layer:.space,addDeck:false)
        verifySmoke(!libraryOpen && selected == [0],"Assignment immediately reveals its inspector and clear action")
        removeSelected()
        verifySmoke(!config.mappings(for:profileID).contains { $0.layer == .space && $0.input == 0 && $0.trigger == .tap },"Immediate clear removes the assigned TAP")
        undo(); verifySmoke(config.mappings(for:profileID).contains { $0.layer == .space && $0.input == 0 && $0.payload == "test" },"Clear can be undone")
        undo(); verifySmoke(config == assignmentBaseline,"Assignment and clear test restores configuration")

        let clipboardBaseline = config
        var clipboardFixture = config
        let tap = Mapping(layer:.space,input:0,action:.text,payload:"copy-test")
        let hold = Mapping(layer:.space,input:0,action:.key,output:1,trigger:.hold)
        clipboardFixture.mappings.removeAll { $0.layer == .space && $0.input == 0 }
        clipboardFixture.mappings += [tap,hold]
        _ = transact(clipboardFixture); showLayer(.space); selected = [0]; copyKeyAssignments()
        selected = [0, UInt16.max]
        verifySmoke(assignmentsDiffer(selectedAssignments(.tap)),"Assigned and unassigned selection is mixed")
        verifySmoke(!assignmentsDiffer([tap, Mapping(layer:.space,input:2,action:.text,payload:"copy-test")]),"Matching actions across keys are not mixed")
        var otherHold = hold; otherHold.holdMilliseconds = 700
        verifySmoke(assignmentsDiffer([hold,otherHold]),"Different hold durations are mixed")
        openLibrary()
        keyboard.onSelect?(0, [])
        verifySmoke(!libraryOpen && selected == [0] && inspectorWasLibrary == false,"Key click replaces Action library with assignment inspector")
        openLibrary()
        keyboard.onSelect?(2, [.command])
        verifySmoke(!libraryOpen && selected == [0,2],"Key click from Action library preserves additive selection")
        openLibrary()
        keyboard.onSelect?(2, [])
        verifySmoke(!libraryOpen && selected == [2] && inspectorWasLibrary == false,"Unassigned key also replaces Action library")
        keyboard.onSelect?(0, [])
        if let scroll = inspector.subviews.first as? NSScrollView, let document = scroll.documentView {
            let cards = document.subviews.compactMap { $0 as? StudioSurface }
            verifySmoke(cards.count == 2 && cards[0].frame.maxY < cards[1].frame.minY,"Assignment TAP/HOLD cards do not overlap")
            verifySmoke(cards.allSatisfy { card in card.subviews.allSatisfy { card.bounds.contains($0.frame) } },"Assignment controls remain inside their card")
        } else { verifySmoke(false,"Assignment inspector is scrollable") }
        saveSmokePreview(view,to:URL(fileURLWithPath:CommandLine.arguments[2]).deletingPathExtension().path + "-assignment-inspector.png")

        selected = [2,3]; pasteKeyAssignments()
        verifySmoke([UInt16(2),3].allSatisfy { code in config.mappings.contains { $0.layer == .space && $0.input == code && $0.trigger == .tap && $0.payload == "copy-test" } && config.mappings.contains { $0.layer == .space && $0.input == code && $0.trigger == .hold } },"Context paste retains TAP/HOLD on each selected target")
        undo(); verifySmoke(config == clipboardFixture,"Context paste is one undo transaction")
        showLayer(.normal); selected = [256]
        verifySmoke(pastedKeyConfiguration() == nil,"Context paste rejects protected left click atomically")
        undo(); verifySmoke(config == clipboardBaseline,"Context clipboard test restores configuration")
        keyClipboard = []; selected = []; showLayer(.space)
        print("PASS: key context clipboard, multi-target TAP/HOLD, protected input and atomic undo")

        let customBaseline = config, savedLibrary = preferences
        let customFixture = StudioAction(id:"text:verification",name:"追加位置テスト",category:"文字列",symbol:"textformat",kind:.text,value:"fixture")
        createCustomAction(customFixture)
        verifySmoke(preferences.created?.first?.id == customFixture.id,"New custom action is first")
        verifySmoke((actionRows.subviews.first as? ActionRow)?.value.id == customFixture.id,"New custom action is first visible row")
        verifySmoke(actionsScroll.contentView.bounds.minY == 0,"Creation scrolls to first row")
        verifySmoke((actionRows.subviews.first as? ActionRow)?.removeCustom != nil,"Custom row exposes deletion")
        search.stringValue = "追加位置テスト"; reloadActions()
        verifySmoke(actionRows.subviews.contains { ($0 as? ActionRow)?.value.id == customFixture.id },"Created action searchable from All")
        removeCustomLibraryAction(customFixture)
        verifySmoke(!preferences.favorites.contains { $0.id == customFixture.id } && !preferences.recent.contains { $0.id == customFixture.id },"Removal clears library copies")
        verifySmoke(!(preferences.created ?? []).contains { $0.id == customFixture.id },"Deleted custom removed")
        undoLibraryDeletion()
        verifySmoke(preferences.created?.first?.id == customFixture.id,"Undo restores custom action")
        var renamed = customFixture; renamed.name = "変更後"
        replaceCustom(customFixture,with:renamed)
        verifySmoke(preferences.created?.first?.name == "変更後","Custom rename persists")
        verifySmoke(createdActionID == nil,"Creation animation consumed once")
        verifySmoke(config == customBaseline,"Library creation/deletion preserves assignments and Deck")
        preferences = savedLibrary; createdActionID = nil; savePreferences(); search.stringValue = ""; collections.selectedSegment = 0
        print("PASS: custom action insertion, visible first row, scroll reset, deletion, search and assignment preservation")
        let oldCategory = category.titleOfSelectedItem, oldSearch = search.stringValue
        for name in category.itemTitles {
            category.selectItem(withTitle:name); search.stringValue = "アプリを終了"
            controlTextDidChange(Notification(name:NSControl.textDidChangeNotification,object:search))
            verifySmoke(category.titleOfSelectedItem == "すべて","Typing switches every category to All")
            verifySmoke(actionRows.subviews.contains { $0 is ActionRow },"Quit action found from every category")
        }
        search.stringValue = ""; category.selectItem(withTitle:"マウス・キー"); reloadActions()
        verifySmoke(category.titleOfSelectedItem == "マウス・キー","Empty search permits category browsing")
        search.stringValue = oldSearch; if let oldCategory = oldCategory { category.selectItem(withTitle:oldCategory) }; reloadActions()
        print("PASS: Action search switches all categories to All; empty search preserves category browsing")
        verifySmoke(StudioAction.catalog.allSatisfy { StudioAction.browsingCategories.contains($0.browsingCategory) },"Every catalog action belongs to one of eight groups")
        for term in ["スクショ","画面撮影","キャプチャ"] {
            verifySmoke(StudioAction.catalog.contains { $0.category == "スクリーンショット" && $0.matchesSearch(term) },"Screenshot aliases")
        }
        for segment in [1,2] {
            collections.selectedSegment = segment; search.stringValue = "スクショ"; reloadActions()
            verifySmoke(collections.selectedSegment == 0 && category.titleOfSelectedItem == "すべて" && actionRows.subviews.contains { $0 is ActionRow },"Search exits saved collection")
        }
        search.stringValue = oldSearch; if let oldCategory = oldCategory { category.selectItem(withTitle:oldCategory) }; reloadActions()
        let beforeWheel = config
        showLayer(.normal)
        menuTrackpad.state = .on; menuTrackpadChanged()
        verifySmoke(config.menuBarTrackpadEnabled == true,"Menu trackpad option saves")
        let roundTrip = try! JSONDecoder().decode(Configuration.self,from:JSONEncoder().encode(config))
        verifySmoke(roundTrip.menuBarTrackpadEnabled == true,"Menu trackpad option persists")
        undo(); verifySmoke(config == beforeWheel && menuTrackpad.state == .off,"Menu trackpad Undo restores checkbox")
        setReverseMouseScroll(true)
        verifySmoke(config.reversesWheel(in:.normal), "Global scroll-only reversal saved")
        undo(); verifySmoke(config == beforeWheel && !config.reversesMouseScroll, "Wheel reversal Undo updates checkbox")
        let beforeDevice = config
        selectedMouseDeviceID = "046d:b042"; refreshMouseScopePicker()
        verifySmoke(mouseScopeIDs[mouseScopePicker.indexOfSelectedItem] == "046d:b042", "Mouse remains selectable for editing")
        var mouseCandidate = config
        let mxAssignment = Mapping(layer:.space,input:260,action:.key,output:12)
        mouseCandidate.setMouseDeviceMapping(mxAssignment,deviceID:"046d:b042",profileID:profileID,layer:.space,input:260,trigger:.tap)
        verifySmoke(transact(mouseCandidate) && editingMappings().contains(mxAssignment), "Mouse-specific assignment appears in selected editor scope")
        verifySmoke(config.mappings(for:profileID).contains(mxAssignment) == false, "Mouse-specific edit does not change common assignments")
        undo(); verifySmoke(config == beforeDevice, "Mouse-specific edit is one undo transaction")
        selectedMouseDeviceID = nil; refreshMouseScopePicker()

        let probe = StudioButton(frame: NSRect(x: 0, y: 0, width: 80, height: 80))
        probe.tapOnlyDrop = true; probe.enableDrop { _ in true }
        probe.supportsTap = false
        verifySmoke(probe.updateDrop(at: NSPoint(x: 20,y: 20)) == [] && !probe.hoveringDrop, "Reserved TAP never advertises an accepted drop")
        verifySmoke(probe.updateDrop(at: NSPoint(x: 20,y: 60)) == .copy && probe.hoveringDrop && probe.dropTrigger == .hold, "Allowed HOLD remains available")
        probe.isEnabled = false
        verifySmoke(!probe.hoveringDrop && probe.updateDrop(at: NSPoint(x:20,y:60)) == [], "Disabled destination clears feedback")
        probe.isEnabled = true; probe.supportsTap = true; probe.supportsHold = false
        verifySmoke(probe.updateDrop(at: NSPoint(x:20,y:60)) == [], "Wheel HOLD rejected before drop")
        verifySmoke(probe.updateDrop(at: NSPoint(x:20,y:20)) == .copy, "Wheel TAP still accepted")
        verifySmoke(probe.updateDrop(at: NSPoint(x:90,y:20)) == [] && !probe.hoveringDrop, "Leaving destination clears feedback")
        probe.supportsHold = true; probe.hasHoldAssignment = true; probe.preservesAssignmentFill = true
        view.addSubview(probe); probe.verifyAssignmentChooser(); probe.removeFromSuperview()

        let original = config
        showLayer(.space); selectKey(0)
        let value = StudioAction.catalog.first { $0.id == "cmd-9" }!
        let json = String(data: try! JSONEncoder().encode(value), encoding: .utf8)!
        verifySmoke(receiveDrop(json, key: 0, deckID: nil), "Library drop accepted")
        verifySmoke(config.mappings(for: profileID).first { $0.input == 0 && $0.layer == .space }?.output == 9, "Drop replaced selected mapping")
        verifySmoke(config.deck.first.flatMap { config.mapping(for: $0) }?.output == 9, "Drop updates shared Deck action")
        undo(); verifySmoke(config == original, "Drop undo restores complete configuration")
        redo(); verifySmoke(config != original, "Drop redo restores assignment"); undo()
        verifySmoke(!receiveDrop("invalid", key: 0, deckID: nil) && !receiveDrop(json, key: 49, deckID: nil), "Invalid and reserved drops rejected")
        verifySmoke(config == original, "Invalid drops preserve configuration")
        selectKey(2, flags: .command); _ = receiveDrop(json, key: 0, deckID: nil)
        verifySmoke(config.mappings(for: profileID).contains { $0.input == 2 && $0.layer == .space && $0.output == 9 }, "Multi-selection drop")
        undo(); verifySmoke(config == original, "Multi-selection undo is atomic")
        let source = original.mappings.first { $0.layer == .space && $0.input == 0 }!
        var moved = StudioAction(id: "move-test", name: "Left", category: "キー", symbol: "keyboard", kind: source.action, output: source.output)
        moved.origin = ActionOrigin(profile: profileID, mapping: source)
        selectKey(2)
        let moveJSON = String(data: try! JSONEncoder().encode(moved), encoding: .utf8)!
        _ = receiveDrop(moveJSON, key: 2, deckID: nil)
        verifySmoke(!config.mappings.contains { $0.input == 0 && $0.layer == .space }, "Summary drag clears source after successful move")
        verifySmoke(config.deck.first?.input == 2 && config.mapping(for: config.deck[0])?.output == source.output, "Deck references follow moved action")
        undo(); verifySmoke(config == original, "Move undo restores source, destination and Deck")
        let id = original.deck[0].id
        _ = receiveDrop(json, key: original.deck[0].input, deckID: id)
        verifySmoke(config.mapping(for: config.deck[0])?.output == 9 && config.deck[0].title == original.deck[0].title, "Deck drop updates action and preserves name")
        undo(); verifySmoke(config == original, "Deck drop undo restores shared action")
        selectedDeck = id; removeSelected()
        verifySmoke(config.mappings == original.mappings && config.deck.count == original.deck.count - 1, "Deck removal preserves keyboard mappings")
        undo(); verifySmoke(config == original, "Deck removal undo")
        let saved = preferences
        if let data = try? Data(contentsOf: preferencesURL), let decoded = try? JSONDecoder().decode(LibraryPreferences.self, from: data) {
            verifySmoke(decoded.recent == saved.recent, "Resolved recent Actions persisted")
        } else { verifySmoke(false, "Library preferences persisted") }
        for value in KeyboardLayout.allCases {
            layoutPicker.selectItem(withTitle: value.rawValue); layoutChanged()
            verifySmoke(config.keyboardLayout == value && keyboard.keyboardLayout == value, "Layout picker changes stored layout and keys")
            verifyGeometry()
        }
        if config != original { undo() }
        verifySmoke(config == original, "Layout undo preserves all mappings and Deck")
        for layer in [Layer.mouseRight, .mouseBack, .mouseForward] {
            showLayer(layer); selectKey(0)
            verifySmoke(mouseButtons[layer]?.isEnabled == true, "Mouse navigation enabled")
            verifySmoke(receiveDrop(json, key: 0, deckID: nil), "Mouse layer accepts action drop")
            verifySmoke(config.mappings.contains { $0.layer == layer && $0.input == 0 }, "Mouse layer persisted")
            undo(); verifySmoke(config == original, "Mouse layer drop undo")
            verifySmoke(!receiveDrop(json, key: layer.mouseCode!, deckID: nil), "Reject layer self assignment before mutation")
        }
        showLayer(.normal)
        verifySmoke(!receiveDrop(json, key: 256, deckID: nil), "Reject normal left drop")
        selectKey(0); selectKey(2, flags: .control); selectKey(3, flags: [.control, .shift])
        verifySmoke(selected.isSuperset(of: [0,2,3]), "Ctrl Shift adds range without losing disjoint selection")
        selectKey(11); verifySmoke(selected == [11], "Ordinary click exits multiple selection")
        selectKey(0); view.layoutSubtreeIfNeeded()
        if let scroll = inspector.subviews.first as? InspectorFormScrollView, let content = scroll.documentView {
            for style in [NSScroller.Style.legacy,.overlay] {
                scroll.scrollerStyle = style; scroll.tile(); content.layoutSubtreeIfNeeded()
                verifySmoke(content.subviews.allSatisfy { $0.frame.minX >= 0 && $0.frame.maxX <= content.bounds.width+0.5 },"Assignment cards stay inside inspector")
                for card in content.subviews.compactMap({ $0 as? StudioSurface }) {
                    card.layoutSubtreeIfNeeded()
                    verifySmoke(card.subviews.allSatisfy { $0.frame.minX >= 0 && $0.frame.maxX <= card.bounds.width+0.5 },"Assignment controls stay inside cards")
                }
            }
        }
        let beforeClear = config
        selectedTrigger = .hold; libraryOpen = true
        keyboard.onBlank?()
        verifySmoke(selected.isEmpty && anchorKey == nil && selectedDeck == nil && selectedTrigger == .tap && !libraryOpen,"Blank keyboard clears all editor selection")
        verifySmoke(config == beforeClear,"Blank click never changes mappings")
        selectKey(11); center.onBlank?()
        verifySmoke(selected.isEmpty && keyboard.keys[11]?.selected == false,"Center blank clears key highlight")
        let holdMappings = [Mapping(layer:.space,input:0,action:.key,output:36,trigger:.hold)]
        keyboard.update(holdMappings,layer:.space,selected:[],configuration:config)
        verifySmoke(keyboard.keys[0]?.hasHoldAssignment == true && keyboard.keys[11]?.hasHoldAssignment == false,"Only HOLD-assigned inputs show a corner marker")
        keyboard.update([],layer:.space,selected:[],configuration:config)
        verifySmoke(keyboard.keys[0]?.hasHoldAssignment == false,"Deleted HOLD clears marker")
        let toggleProbe = StudioToggle(title:"検証",target:nil,action:nil)
        toggleProbe.state = .on
        verifySmoke(toggleProbe.toggle.state == .on,"Native switch reflects restored state")
        toggleProbe.toggle.state = .off
        _ = toggleProbe.toggle.sendAction(toggleProbe.toggle.action,to:toggleProbe.toggle.target)
        verifySmoke(toggleProbe.state == .off,"Native switch forwards edits to stored control state")
        print("PASS: blank selection reset without mutation, HOLD corner marker lifecycle and native switch state/action")
        selected.removeAll(); keyboard.update(config.mappings(for: profileID), layer: currentLayer, selected: selected, configuration: config); libraryOpen = false; renderInspector()
        let mouseTen = StudioAction.catalog.first { $0.id == "mouse-button-10" }!
        showLayer(.space)
        let mouseTenJSON = String(data:try! JSONEncoder().encode(mouseTen),encoding:.utf8)!
        verifySmoke(receiveDrop(mouseTenJSON,key:310,deckID:nil), "Space button 10 accepts button 10 output")
        verifySmoke(config.mappings(for:profileID).contains { $0.layer == .space && $0.input == 310 && $0.action == .mouse && $0.output == 10 }, "Extra mouse input and output round-trip")
        selectKey(310)
        verifySmoke(keyboard.showsMouseGrid && keyboard.mouseGrid[310]?.selected == true,"Extra input opens the same mouse grid")
        undo(); keyboard.showMouseGrid(false)
        let beforeDeck = config
        let cachedEditor = makeDeckEditor(), preservedDeckID = config.deck[0].id
        let preservedDeckButton = deckEditorButtons[preservedDeckID]
        let file = preferencesURL.deletingLastPathComponent().appendingPathComponent("deck-drop-test.txt")
        try! Data("Deck test".utf8).write(to:file)
        verifySmoke(receiveDeckFile(file,to:nil),"File drop creates independent Deck Action")
        let fileItem = config.deck.last!
        verifySmoke(makeDeckEditor() === cachedEditor && deckEditorButtons[preservedDeckID] === preservedDeckButton,"Adding one Deck item preserves editor and other tiles")
        verifySmoke(config.mapping(for:fileItem)?.application == file.path && config.mappings == beforeDeck.mappings,"File drop preserves exact path and key assignments")
        verifySmoke(editDeckItem(fileItem.id,edit:.rename("File")),"Overlay rename shares editor transaction")
        undo(); verifySmoke(config.deck.last?.title == fileItem.title,"Rename undone")
        undo(); verifySmoke(config == beforeDeck,"File drop undone without data loss")
        verifySmoke(makeDeckEditor() === cachedEditor && deckEditorButtons[preservedDeckID] === preservedDeckButton && deckEditorButtons[fileItem.id] == nil,"Removing one Deck item preserves unrelated tiles")
        var monitorConfig = config
        monitorConfig.deck.append(DeckItem(title:"時計",profileID:"default",layer:.normal,input:0,directAction:Mapping(layer:.normal,input:0,action:.disabled),monitor:.clock))
        verifySmoke(transact(monitorConfig),"Prepare monitor replacement")
        let monitorID = config.deck.last!.id
        verifySmoke(receiveDeckFile(file,to:monitorID) && config.deck.last?.monitor == nil,"File replacement removes monitor subscription")
        undo(); verifySmoke(config.deck.last?.monitor == .clock,"Undo restores monitor")
        verifySmoke(receiveDrop(json,key:0,deckID:monitorID) && config.deck.last?.monitor == nil,"Action replacement removes monitor subscription")
        undo(); undo(); verifySmoke(config == beforeDeck,"Monitor replacement history preserves fixture")
        var groupConfig = config
        groupConfig.deck = (0..<5).map { DeckItem(title:"Slot \($0)",profileID:"default",layer:.normal,input:0,directAction:Mapping(layer:.normal,input:0,action:.key,output:8)) }
        verifySmoke(transact(groupConfig),"Prepare group Deck")
        let group = [config.deck[0].id, config.deck[2].id]
        let groupEditor = makeDeckEditor()
        let multiSelect = groupEditor.subviews.compactMap { $0 as? StudioButton }
            .first { $0.identifier?.rawValue == "deck-multi-select" }!
        let firstTile = deckEditorButtons[group[0]]!
        let secondTile = deckEditorButtons[group[1]]!
        firstTile.performClick(nil)
        multiSelect.performClick(nil)
        secondTile.performClick(nil)
        verifySmoke(multiSelect.selected && selectedDecks == Set(group), "Deck multi-select button enables plain-click selection")
        secondTile.performClick(nil)
        verifySmoke(selectedDecks == [group[0]], "Clicking a selected tile again removes it from the group")
        secondTile.performClick(nil)
        verifySmoke(Set(secondTile.dragIDs?() ?? []) == Set(group) && Set(firstTile.dragIDs?() ?? []) == Set(group), "Dragging either selected tile carries the group")
        verifySmoke(secondTile.moveItems?(group, group[1], 5) == true, "Group drag uses shared transaction")
        let movedPositions = config.deckPositions("default")
        verifySmoke(movedPositions[group[0]] == 3 && movedPositions[group[1]] == 5 &&
                    movedPositions[groupConfig.deck[3].id] == 0 && movedPositions[groupConfig.deck[4].id] == 4,
                    "Group move retains gaps and relocates only displaced items")
        verifySmoke(selectedDecks == Set(group), "Moved group remains selected")
        multiSelect.performClick(nil)
        verifySmoke(!multiSelect.selected && selectedDecks.count == 1, "Turning off Deck multi-select restores single selection")
        var firstAnchor = groupConfig
        verifySmoke(firstAnchor.moveDeck(Set(group), layout:"default", anchoredAt:group[0], toSlot:3) &&
                    firstAnchor.deckPositions("default") == movedPositions, "Either grabbed member produces the same translation")
        var overflow = groupConfig
        verifySmoke(!overflow.moveDeck(Set(group), layout:"default", anchoredAt:group[0], toSlot:4) &&
                    overflow == groupConfig, "Group that crosses the column boundary stays in place")

        verifySmoke(editDeckItems(group,edit:.color("245A86")),"Overlay group color shares editor transaction")
        verifySmoke(config.deck.filter { group.contains($0.id) }.allSatisfy { $0.colorHex == "245A86" },"Color changes every selected item")
        undo(); verifySmoke(config.deck.allSatisfy { $0.colorHex == nil },"One undo restores group colors")
        undo(); verifySmoke(config == groupConfig,"One undo restores entire group")
        undo(); verifySmoke(config == beforeDeck,"Restore editor fixture")
        let copiedID = config.deck[0].id, pasteID = config.deck[1].id
        let copiedMapping = config.mapping(for:config.deck[0])
        copyDeckAssignments([copiedID])
        verifySmoke(receiveDrop(json,key:config.deck[0].input,deckID:copiedID),"Modify original after copying Deck assignment")
        let keysBeforePaste = config.mappings
        verifySmoke(pasteDeckAssignments(layout:selectedLayoutID,slot:config.deckPositions(selectedLayoutID)[pasteID]!),"Paste assignment into destination slot")
        verifySmoke(config.deck.first { $0.id == pasteID }?.directAction == copiedMapping && config.mappings == keysBeforePaste,"Deck clipboard is a snapshot; paste never edits keyboard source")
        undo(); undo(); verifySmoke(config == beforeDeck,"Assignment paste and source edit undo restore fixture")
        let named = StudioAction(id:"deck-layout-default",name:"Deck",category:"Deckパネル",symbol:"square.grid.3x3",kind:.deck,value:"default")
        verifySmoke(named.mapping(layer:.space,input:0).payload == "default","Action drag preserves named Deck identity")
        let monitorAction = StudioAction.monitorActions.first { $0.deckMonitor == .clock }!
        let monitorJSON = String(data:try! JSONEncoder().encode(monitorAction),encoding:.utf8)!
        verifySmoke(!receiveDrop(monitorJSON,key:0,deckID:nil),"Deck monitor cannot replace keyboard action")
        verifySmoke(receiveDrop(monitorJSON,key:config.deck[0].input,deckID:copiedID) && config.deck[0].monitor == .clock,"Monitor drag replaces Deck tile")
        undo(); verifySmoke(config == beforeDeck,"Monitor drag is one reversible transaction")
        let emptySlot = config.deckSlotCount(selectedLayoutID)-1
        verifySmoke(receiveDeckFile(file,layout:selectedLayoutID,slot:emptySlot),"File drop into exact empty cell")
        let placed = config.deck.last!.id
        verifySmoke(config.deckPositions(selectedLayoutID)[placed] == emptySlot,"Empty cell destination persists")
        verifySmoke(!receiveDeckFile(file,layout:selectedLayoutID,slot:emptySlot),"Occupied destination cannot overwrite through empty slot route")
        verifySmoke(!receiveDeckAction(json,layout:selectedLayoutID,slot:emptySlot),"Occupied Action drop reports rejection without a false successful drop")
        let fixedPositions = config.deckPositions(selectedLayoutID)
        verifySmoke(editDeckItem(placed,edit:.remove),"Remove sparse tile")
        verifySmoke(config.deckPositions(selectedLayoutID).allSatisfy { fixedPositions[$0.key] == $0.value },"Deletion preserves every surviving position")
        undo(); verifySmoke(config.deckPositions(selectedLayoutID)[placed] == emptySlot,"Undo restores deleted sparse cell")
        undo(); verifySmoke(config == beforeDeck,"Sparse placement undo restores legacy representation exactly")
        print("PASS: library drop, replacement, shared Deck action, undo/redo, protected input, atomic multi-selection")
    }
    func showDeckDrawerForPreview(_ enabled: Bool) {
        let original = config
        deckDrawerOpen = enabled; deckEditor?.needsLayout = true; view.layoutSubtreeIfNeeded()
        verifySmoke(config == original,"Opening Deck drawer never writes settings")
        guard enabled,let editor = deckEditor,let scroll = editor.subviews.first(where:{ $0 is NSScrollView }) as? NSScrollView else { return }
        verifySmoke(!deckDrawer.isHidden && !deckDrawer.frame.intersects(scroll.frame),"Deck drawer and preview do not overlap")
        if let grid = scroll.documentView {
            verifySmoke(abs(grid.frame.width-grid.bounds.width)<1 && abs(grid.frame.height-grid.bounds.height)<1,"Deck controls retain readable scale beside settings")
            verifySmoke(scroll.hasHorizontalScroller && scroll.hasVerticalScroller,"Large Deck remains reachable with scrolling")
        }
    }
    func showDeckListForPreview(_ enabled: Bool) {
        deckListMode = enabled; deckEditor = nil; renderCenter(); view.layoutSubtreeIfNeeded()
    }
    func verifyGeometry() {
        view.layoutSubtreeIfNeeded()
        verifySmoke(sidebar.frame.maxX < center.frame.minX && center.frame.maxX < inspector.frame.minX, "Three panes never overlap")
        verifySmoke(sidebar.frame.width < inspector.frame.width, "Sidebar stays narrower than inspector")
        let leftMargin = center.frame.minX - (view.separators[0].midX)
        let rightMargin = view.separators[1].midX - center.frame.maxX
        verifySmoke(abs(leftMargin-rightMargin) <= 1, "Content margins between pane dividers are symmetric")
        let visibleKeyFrames = keyboard.subviews.compactMap { $0 as? MacKeyboardKey }.filter { !$0.isHidden }.map { $0.frame }
        if let left = visibleKeyFrames.map({ $0.minX }).min(), let right = visibleKeyFrames.map({ $0.maxX }).max() {
            verifySmoke(abs(left-(keyboard.bounds.width-right)) < 1, "Keyboard remains centered even when height limits its scale")
        }
        let jis = keyboard.keyboardLayout == .jis
        let a = keyboard.keys[0]!, q = keyboard.keys[12]!, z = keyboard.keys[6]!
        verifySmoke(abs(a.frame.width - q.frame.width) < 0.5 && abs(q.frame.width - z.frame.width) < 0.5, "Letter rows use the same physical key pitch")
        verifySmoke(z.frame.minX > a.frame.minX && (jis || a.frame.minX > q.frame.minX), "Mac letter rows retain physical stagger")
        let modifier = keyboard.subviews.compactMap { $0 as? StudioButton }.first { $0.title == (jis ? "⌃ control" : "⇪ caps lock") }
        verifySmoke(modifier != nil && abs(modifier!.frame.minY - a.frame.minY) < 0.5 && modifier!.frame.maxX < a.frame.minX, "JIS Control / US Caps Lock is beside A")
        for key in keyboard.subviews.compactMap({ $0 as? MacKeyboardKey }) {
            verifySmoke(key.tileCornerRadius == a.tileCornerRadius, "Every keyboard key uses the same corner radius as A")
        }
        let enter = keyboard.keys[36]!
        if jis {
            let notch = NSPoint(x:enter.bounds.width * 0.08,y:enter.bounds.height * 0.75)
            verifySmoke(!enter.keyOutline(enter.bounds).contains(notch), "JIS Return lower-left notch stays empty")
            verifySmoke(enter.hitTest(keyboard.convert(notch,from:enter)) == nil, "JIS Return notch does not capture clicks")
            verifySmoke(enter.updateDrop(at:notch) == [], "JIS Return notch rejects Action drops")
            verifySmoke(enter.keyOutline(enter.bounds).contains(NSPoint(x:enter.bounds.midX,y:enter.bounds.midY)), "JIS Return stem remains interactive")
        }
        verifySmoke(abs(q.frame.width / q.frame.height - 1) < 0.08, "Mac letter keys retain near-square proportions")
        verifySmoke(jis ? enter.frame.minY == q.frame.minY && enter.frame.maxY > a.frame.minY : enter.frame.minY == a.frame.minY, "JIS tall Return and US horizontal Return occupy correct rows")
        verifySmoke(keyboard.keys[126]!.frame.minY > z.frame.maxY && keyboard.keys[126]!.frame.minY < keyboard.keys[125]!.frame.minY, "Arrow keys form an inverted T on the bottom row")
        for code in [UInt16(24),33,30,42,39] {
            verifySmoke(keyboard.keys[code]?.title == Keys.name(code,layout:keyboard.keyboardLayout), "Symbol key legends match assigned physical codes")
        }
        let layoutPreview = URL(fileURLWithPath:CommandLine.arguments[2]).deletingPathExtension().path + "-layout-" + keyboard.keyboardLayout.rawValue + ".png"
        saveSmokePreview(view,to:layoutPreview)
        let touchPreview = KeyboardCanvas(frame:NSRect(x:0,y:0,width:820,height:640))
        touchPreview.update([],layer:.trackpad,selected:[],configuration:config)
        touchPreview.layoutSubtreeIfNeeded()
        for code in ([270,271,272,273] + Array(400...441)).map(UInt16.init) {
            touchPreview.update([],layer:.trackpad,selected:[code],configuration:config)
            touchPreview.layoutSubtreeIfNeeded()
            verifySmoke(touchPreview.keys[code] != nil,"Trackpad target can be revealed by input code")
            verifySmoke(touchPreview.keys.values.allSatisfy { touchPreview.bounds.contains($0.frame) },"Trackpad targets remain inside the canvas")
            let targets = Array(touchPreview.keys.values)
            verifySmoke(targets.indices.allSatisfy { i in targets.indices.allSatisfy { j in i == j || targets[i].frame.intersection(targets[j].frame).isEmpty } },"Trackpad targets do not overlap")
        }
        saveSmokePreview(touchPreview,to:URL(fileURLWithPath:CommandLine.arguments[2]).deletingPathExtension().path + "-trackpad.png")
        for code: UInt16 in [414,270,400,405,410,426] {
            touchPreview.update([],layer:.trackpad,selected:[code],configuration:config)
            touchPreview.layoutSubtreeIfNeeded()
            saveSmokePreview(touchPreview,to:URL(fileURLWithPath:CommandLine.arguments[2]).deletingPathExtension().path + "-gesture-\(code).png")
        }

        let beforeTrackpad = config
        let beforeTrackpadNavigation = navigationHistory
        let originalSize = view.frame.size
        let oldAppearance = view.appearance
        showLayer(.trackpad)
        selectKey(432)
        let copy = StudioAction(id:"edge-copy-test",name:"コピー",category:"基本操作",symbol:"doc.on.doc",kind:.shortcut,output:8,modifiers:.command)
        let edgeJSON = String(decoding:try! JSONEncoder().encode(copy),as:UTF8.self)
        verifySmoke(receiveDrop(edgeJSON,key:432,deckID:nil),"Edge tile accepts the library drop")
        verifySmoke(config.mappings(for:profileID).contains { $0.layer == .trackpad && $0.input == 432 },"Edge assignment saves to the profile")
        let volume = StudioAction.catalog.first { $0.id == "system-volumeUp" }!
        search.stringValue = volume.name
        reloadActions()
        let volumeRow = actionRows.subviews.compactMap { $0 as? ActionRow }.first { $0.value.id == volume.id }
        verifySmoke(volumeRow?.onChoose != nil,"A selected edge can be assigned by clicking the library")
        volumeRow?.onChoose?()
        verifySmoke(config.mappings(for:profileID).contains { $0.layer == .trackpad && $0.input == 432 && $0.payload == SystemCommand.volumeUp.rawValue },"Click replaces the selected edge action")
        let widthBefore = config.edgeWidth
        keyboard.onTrackpadWidth?(0.17)
        verifySmoke(config.edgeWidth == 0.17,"Width slider commits settings")
        let restored = try! JSONDecoder().decode(Configuration.self,from:ConfigurationStore.encoded(config))
        verifySmoke(restored == config && widthBefore != restored.edgeWidth,"Edge action and width survive settings export and import")
        let previewBase = URL(fileURLWithPath:CommandLine.arguments[2]).deletingPathExtension().path
        for (name,appearance,size) in [("dark",NSAppearance.Name.darkAqua,originalSize),("light-compact",NSAppearance.Name.aqua,NSSize(width:1100,height:790))] {
            view.appearance = NSAppearance(named:appearance)
            view.frame.size = size
            view.needsLayout = true
            view.layoutSubtreeIfNeeded()
            verifySmoke(layoutPicker.isHidden && mouseScopePicker.isHidden && keyboard.keys.count == 8,"Edge view fits with gesture controls")
            verifySmoke(actionsScroll.frame.height > 80 && inspector.bounds.contains(actionsScroll.frame),"Action library fits above edge assignment controls")
            saveSmokePreview(view,to:previewBase + "-trackpad-edge-" + name + ".png")
        }
        inspector.subviews.compactMap { $0 as? StudioButton }.first { $0.identifier?.rawValue == "trackpad-remove" }?.performClick(nil)
        verifySmoke(!config.mappings(for:profileID).contains { $0.layer == .trackpad && $0.input == 432 },"Delete removes the selected edge assignment")
        undo()
        verifySmoke(config == restored,"Undo restores removed edge action")
        redo()
        verifySmoke(!config.mappings(for:profileID).contains { $0.layer == .trackpad && $0.input == 432 },"Redo removes edge action again")
        undo(); undo(); undo(); undo()
        verifySmoke(config == beforeTrackpad,"Undo returns to the original configuration")
        view.appearance = oldAppearance
        view.frame.size = originalSize
        showLayer(.space)
        navigationHistory = beforeTrackpadNavigation
        search.stringValue = ""
        view.needsLayout = true
        view.layoutSubtreeIfNeeded()
        print("PASS: edge editor drop, click replacement, width, export/import, deletion, undo/redo and compact light/dark previews")
        var mx4PreviewConfiguration = Configuration()
        mx4PreviewConfiguration.mouseButtonLayouts = [MouseButtonLayout(name:"MX Master 4",items:[MouseLayoutItem(source:340,x:252,y:324)])]
        mx4PreviewConfiguration.selectedMouseButtonLayout = 0
        let mx4Preview = KeyboardCanvas(frame:NSRect(x:0,y:0,width:820,height:640))
        mx4Preview.update([],layer:.normal,selected:[],configuration:mx4PreviewConfiguration)
        verifySmoke(mx4Preview.keys[340]?.title == "モード切替", "MX Master 4 mode-switch tile uses its name instead of its internal number")
        let layoutSpecific: Set<UInt16> = keyboard.keyboardLayout == .jis ? [50] : [93,94,102,104]
        verifySmoke(Set(keyboard.keys.keys) == Set(Keys.inputs.filter { $0.code < 300 }.map { $0.code }).subtracting(layoutSpecific), "Every layout key and mouse input has a visible destination")
        verifySmoke(keyboard.additionalMouse.numberOfItems == Keys.extraMouseInputs.count + 1, "Every additional physical mouse button has an editor destination")
        let expectedMouse = Set((0...31).compactMap { config.mouseInput(Int64($0)) } + [270,271,272,273])
        verifySmoke(Set(keyboard.mouseGrid.keys) == expectedMouse && expectedMouse.count == 36, "All 32 physical buttons and four wheel directions have unique visible destinations")
        let longAction = StudioAction(id:"hover-test",name:"ウィンドウを右のデスクトップへ移動",category:"ウィンドウ",symbol:"macwindow",kind:.window,value:"moveRight")
        let hoverRow = ActionRow(longAction,favorite:false,toggle:{})
        view.addSubview(hoverRow); hoverRow.verifyPassiveHelp(); hoverRow.removeFromSuperview()
        verifySmoke(hoverRow.accessibilityHelp()?.contains(longAction.name) == true && hoverRow.accessibilityHelp()?.contains(longAction.detail) == true,"Action hover retains complete title and detail")
        let navigationBaseline = navigationHistory
        navigationHistory = []; libraryOpen = false; keyboard.showMouseGrid(false); updateBackNavigation()
        verifySmoke(backNavigation.isHidden && !backNavigation.isEnabled,"Back is hidden without a destination")
        openLibrary()
        verifySmoke(!backNavigation.isHidden && backNavigation.isEnabled,"Opening Action library reveals Back")
        _ = goBack()
        verifySmoke(backNavigation.isHidden,"Closing last destination hides Back")
        showLayer(.space); showSection("settings"); showSection("gestures")
        verifySmoke(!backNavigation.isHidden,"Screen history reveals Back")
        verifySmoke(goBack() && section == "settings", "Back returns from gestures to settings")
        verifySmoke(goBack() && section == "keyboard" && currentLayer == .space, "Back restores prior layer")
        keyboard.showMouseGrid(true)
        verifySmoke(goBack() && !keyboard.showsMouseGrid, "Mouse grid back restores keyboard")
        showSection("deck"); selectDeckLayout("default"); deckDrawerOpen = true
        verifySmoke(goBack() && !deckDrawerOpen && !deckGallery,"Back closes Deck customization first")
        verifySmoke(goBack() && deckGallery && section == "deck","Back returns Deck detail to gallery")
        verifySmoke(goBack() && section == "keyboard","Back leaves gallery for previous screen")
        navigationHistory = navigationBaseline
        keyboard.showMouseGrid(true); view.layoutSubtreeIfNeeded()
        verifySmoke(keyboard.mouseGrid.values.allSatisfy { !$0.isHidden && $0.frame.width > 0 }, "Multi-button grid is visible")
        let mousePath = URL(fileURLWithPath:CommandLine.arguments[2]).deletingPathExtension().path + "-multibutton.png"
        saveSmokePreview(view,to:mousePath)
        keyboard.showMouseGrid(false)
        let keyViews = Array(keyboard.keys.values)
        for i in keyViews.indices {
            for j in keyViews.indices where j > i {
                let overlap = keyViews[i].frame.intersection(keyViews[j].frame)
                if !overlap.isEmpty && overlap.width >= 0.5 && overlap.height >= 0.5 {
                    for x in stride(from:overlap.minX+0.25,to:overlap.maxX,by:1) {
                        for y in stride(from:overlap.minY+0.25,to:overlap.maxY,by:1) {
                            let p = NSPoint(x:x,y:y)
                            let first = keyViews[i].keyOutline(keyViews[i].bounds).contains(keyViews[i].convert(p,from:keyboard))
                            let second = keyViews[j].keyOutline(keyViews[j].bounds).contains(keyViews[j].convert(p,from:keyboard))
                            verifySmoke(!(first && second), "Physical key shapes do not overlap")
                        }
                    }
                }
            }
        }
        for button in keyboard.keys.values {
            verifySmoke(keyboard.bounds.insetBy(dx: -0.5, dy: -0.5).contains(button.frame), "Supported key \(button.title) remains inside keyboard: \(button.frame) in \(keyboard.bounds)")
            verifySmoke(button.frame.width >= 25 && button.frame.height >= ((123...126).contains(keyboard.keys.first { $0.value === button }!.key) ? 12 : 20), "Keys remain usable at minimum width (Mac half-height arrows)")
        }
    }
    func verifyManagementGeometry() {
        for name in ["profiles","settings"] {
            showSection(name); view.layoutSubtreeIfNeeded()
            verifySmoke(inspector.isHidden && center.frame.width > 700,"Management screen uses available width")
            verifySmoke(nav.filter { $0.identifier?.rawValue.hasPrefix("nav-") == true && $0.selected }.count == 1,"Exactly one active management navigation item")
            func descendants(_ node:NSView) -> [NSView] { [node] + node.subviews.flatMap(descendants) }
            let controls = descendants(center)
            let scrolls = controls.compactMap { $0 as? NSScrollView }
            verifySmoke(!scrolls.isEmpty,"Management screen requires scrolling")
            for scroll in scrolls where !scroll.isHiddenOrHasHiddenAncestor {
                (scroll as? SettingsPane)?.verifyNavigation()
                guard let document = scroll.documentView else { continue }
                document.layoutSubtreeIfNeeded()
                verifySmoke(document.bounds.width <= scroll.contentView.bounds.width+1,"No horizontal clipping in management screen")
            }
            for button in controls.compactMap({ $0 as? NSButton }) where button.cell is StudioNativeActionCell {
                let width = (button.title as NSString).size(withAttributes:[.font:button.font ?? NSFont.systemFont(ofSize:13)]).width + 28
                verifySmoke(button.frame.height >= 39 && button.frame.width >= width-1,"Native command keeps Windows control height and caption padding: \(button.title)")
            }
            let path = URL(fileURLWithPath:CommandLine.arguments[2]).deletingPathExtension().path + "-compact-" + name + ".png"
            saveSmokePreview(view,to:path)
        }
        showLayer(.space)
    }
    func selectMappingFromList(_ mapping: Mapping) {
        guard section == "list" else { return }
        currentLayer = mapping.layer; selectedTrigger = mapping.trigger; selected = [mapping.input]; selectedDeck = nil
        showSection("keyboard")
    }
    func openLibrary() {
        libraryOpen = true; search.stringValue = ""; category.selectItem(withTitle: "すべて"); collections.selectedSegment = 2; renderInspector()
    }
    func openMouseLibrary() { libraryOpen = true; search.stringValue = ""; category.selectItem(withTitle:"すべて"); collections.selectedSegment = 0; renderInspector() }
    func showLibraryForPreview() { libraryOpen = true; category.selectItem(withTitle: "文字・編集"); collections.selectedSegment = 0; renderInspector() }
}


extension WorkspaceController {
    private var gestureActions: [StudioAction] {
        config.gestures.map { StudioAction(id:"gesture-" + $0.id,name:$0.name,category:"ジェスチャー",symbol:"cursorarrow.motionlines",kind:.gesture,value:$0.id) }
    }
    @objc private func gesturePointerChanged(_ sender: NSButton) {
        guard let index = config.gestures.firstIndex(where: { $0.id == sender.identifier?.rawValue }) else { return }
        var candidate = config; candidate.gestures[index].locksCursor = sender.state == .on
        _ = transact(candidate)
    }
    private func makeGestureEditor() -> NSView {
        let scroll = NSScrollView(); scroll.hasVerticalScroller = true; scroll.drawsBackground = false
        let body = NSView(frame:NSRect(x:0,y:0,width:max(400,center.bounds.width-18),height:max(center.bounds.height,CGFloat(max(1,config.gestures.count))*440+100))); scroll.documentView = body
        let add = button("ジェスチャーを追加") { [weak self] in
            guard let self = self else { return }; var candidate = self.config
            var definition = GestureDefinition(); definition.name = "ジェスチャー \(candidate.gestures.count+1)"
            candidate.gestures.append(definition); _ = self.transact(candidate)
        }
        add.fill = Studio.assigned
        add.frame = NSRect(x:24,y:body.frame.height-60,width:210,height:32); body.addSubview(add)
        let library = button("Actionを開く") { [weak self] in self?.openMouseLibrary() }
        library.fill = Studio.key
        library.frame = NSRect(x:250,y:body.frame.height-60,width:170,height:32); body.addSubview(library)
        for (index, definition) in config.gestures.enumerated() {
            let y = body.frame.height-110-CGFloat(index)*440
            let title = Studio.label(definition.name,18); title.frame = NSRect(x:24,y:y,width:350,height:28); body.addSubview(title)
            let options = button("名前・感度・カーソル固定…") { [weak self] in self?.editGesture(definition.id) }
            options.frame = NSRect(x:24,y:y-38,width:min(285,body.frame.width-140),height:32); body.addSubview(options)
            let remove = button("削除") { [weak self] in
                guard let self = self else { return }
                let references = self.config.mappings + self.config.profiles.flatMap { $0.mappings }
                guard !references.contains(where: { $0.action == .gesture && $0.payload == definition.id }) else { self.onStatus?("割り当てで使用中です。先にキー／ボタンの割り当てを解除してください。"); return }
                var candidate = self.config; candidate.gestures.removeAll { $0.id == definition.id }; _ = self.transact(candidate)
            }
            remove.frame = NSRect(x:body.frame.width-100,y:y-38,width:80,height:28); body.addSubview(remove)
            let pointer = StudioToggle(title:"ジェスチャー中はポインターを固定する",target:self,action:#selector(gesturePointerChanged(_:)))
            pointer.identifier = NSUserInterfaceItemIdentifier(definition.id); pointer.state = definition.locksCursor ? .on : .off
            pointer.frame = NSRect(x:24,y:y-72,width:body.frame.width-48,height:26); body.addSubview(pointer)
            let details = Studio.label("感度 \(definition.threshold) · カーソル\(definition.locksCursor ? "固定" : "移動") · 各方向へActionをドラッグ",11,true)
            details.frame = NSRect(x:24,y:y-94,width:body.frame.width-48,height:22); body.addSubview(details)
            let cellWidth = min(158,(body.frame.width-64)/3)
            for (direction,label,column,offset) in [("up","↑",1,153.0),("left","←",0,233.0),("center","中央",1,233.0),("right","→",2,233.0),("down","↓",1,313.0)] {
                let x = 24 + CGFloat(column)*(cellWidth+8)
                let name = definition.actions[direction]?.targetDescription(layout:config.keyboardLayout) ?? "未割り当て"
                let cell = StudioButton(label + " " + name) { [weak self] in self?.openMouseLibrary() }
                cell.fill = definition.actions[direction] == nil ? Studio.key : Studio.assigned
                cell.frame = NSRect(x:x,y:y-offset,width:cellWidth,height:44); cell.toolTip = label + " " + name
                cell.enableDrop { [weak self] json in
                    guard let self = self, let data = json.data(using:.utf8), let action = try? JSONDecoder().decode(StudioAction.self,from:data), action.kind != .gesture else { return false }
                    self.resolve(action) { [weak self] resolved in
                        guard let self = self, let i = self.config.gestures.firstIndex(where: { $0.id == definition.id }) else { return }
                        var candidate = self.config; candidate.gestures[i].actions[direction] = resolved.mapping(layer:.normal,input:0)
                        _ = self.transact(candidate)
                    }; return true
                }
                body.addSubview(cell)
                let clear = button("解除") { [weak self] in
                    guard let self = self, let i = self.config.gestures.firstIndex(where: { $0.id == definition.id }) else { return }
                    var candidate = self.config; candidate.gestures[i].actions.removeValue(forKey:direction); _ = self.transact(candidate)
                }
                clear.frame = NSRect(x:x,y:y-offset-36,width:cellWidth,height:32); clear.isEnabled = definition.actions[direction] != nil; body.addSubview(clear)
            }
            let hint = Studio.label("作成したジェスチャーはAction一覧の「すべて」からキー／ボタンのTAPへドラッグ",10,true)
            hint.frame = NSRect(x:24,y:y-388,width:body.frame.width-48,height:22); body.addSubview(hint)
        }
        scroll.contentView.scroll(to:NSPoint(x:0,y:max(0,body.frame.height-center.bounds.height)))
        return scroll
    }
    func verifyGestureEditor() {
        let original = config
        var candidate = config
        var gesture = GestureDefinition(); gesture.name = "テスト用ジェスチャー"
        gesture.actions["left"] = Mapping(layer:.normal,input:0,action:.system,payload:"navigateBack")
        candidate.gestures.append(gesture)
        verifySmoke(transact(candidate),"Gesture definition saves")
        let pointer = NSButton(); pointer.identifier = NSUserInterfaceItemIdentifier(gesture.id); pointer.state = .off
        gesturePointerChanged(pointer)
        verifySmoke(config.gestures.last?.locksCursor == false,"Visible pointer control saves per gesture")
        undo(); verifySmoke(config == candidate,"Pointer setting undo preserves gesture actions")
        showSection("gestures")
        view.layoutSubtreeIfNeeded()
        saveSmokePreview(view,to:URL(fileURLWithPath:CommandLine.arguments[2]).deletingPathExtension().path + "-gesture-editor.png")
        verifySmoke(center.subviews.first is NSScrollView,"Gesture editor visible")
        verifySmoke(gestureActions.contains { $0.value == gesture.id },"Gesture available in Action library")
        let action = gestureActions.first { $0.value == gesture.id }!
        showLayer(.space)
        let json = String(data:try! JSONEncoder().encode(action),encoding:.utf8)!
        verifySmoke(receiveDrop(json,key:2,deckID:nil),"Gesture Action drops onto key")
        verifySmoke(config.mappings.contains { $0.layer == .space && $0.input == 2 && $0.action == .gesture && $0.payload == gesture.id },"Gesture reference persists")
        undo(); verifySmoke(config == candidate,"Gesture assignment undo")
        verifySmoke(!receiveDrop(json,key:2,deckID:nil,trigger:.hold),"Gesture HOLD drop rejected")
        undo(); verifySmoke(config == original,"Gesture creation undo preserves profiles and Deck")
        redo(); verifySmoke(config.gestures == candidate.gestures,"Gesture redo restores actions")
        undo(); showLayer(.space)
    }
    private func editGesture(_ id: String) {
        guard let definition = config.gestures.first(where: { $0.id == id }), let window = view.window else { return }
        let alert = NSAlert(); alert.messageText = "ジェスチャー設定"; alert.addButton(withTitle:"保存"); alert.addButton(withTitle:"キャンセル")
        let body = NSView(frame:NSRect(x:0,y:0,width:360,height:115))
        let name = NSTextField(string:definition.name); name.frame = NSRect(x:0,y:80,width:360,height:26); body.addSubview(name)
        let label = Studio.label("感度（3〜100）",12); label.frame = NSRect(x:0,y:45,width:170,height:24); body.addSubview(label)
        let threshold = NSTextField(string:String(definition.threshold)); threshold.frame = NSRect(x:180,y:45,width:100,height:26); body.addSubview(threshold)
        let lock = StudioToggle(title:"ジェスチャー中はカーソルを固定",target:nil,action:nil); lock.state = definition.locksCursor ? .on : .off; lock.frame = NSRect(x:0,y:5,width:360,height:28); body.addSubview(lock)
        alert.accessoryView = body
        alert.beginSheetModal(for:window) { [weak self] response in
            guard response == .alertFirstButtonReturn, let self = self, let i = self.config.gestures.firstIndex(where: { $0.id == id }) else { return }
            var candidate = self.config; candidate.gestures[i].name = name.stringValue.trimmingCharacters(in:.whitespacesAndNewlines)
            candidate.gestures[i].threshold = Int(threshold.stringValue) ?? 0; candidate.gestures[i].locksCursor = lock.state == .on
            _ = self.transact(candidate)
        }
    }
}

extension WorkspaceController {
    private var profileActions: [StudioAction] {
        [("default","標準")] .map { StudioAction(id:"profile-" + $0.0,name:"プロファイル：" + $0.1,category:"プロファイル",symbol:"person.crop.rectangle",kind:.profile,value:$0.0) } +
        config.profiles.map { StudioAction(id:"profile-" + $0.id,name:"プロファイル：" + $0.name,category:"プロファイル",symbol:"person.crop.rectangle",kind:.profile,value:$0.id) }
    }
    private var macroActions: [StudioAction] {
        config.macros.map { StudioAction(id:"macro-" + $0.id,name:$0.name,category:"マクロ",symbol:"play.rectangle",kind:.macro,value:$0.id) }
    }
    func acceptMacroRecording(_ id: String, steps: [MacroStep]) {
        recordingMacroID = nil
        guard let index = config.macros.firstIndex(where: { $0.id == id }) else { return }
        var candidate = config; candidate.macros[index].steps += steps
        if !steps.isEmpty && !transact(candidate) { onStatus?("記録結果を保存できませんでした。設定のエラーを確認してください。"); return }
        renderCenter(); onStatus?("\(steps.count)手順を記録しました。再生前に内容を確認してください。")
    }
    private func makeMacroEditor() -> NSView {
        if !config.macros.contains(where: { $0.id == selectedMacroID }) { selectedMacroID = config.macros.first?.id }
        let macro = config.macros.first { $0.id == selectedMacroID }
        let panel = StudioSurface(), browser = MacroBrowserView()
        let heading = Studio.label("マクロ一覧",17)
        browser.update(config.macros.map { (id:$0.id,name:$0.name) },selectedID:selectedMacroID ?? "")
        browser.list.selected = { [weak self] id in
            guard let self = self else { return }; self.onStopRecording?()
            self.selectedMacroID = id; self.macroSelectedSteps.removeAll(); self.renderCenter(); self.renderInspector()
        }
        browser.list.rename = { [weak self] in if let id = self?.selectedMacroID { self?.editMacro(id) } }
        browser.list.duplicate = { [weak self] in
            guard let self = self,let macro = self.config.macros.first(where:{$0.id == self.selectedMacroID}) else { return }
            var candidate = self.config, copy = macro; copy.id = UUID().uuidString; copy.name += " のコピー"
            copy.steps = copy.steps.map { var step = $0; step.id = UUID().uuidString; return step }
            candidate.macros.append(copy); self.selectedMacroID = copy.id; _ = self.transact(candidate)
        }
        browser.list.remove = { [weak self] in if let id = self?.selectedMacroID { self?.confirmDeleteMacro(id) } }
        let title = Studio.label(macro?.name ?? "マクロを作成",22); title.lineBreakMode = .byTruncatingTail; title.toolTip = macro?.name
        let create = button("新規",symbol:"plus") { [weak self] in
            guard let self = self else { return }; var candidate = self.config
            var added = MacroDefinition(); added.name = "マクロ \(candidate.macros.count+1)"; candidate.macros.append(added)
            self.selectedMacroID = added.id; self.macroSelectedSteps.removeAll(); _ = self.transact(candidate)
        }
        create.setAccessibilityLabel("新しいマクロを作成")
        let more = NSPopUpButton(frame:.zero,pullsDown:true)
        more.addItems(withTitles:["管理", "名前・複製・削除…", "起動ショートカットを書き出す…", "アプリを左右に並べるマクロを作成…"])
        more.target = self; more.action = #selector(macroMoreCommand(_:)); more.isEnabled = macro != nil
        for view in [heading,browser,title,create,more] { panel.addSubview(view) }
        let empty = Studio.label("左下の「新規」からマクロを作成してください。",13,true)
        panel.addSubview(empty); empty.isHidden = macro != nil
        let play = button("再生",symbol:"play.fill") { [weak self] in if let id = macro?.id { self?.onPlayMacro?(id) } }
        let stop = button("停止",symbol:"stop.fill") { [weak self] in self?.onStopMacro?(); self?.onStopRecording?() }
        macroPlayButton = play; macroStopButton = stop
        let status = Studio.label(recordingMacroID == nil ? "\(macro?.steps.count ?? 0) 手順" : "● 記録中",12,true)
        recordingLabel = status; status.lineBreakMode = .byTruncatingTail
        refreshMacroProgress(); if recordingMacroID != nil { status.textColor = .systemRed }
        for view in [play,stop,status] { panel.addSubview(view) }
        var list:MacroStepsView?
        if let macro = macro {
            let steps = MacroStepsView(steps:macro.steps,layout:config.keyboardLayout,changed:{ [weak self] steps in
                guard let self = self,let index = self.config.macros.firstIndex(where:{$0.id == macro.id}) else { return }
                var candidate = self.config; candidate.macros[index].steps = steps; _ = self.transact(candidate)
            },edit:{ [weak self] in self?.editMacroStep(macro.id,stepID:$0) },dropAction:{ [weak self] json,before in
                self?.insertMacroAction(json,macroID:macro.id,before:before) ?? false
            })
            steps.onNotice = { [weak self] message in self?.onStatus?(message) }
            steps.selectIDs(macroSelectedSteps)
            steps.selectionChanged = { [weak self] ids in self?.macroSelectedSteps = ids; if self?.macroEditorMode == 2 { self?.renderInspector() } }
            panel.addSubview(steps); macroStepList = steps; list = steps
        } else { macroStepList = nil }
        refreshMacroProgress()
        let copy = button("",symbol:"doc.on.doc") { [weak list] in list?.copySelection() }
        let paste = button("",symbol:"doc.on.clipboard") { [weak list] in list?.pasteSelection() }
        let remove = button("",symbol:"trash") { [weak list] in list?.deleteSelection() }
        for (control,name) in [(copy,"選択した手順をコピー"),(paste,"手順を貼り付け"),(remove,"選択した手順を削除")] {
            control.toolTip = name; control.setAccessibilityLabel(name); control.isEnabled = macro != nil; panel.addSubview(control)
        }
        copy.isEnabled = !macroSelectedSteps.isEmpty; remove.isEnabled = !macroSelectedSteps.isEmpty
        copy.toolTip = copy.isEnabled ? "選択した手順をコピー" : "手順を選択するとコピーできます"
        remove.toolTip = remove.isEnabled ? "選択した手順を削除" : "手順を選択すると削除できます"
        paste.toolTip = "コピーしたマクロ手順を選択行の後ろへ貼り付け。未選択なら末尾へ追加"
        list?.selectionChanged = { [weak self,weak copy,weak remove] ids in
            self?.macroSelectedSteps = ids; copy?.isEnabled = !ids.isEmpty; remove?.isEnabled = !ids.isEmpty
            copy?.toolTip = ids.isEmpty ? "手順を選択するとコピーできます" : "選択した手順をコピー"
            remove?.toolTip = ids.isEmpty ? "手順を選択すると削除できます" : "選択した手順を削除"
            if self?.macroEditorMode == 2 { self?.renderInspector() }
        }
        let hint = Studio.label("手順のドラッグは移動 · Actionのドロップは追加",11,true); hint.lineBreakMode = .byTruncatingTail; hint.toolTip = "手順をドラッグして移動。挿入線が移動先です。Actionをドロップすると新しい手順を追加します。右クリックで編集・コピー・削除。"; panel.addSubview(hint)
        panel.arrange = { [weak panel] in
            guard let panel = panel else { return }; let w = panel.bounds.width,h = panel.bounds.height
            let sidebar:CGFloat = w < 700 ? 150 : 168, x = sidebar+26, content = w-x
            heading.frame = NSRect(x:0,y:2,width:sidebar,height:28)
            browser.frame = NSRect(x:0,y:42,width:sidebar,height:max(100,h-94))
            create.frame = NSRect(x:0,y:h-40,width:sidebar,height:40)
            title.frame = NSRect(x:x,y:0,width:max(0,content-82),height:32)
            more.frame = NSRect(x:w-76,y:0,width:76,height:32)
            status.frame = NSRect(x:x,y:46,width:max(0,content-140),height:26)
            for (i,control) in [copy,paste,remove].enumerated() { control.frame = NSRect(x:w-132+CGFloat(i)*44,y:40,width:40,height:40) }
            list?.frame = NSRect(x:x,y:88,width:content,height:max(100,h-176))
            empty.frame = NSRect(x:x,y:100,width:content,height:60)
            hint.frame = NSRect(x:x,y:h-80,width:content,height:28)
            play.frame = NSRect(x:x,y:h-40,width:96,height:40); stop.frame = NSRect(x:x+104,y:h-40,width:96,height:40)
            panel.separators = [NSRect(x:sidebar+13,y:0,width:1,height:h)]
        }
        return panel
    }
    @objc private func macroModeChanged(_ sender:NSSegmentedControl) { macroEditorMode = sender.selectedSegment; renderInspector() }
    private func addPointClick(_ point: CGPoint, to id: String) {
        guard point.x.isFinite, point.y.isFinite, let index = config.macros.firstIndex(where:{ $0.id == id }) else { return }
        var candidate = config
        candidate.macros[index].steps += [MacroStep(phase:.move,x:point.x,y:point.y,relative:false), MacroStep(delayMilliseconds:80,action:Mapping(layer:.normal,input:0,action:.mouse,output:0))]
        if transact(candidate) { onStatus?("クリック位置を追加しました"); renderCenter() }
    }
    private func captureMacroPoint(_ id: String) {
        guard macroPointWindows.isEmpty, let editor = view.window else { return }
        let alert = NSAlert(); alert.messageText = "クリック位置を指定"
        alert.informativeText = "Filmoraなど対象の画面を先に開いてください。開始すると設定画面が隠れます。登録する場所をクリックしてください。Escまたは右クリックでキャンセル。再生時もウィンドウの位置・サイズを合わせてください。"
        alert.addButton(withTitle:"位置を指定"); alert.addButton(withTitle:"キャンセル")
        alert.beginSheetModal(for:editor) { [weak self, weak editor] result in
            guard result == .alertFirstButtonReturn, let self = self, let editor = editor else { return }
            editor.orderOut(nil)
            for screen in NSScreen.screens {
                let overlay = MacroPointWindow(contentRect:screen.frame,styleMask:.borderless,backing:.buffered,defer:false)
                overlay.isReleasedWhenClosed = false; overlay.level = .screenSaver
                overlay.backgroundColor = NSColor.black.withAlphaComponent(0.08); overlay.isOpaque = false
                overlay.collectionBehavior = [.canJoinAllSpaces,.fullScreenAuxiliary]
                let surface = MacroPointSurface(frame:NSRect(origin:.zero,size:screen.frame.size))
                surface.picked = { [weak self, weak editor] point in
                    guard let self = self else { return }
                    let windows = self.macroPointWindows; self.macroPointWindows.removeAll()
                    windows.forEach { $0.close() }
                    editor?.makeKeyAndOrderFront(nil)
                    if let point = point { self.addPointClick(point,to:id) }
                }
                let hint = NSTextField(labelWithString:"クリックで位置を登録   ·   Escでキャンセル")
                hint.alignment = .center; hint.textColor = .white; hint.backgroundColor = .black; hint.drawsBackground = true
                hint.frame = NSRect(x:max(0,(screen.frame.width-400)/2),y:screen.frame.height-80,width:400,height:32)
                surface.addSubview(hint); overlay.contentView = surface
                self.macroPointWindows.append(overlay); overlay.makeKeyAndOrderFront(nil); overlay.makeFirstResponder(surface)
            }
        }
    }
    private func makeMacroTools() -> NSView {
        let panel = StudioSurface(frame:NSRect(x:0,y:0,width:276,height:max(700,inspector.bounds.height)))
        let mode = NSSegmentedControl(labels:["追加","記録","編集"],trackingMode:.selectOne,target:self,action:#selector(macroModeChanged(_:)))
        mode.selectedSegment = macroEditorMode; mode.selectedSegmentBezelColor = Studio.accent
        mode.frame = NSRect(x:14,y:0,width:248,height:36); panel.addSubview(mode)
        panel.arrange = { [weak panel] in
            guard let panel = panel else { return }
            for control in panel.subviews {
                control.frame.size.width = max(0,panel.bounds.width-28)
            }
        }
        guard let macro = config.macros.first(where:{$0.id == selectedMacroID}) else { return panel }
        var y:CGFloat = 56
        func place(_ view:NSView,_ height:CGFloat = 40) { (view as? StudioButton)?.leftAligned = true; view.frame = NSRect(x:14,y:y,width:248,height:height); panel.addSubview(view); y += height+12 }
        if macroEditorMode == 0 {
            place(Studio.label("手順を追加",17))
            for (name,kind,symbol) in [("カスタムショートカット",ActionType.shortcut,"keyboard"),("アプリを追加",.launch,"app"),("ファイルを開く",.file,"doc"),("URLを開く",.url,"link")] {
                let control = button(name,symbol:symbol) { [weak self] in
                    guard let self = self,let action = StudioAction.catalog.first(where:{$0.kind == kind && $0.parameterized}) else { return }
                    self.resolve(action) { [weak self] in self?.appendMacroStep(MacroStep(action:$0.mapping(layer:.normal,input:0)),to:macro.id) }
                }; place(control)
            }
            place(button("定番アクション",symbol:"star") { [weak self] in self?.openMouseLibrary() })
            let clickRow = StudioSurface(frame:NSRect(x:0,y:0,width:248,height:40))
            for (index,label) in ["左クリック","右クリック","中クリック"].enumerated() {
                let click = button(label) { [weak self] in
                    self?.appendMacroStep(MacroStep(action:Mapping(layer:.normal,input:0,action:.mouse,output:UInt16(index))),to:macro.id)
                }
                click.font = .systemFont(ofSize:11)
                click.toolTip = "現在のポインター位置で" + label + "を1回実行。押す・離すをまとめて追加します。"
                click.frame = NSRect(x:CGFloat(index)*84,y:0,width:80,height:40); clickRow.addSubview(click)
            }
            clickRow.arrange = { [weak clickRow] in
                guard let row = clickRow else { return }; let width = max(0,(row.bounds.width-12)/3)
                for (index,child) in row.subviews.enumerated() { child.frame = NSRect(x:CGFloat(index)*(width+6),y:0,width:width,height:40) }
            }
            place(clickRow)
            place(button("クリック位置を指定",symbol:"scope") { [weak self] in self?.captureMacroPoint(macro.id) })
            place(button("マウス移動を追加",symbol:"cursorarrow.motionlines") { [weak self] in self?.appendMacroStep(MacroStep(phase:.move),to:macro.id) })
            place(Studio.label("文字列",13))
            let text = MacroTextField(string:""); text.isEditable = true; text.isSelectable = true; text.isBordered = true; text.drawsBackground = true
            text.placeholderString = "入力する文字列"; text.cell?.wraps = true; text.cell?.isScrollable = false; text.maximumNumberOfLines = 0; place(text,72)
            let addText = button("文字列を追加",symbol:"textformat") { [weak self,weak text] in
                guard let text = text?.stringValue,!text.isEmpty else { return }
                self?.appendMacroStep(MacroStep(action:Mapping(layer:.normal,input:0,action:.text,payload:text)),to:macro.id)
            }
            addText.isEnabled = false
            text.changed = { [weak text,weak addText] in addText?.isEnabled = !(text?.stringValue.isEmpty ?? true) }; place(addText)
            place(Studio.label("実行前の待機時間（ms）",13))
            let delay = NSTextField(string:"500"); place(delay,32)
            place(button("待機を追加",symbol:"clock") { [weak self,weak delay] in
                guard let ms = Int(delay?.stringValue ?? ""),ms >= 0 else { self?.onStatus?("待機時間には0以上の整数を入力してください。"); return }
                self?.appendMacroStep(MacroStep(delayMilliseconds:ms),to:macro.id)
            })
        } else if macroEditorMode == 1 {
            place(Studio.label(recordingMacroID == nil ? "自動記録" : "● 記録中",17))
            let mapped = StudioToggle(title:"割り当て後のActionを記録",target:nil,action:nil); mapped.state = .on; place(mapped)
            let movement = StudioToggle(title:"カーソルの移動軌跡も記録",target:nil,action:nil); place(movement)
            let relative = StudioToggle(title:"開始位置からの相対移動",target:nil,action:nil); relative.state = .on; place(relative)
            place(button(recordingMacroID == nil ? "記録を開始" : "記録を終了",symbol:"record.circle") { [weak self] in
                guard let self = self else { return }
                if self.recordingMacroID != nil { self.onStopRecording?() }
                else if self.onRecordMacro?(macro.id,movement.state == .on,relative.state == .on,mapped.state == .on) == true {
                    self.recordingMacroID = macro.id; self.renderCenter(); self.renderInspector()
                }
            })
            let note = NSTextField(wrappingLabelWithString:"Control＋Shift＋F12でも記録を終了できます。記録後は手順を確認してから再生してください。")
            note.textColor = Studio.muted; place(note,76)
        } else {
            let selected = macro.steps.filter { macroSelectedSteps.contains($0.id) }
            place(Studio.label("手順を編集（\(selected.count)件）",17))
            let edit = button("選択した手順の詳細…",symbol:"pencil") { [weak self] in if let step = selected.first { self?.editMacroStep(macro.id,stepID:step.id) } }
            edit.isEnabled = selected.count == 1; place(edit)
            place(Studio.label("実行前の待機時間（ms）",13))
            let delay = NSTextField(string:String(selected.first?.delayMilliseconds ?? 0)); place(delay,32)
            for (title,all) in [("選択した手順へ反映",false),("全手順へ反映",true)] {
                let apply = button(title) { [weak self,weak delay] in
                    guard let self = self,let ms = Int(delay?.stringValue ?? ""),ms >= 0,let index = self.config.macros.firstIndex(where:{$0.id == macro.id}) else { return }
                    var candidate = self.config
                    for i in candidate.macros[index].steps.indices where all || self.macroSelectedSteps.contains(candidate.macros[index].steps[i].id) { candidate.macros[index].steps[i].delayMilliseconds = ms }
                    _ = self.transact(candidate)
                }; apply.isEnabled = all ? !macro.steps.isEmpty : !selected.isEmpty; place(apply)
            }
            let copy = button("コピー",symbol:"doc.on.doc") { [weak self] in self?.macroStepList?.copySelection() }; copy.isEnabled = !selected.isEmpty; place(copy)
            place(button("貼り付け",symbol:"doc.on.clipboard") { [weak self] in self?.macroStepList?.pasteSelection() })
            let remove = button("選択した手順を削除",symbol:"trash") { [weak self] in self?.macroStepList?.deleteSelection() }; remove.isEnabled = !selected.isEmpty; place(remove)
        }
        let scroll = InspectorFormScrollView(frame:inspector.bounds); scroll.autoresizingMask = [.width,.height]; scroll.hasVerticalScroller = true; scroll.drawsBackground = false
        panel.frame.size.height = max(y+12,inspector.bounds.height); scroll.documentView = panel; scroll.tile(); panel.layoutSubtreeIfNeeded()
        return scroll
    }
    private func confirmDeleteMacro(_ id:String) {
        guard let macro = config.macros.first(where:{$0.id == id}),let window = view.window else { return }
        let alert = NSAlert(); alert.messageText = "「\(macro.name)」を削除しますか？"
        alert.informativeText = "削除後はツールバーの「元に戻す」で復元できます。"
        alert.addButton(withTitle:"削除"); alert.addButton(withTitle:"キャンセル")
        alert.beginSheetModal(for:window) { [weak self] response in
            guard response == .alertFirstButtonReturn,let self = self else { return }
            self.onStopRecording?(); var candidate = self.config; candidate.macros.removeAll { $0.id == id }; _ = self.transact(candidate)
        }
    }
    @objc private func macroMoreCommand(_ sender: NSPopUpButton) {
        guard let macro = config.macros.first(where:{ $0.id == selectedMacroID }) else { return }
        switch sender.indexOfSelectedItem {
        case 1: editMacro(macro.id)
        case 2: exportMacroShortcut(macro)
        case 3: createSplitAppMacro()
        default: break
        }
    }
    func updateRecordingCount(_ count: Int) {
        guard recordingMacroID != nil else { return }
        recordingLabel?.stringValue = "● 記録中 · \(count)手順 · Control＋Shift＋F12で停止"
    }
    private func insertMacroAction(_ json: String, macroID: String, before: String?) -> Bool {
        guard let data = json.data(using:.utf8), let action = try? JSONDecoder().decode(StudioAction.self,from:data), action.kind != .gesture else { return false }
        resolve(action) { [weak self] action in
            guard let self = self, let m = self.config.macros.firstIndex(where: { $0.id == macroID }) else { return }
            var candidate = self.config
            let position = before.flatMap { id in candidate.macros[m].steps.firstIndex(where: { $0.id == id }) } ?? candidate.macros[m].steps.count
            candidate.macros[m].steps.insert(MacroStep(action:action.mapping(layer:.normal,input:0)),at:position)
            _ = self.transact(candidate)
        }; return true
    }
    private func exportMacroShortcut(_ macro: MacroDefinition) {
        guard let window = view.window, !macro.steps.isEmpty else { onStatus?("先にマクロの手順を追加してください。"); return }
        let panel = NSSavePanel(); panel.allowedContentTypes = [UTType(filenameExtension:"webloc") ?? .data]
        panel.nameFieldStringValue = macro.name + ".webloc"
        panel.directoryURL = FileManager.default.urls(for:.desktopDirectory,in:.userDomainMask).first
        panel.beginSheetModal(for:window) { [weak self] response in
            guard response == .OK, let url = panel.url else { return }
            do {
                let data = try PropertyListSerialization.data(fromPropertyList:["URL":"relyr-macro://run/" + macro.id],format:.xml,options:0)
                try data.write(to:url,options:.atomic); self?.onStatus?("マクロの起動ショートカットを保存しました。")
            } catch { self?.onStatus?(error.localizedDescription) }
        }
    }
    private func createSplitAppMacro() {
        resolve(StudioAction(id:"split-app",name:"左右に並べるアプリを選択",category:"マクロ",symbol:"app",kind:.launch,parameterized:true)) { [weak self] action in
            guard let self = self else { return }
            var macro = MacroDefinition(); macro.name = action.name + "を左右に並べる"
            macro.steps = [MacroStep(action:action.mapping(layer:.normal,input:0)),
                MacroStep(delayMilliseconds:1800,action:Mapping(layer:.normal,input:0,action:.window,payload:WindowCommand.snapLeft.rawValue)),
                MacroStep(delayMilliseconds:200,action:Mapping(layer:.normal,input:0,action:.shortcut,output:45,modifiers:.command)),
                MacroStep(delayMilliseconds:800,action:Mapping(layer:.normal,input:0,action:.window,payload:WindowCommand.snapRight.rawValue))]
            var candidate = self.config; candidate.macros.append(macro); self.selectedMacroID = macro.id; _ = self.transact(candidate)
        }
    }
    @objc private func macroSelectionChanged(_ sender: NSPopUpButton) {
        onStopRecording?(); selectedMacroID = sender.selectedItem?.representedObject as? String; renderCenter()
    }
    private func appendMacroStep(_ step: MacroStep, to id: String) {
        guard let index = config.macros.firstIndex(where: { $0.id == id }) else { return }
        var candidate = config; candidate.macros[index].steps.append(step); _ = transact(candidate)
    }
    @discardableResult private func receiveMacroDrop(_ json: String, id: String, stepID: String?) -> Bool {
        guard let data = json.data(using:.utf8), let action = try? JSONDecoder().decode(StudioAction.self,from:data), action.kind != .gesture else { return false }
        resolve(action) { [weak self] resolved in
            guard let self = self, let index = self.config.macros.firstIndex(where: { $0.id == id }) else { return }
            var candidate = self.config; let mapping = resolved.mapping(layer:.normal,input:0)
            if let stepID = stepID, let step = candidate.macros[index].steps.firstIndex(where: { $0.id == stepID }) {
                candidate.macros[index].steps[step].action = mapping; candidate.macros[index].steps[step].phase = .pulse
            } else { candidate.macros[index].steps.append(MacroStep(action:mapping)) }
            _ = self.transact(candidate)
        }; return true
    }
    private func editMacro(_ id: String) {
        onStopRecording?()
        guard let index = config.macros.firstIndex(where: { $0.id == id }), let window = view.window else { return }
        let alert = NSAlert(); alert.messageText = "マクロ設定"
        for title in ["保存","キャンセル","複製","削除"] { alert.addButton(withTitle:title) }
        let name = NSTextField(string:config.macros[index].name); name.frame = NSRect(x:0,y:0,width:350,height:28); alert.accessoryView = name
        alert.beginSheetModal(for:window) { [weak self] response in
            guard let self = self, let i = self.config.macros.firstIndex(where: { $0.id == id }) else { return }
            var candidate = self.config
            if response == .alertFirstButtonReturn { candidate.macros[i].name = name.stringValue.trimmingCharacters(in:.whitespacesAndNewlines) }
            else if response.rawValue == 1002 {
                var copy = candidate.macros[i]; copy.id = UUID().uuidString; copy.name += " のコピー"; candidate.macros.append(copy); self.selectedMacroID = copy.id
            } else if response.rawValue == 1003 { candidate.macros.remove(at:i) }
            else { return }
            _ = self.transact(candidate)
        }
    }
    private func editMacroStep(_ id: String, stepID: String) {
        guard let macro = config.macros.first(where: { $0.id == id }), let step = macro.steps.first(where: { $0.id == stepID }), let window = view.window else { return }
        let alert = NSAlert(); alert.messageText = "手順を編集"; alert.addButton(withTitle:"保存"); alert.addButton(withTitle:"キャンセル")
        let body = NSView(frame:NSRect(x:0,y:0,width:380,height:160))
        let label = Studio.label("実行前の待ち時間（ミリ秒）",12); label.frame = NSRect(x:0,y:125,width:260,height:24); body.addSubview(label)
        let delay = NSTextField(string:String(step.delayMilliseconds)); delay.frame = NSRect(x:265,y:125,width:100,height:26); body.addSubview(delay)
        let phase = NSPopUpButton(frame:NSRect(x:0,y:85,width:220,height:28)); phase.addItems(withTitles:["1回実行","押す","離す","マウス移動"]); phase.selectItem(at:MacroPhase.allCases.firstIndex(of:step.phase)!); body.addSubview(phase)
        let x = NSTextField(string:String(step.x)), y = NSTextField(string:String(step.y))
        x.placeholderString = "X"; y.placeholderString = "Y"; x.frame = NSRect(x:0,y:45,width:170,height:26); y.frame = NSRect(x:190,y:45,width:170,height:26); body.addSubview(x); body.addSubview(y)
        let relative = StudioToggle(title:"相対移動（OFFなら画面座標）",target:nil,action:nil); relative.state = step.relative ? .on : .off; relative.frame = NSRect(x:0,y:5,width:370,height:28); body.addSubview(relative)
        alert.accessoryView = body
        alert.beginSheetModal(for:window) { [weak self] response in
            guard response == .alertFirstButtonReturn, let self = self, let m = self.config.macros.firstIndex(where: { $0.id == id }), let i = self.config.macros[m].steps.firstIndex(where: { $0.id == stepID }) else { return }
            var candidate = self.config; var updated = candidate.macros[m].steps[i]
            updated.delayMilliseconds = Int(delay.stringValue) ?? -1; updated.phase = MacroPhase.allCases[phase.indexOfSelectedItem]
            updated.x = Double(x.stringValue) ?? .nan; updated.y = Double(y.stringValue) ?? .nan; updated.relative = relative.state == .on
            if updated.phase == .move { updated.action = nil }
            candidate.macros[m].steps[i] = updated; _ = self.transact(candidate)
        }
    }
    private func recordMacro(_ id: String) {
        guard let window = view.window else { return }
        let alert = NSAlert(); alert.messageText = "操作をマクロに記録"
        alert.informativeText = "記録開始後、他のアプリで行ったキー・クリック・ホイールを記録します。Control＋Shift＋F12で終了します。"
        alert.addButton(withTitle:"記録開始"); alert.addButton(withTitle:"キャンセル")
        let body = NSView(frame:NSRect(x:0,y:0,width:380,height:100))
        let mapped = StudioToggle(title:"物理入力ではなく割り当て後のActionを記録",target:nil,action:nil); mapped.state = .on; mapped.frame = NSRect(x:0,y:70,width:380,height:26); body.addSubview(mapped)
        let movement = StudioToggle(title:"マウスの移動も記録する",target:nil,action:nil); movement.frame = NSRect(x:0,y:35,width:370,height:26); body.addSubview(movement)
        let relative = StudioToggle(title:"相対移動で記録する",target:nil,action:nil); relative.state = .on; relative.frame = NSRect(x:0,y:0,width:370,height:26); body.addSubview(relative); alert.accessoryView = body
        alert.beginSheetModal(for:window) { [weak self] response in
            guard response == .alertFirstButtonReturn, let self = self else { return }
            if self.onRecordMacro?(id,movement.state == .on,relative.state == .on,mapped.state == .on) == true { self.recordingMacroID = id; self.renderCenter() }
        }
    }
}


extension WorkspaceController {
    func verifyMacroEditor() {
        let original = config
        var candidate = config; var macro = MacroDefinition(); macro.name = "検証用マクロ"
        macro.steps = [MacroStep(delayMilliseconds:100,action:Mapping(layer:.normal,input:0,action:.shortcut,output:8,modifiers:.command)),MacroStep(delayMilliseconds:250),MacroStep(action:Mapping(layer:.normal,input:0,action:.shortcut,output:9,modifiers:.command))]
        candidate.macros.append(macro); selectedMacroID = macro.id
        verifySmoke(transact(candidate),"Macro saves")
        showSection("macros"); view.layoutSubtreeIfNeeded()
        verifySmoke(macroStepList?.window != nil,"Macro step list is visible")
        if let panel = center.subviews.first {
            let buttons = panel.subviews.compactMap { $0 as? NSButton }
            verifySmoke(buttons.allSatisfy { panel.bounds.contains($0.frame) },"Macro toolbar buttons stay inside editor")
            for (i,button) in buttons.enumerated() {
                verifySmoke(buttons.dropFirst(i+1).allSatisfy { !button.frame.intersects($0.frame) },"Macro toolbar controls never overlap")
            }
        }
        if let panel = center.subviews.first as? StudioSurface {
            let size = panel.frame.size
            for width: CGFloat in [450,560,820] {
                panel.setFrameSize(NSSize(width:width,height:620)); panel.needsLayout = true; panel.layoutSubtreeIfNeeded()
                let controls = panel.subviews.filter { !$0.isHidden }
                verifySmoke(controls.allSatisfy { panel.bounds.contains($0.frame) },"Narrow macro editor contains every visible control")
            }
            panel.setFrameSize(size); panel.needsLayout = true; panel.layoutSubtreeIfNeeded()
        }
        let selectionBeforeProgress = macroStepList?.selectedStepIDs
        updateMacroProgress(.init(id:macro.id,message:"手順 2 / 3 · 待機",running:true,failed:false,stepID:macro.steps[1].id))
        verifySmoke(macroPlayButton?.isEnabled == false && macroStopButton?.isEnabled == true,"Playback prevents duplicate starts")
        verifySmoke(macroStepList?.selectedStepIDs == selectionBeforeProgress,"Progress retains edit selection")
        updateMacroProgress(.init(id:macro.id,message:"停止：検証用",running:false,failed:true,stepID:macro.steps[1].id))
        verifySmoke(recordingLabel?.stringValue == "停止：検証用" && macroPlayButton?.isEnabled == true,"Failure reason remains beside steps and permits retry")
        macroProgress = nil; refreshMacroProgress()
        saveSmokePreview(view,to:URL(fileURLWithPath:CommandLine.arguments[2]).deletingPathExtension().path + "-macro-editor.png")
        macroSelectedSteps = [macro.steps[0].id]
        for mode in 0...2 {
            macroEditorMode = mode; showSection("macros"); view.layoutSubtreeIfNeeded()
            if let form = inspector.subviews.first as? InspectorFormScrollView, let body = form.documentView {
                let originalSize = form.frame.size
                for style in [NSScroller.Style.legacy, .overlay] {
                    form.scrollerStyle = style
                    for width:CGFloat in [220,260,320] {
                        form.setFrameSize(NSSize(width:width,height:400)); form.tile(); body.layoutSubtreeIfNeeded()
                        verifySmoke(body.frame.width <= form.contentSize.width,"Macro form fits visible viewport")
                        verifySmoke(body.subviews.allSatisfy { $0.frame.minX >= 14 && $0.frame.maxX <= body.bounds.width-14+0.5 },"All macro fields and controls retain equal margins")
                    }
                }
                form.setFrameSize(originalSize); form.tile(); body.layoutSubtreeIfNeeded()
            }
            saveSmokePreview(view,to:URL(fileURLWithPath:CommandLine.arguments[2]).deletingPathExtension().path + "-macro-mode-\(mode).png")
        }
        macroEditorMode = 0
        macroSelectedSteps.removeAll()
        showSection("macros")
        let action = StudioAction.catalog.first { $0.id == "cmd-8" }!
        let json = String(data:try! JSONEncoder().encode(action),encoding:.utf8)!
        verifySmoke(receiveMacroDrop(json,id:macro.id,stepID:nil),"Action drops into macro")
        verifySmoke(config.macros.last?.steps.count == 4,"Macro append persists")
        undo(); verifySmoke(config == candidate,"Macro step undo")
        let launch = macroActions.first { $0.value == macro.id }!
        let launchJSON = String(data:try! JSONEncoder().encode(launch),encoding:.utf8)!
        showLayer(.space); selected.removeAll()
        verifySmoke(receiveDrop(launchJSON,key:2,deckID:nil),"Macro assigns to layer")
        verifySmoke(config.mappings.contains { $0.layer == .space && $0.input == 2 && $0.action == .macro },"Macro mapping persists")
        undo(); verifySmoke(config == candidate,"Macro mapping undo")
        verifySmoke(receiveDrop(launchJSON,key:nil,deckID:nil),"Macro assigns to Deck")
        verifySmoke(config.deck.last?.directAction?.action == .macro,"Deck macro persisted")
        undo(); undo(); verifySmoke(config == original,"Macro editor keeps original profile and Deck data")
        redo(); verifySmoke(config == candidate,"Macro creation redo"); undo()
        MacroStepsView.verifyEditing()
        print("PASS: macro editor, Action drops, layer/Deck assignment, persistence and undo/redo")
    }
}
