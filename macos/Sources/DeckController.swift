import AppKit

private final class DeckPanel: NSPanel {
    var resizeCorner: ((NSEvent) -> Bool)?
    override func sendEvent(_ event: NSEvent) {
        if event.type == .leftMouseDown, resizeCorner?(event) == true { return }
        super.sendEvent(event)
    }
    override var canBecomeKey: Bool { false }
    override var canBecomeMain: Bool { false }
}

/// A visible, first-click resize target for the nonactivating panel.
private final class DeckResizeGrip: NSView {
    let left: Bool
    let bottom: Bool
    var begin: (() -> Void)?
    var resize: ((NSSize) -> NSSize)?
    var end: (() -> Void)?
    init(left: Bool, bottom: Bool) {
        self.left = left; self.bottom = bottom
        super.init(frame:.zero)
        setAccessibilityLabel("Deckのサイズを変更")
        toolTip = "ドラッグしてDeckのサイズを変更"
    }
    required init?(coder: NSCoder) { fatalError("init(coder:) has not been implemented") }
    override var mouseDownCanMoveWindow: Bool { false }
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }
    override func resetCursorRects() {
        if #available(macOS 15.0, *) {
            let position: NSCursor.FrameResizePosition = bottom ? (left ? .bottomLeft : .bottomRight) : (left ? .topLeft : .topRight)
            addCursorRect(bounds,cursor:.frameResize(position:position,directions:.all))
        } else { addCursorRect(bounds,cursor:.crosshair) }
    }
    override func draw(_ dirtyRect: NSRect) {
        guard bottom && !left else { return }
        Studio.muted.withAlphaComponent(0.65).setStroke()
        for offset in [CGFloat(3),CGFloat(7)] {
            let line = NSBezierPath()
            line.move(to:NSPoint(x:bounds.width-offset-5,y:2))
            line.line(to:NSPoint(x:bounds.width-2,y:offset+5))
            line.lineWidth = 1; line.stroke()
        }
    }
    static func frame(start: NSRect, size: NSSize, left: Bool, bottom: Bool) -> NSRect {
        NSRect(x:left ? start.maxX-size.width : start.minX,
               y:bottom ? start.maxY-size.height : start.minY,
               width:size.width,height:size.height)
    }
    override func mouseDown(with event: NSEvent) {
        guard let window = window else { return }
        let start = window.frame
        let origin = window.convertPoint(toScreen:event.locationInWindow)
        begin?()
        defer { end?() }
        while let next = window.nextEvent(matching:[.leftMouseDragged,.leftMouseUp]) {
            if next.type == .leftMouseUp { break }
            let point = window.convertPoint(toScreen:next.locationInWindow)
            let proposed = NSSize(width:start.width+(point.x-origin.x)*(left ? -1 : 1),
                                  height:start.height+(point.y-origin.y)*(bottom ? -1 : 1))
            var size = resize?(proposed) ?? proposed
            if let area = window.screen?.visibleFrame {
                let maximumWidth = left ? start.maxX-area.minX : area.maxX-start.minX
                let maximumHeight = bottom ? start.maxY-area.minY : area.maxY-start.minY
                let scale = min(1,min(max(1,maximumWidth)/max(1,size.width),max(1,maximumHeight)/max(1,size.height)))
                size.width *= scale; size.height *= scale
            }
            window.setFrame(Self.frame(start:start,size:size,left:left,bottom:bottom),display:true)
        }
    }
}

private final class DeckTitleButton: NSButton {
    var hoverChanged: ((DeckTitleButton, Bool) -> Void)?
    var hint: String?
    private var tracking: NSTrackingArea?

    override func updateTrackingAreas() {
        super.updateTrackingAreas()
        if let tracking { removeTrackingArea(tracking) }
        let area = NSTrackingArea(rect: .zero, options: [.mouseEnteredAndExited, .activeAlways, .inVisibleRect], owner: self)
        addTrackingArea(area)
        tracking = area
    }

    override func mouseEntered(with event: NSEvent) { hoverChanged?(self, true) }
    override func mouseExited(with event: NSEvent) { hoverChanged?(self, false) }
    override func mouseDown(with event: NSEvent) {
        hoverChanged?(self, false)
        super.mouseDown(with: event)
    }
}

private final class DeckTitleHint: NSTextField {
    override func hitTest(_ point: NSPoint) -> NSView? { nil }
}

private final class DeckHoverView: NSView {
    // The window paints one opaque surface through the transparent title bar.
    // A separate behind-window material here leaves the title bar unpainted.

    var entered: (() -> Void)?
    var exited: (() -> Void)?
    private var tracking: NSTrackingArea?
    override func updateTrackingAreas() {
        super.updateTrackingAreas(); if let tracking = tracking { removeTrackingArea(tracking) }
        let area = NSTrackingArea(rect:.zero,options:[.mouseEnteredAndExited,.activeAlways,.inVisibleRect],owner:self,userInfo:nil)
        addTrackingArea(area); tracking = area
    }
    override func mouseEntered(with event: NSEvent) { entered?() }
    override func mouseExited(with event: NSEvent) { exited?() }
}
private final class DeckTabView: NSView {
    var expand: (() -> Void)?
    var armed = false
    var title = "Deck"
    private var tracking: NSTrackingArea?
    override func updateTrackingAreas() {
        super.updateTrackingAreas(); if let tracking = tracking { removeTrackingArea(tracking) }
        let area = NSTrackingArea(rect:.zero,options:[.mouseEnteredAndExited,.mouseMoved,.activeAlways,.inVisibleRect],owner:self,userInfo:nil)
        addTrackingArea(area); tracking = area
    }
    override func draw(_ dirtyRect: NSRect) {
        Studio.key.setFill(); bounds.fill()
        (title as NSString).draw(in:bounds.insetBy(dx:8,dy:8),withAttributes:[.font:NSFont.systemFont(ofSize:11),.foregroundColor:Studio.text])
    }
    private func expandFromBody(_ event: NSEvent) {
        if armed && convert(event.locationInWindow,from:nil).x < bounds.width-20 { expand?() }
    }
    override func mouseEntered(with event: NSEvent) { expandFromBody(event) }
    override func mouseMoved(with event: NSEvent) { expandFromBody(event) }
    override func mouseExited(with event: NSEvent) { armed = true }
    override func mouseDown(with event: NSEvent) {
        if convert(event.locationInWindow,from:nil).x >= bounds.width - 20 { window?.performDrag(with:event) }
        else { expand?() }
    }
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }
}

enum DeckEdit { case rename(String), remove, move(Int), color(String?), icon(Data?) }

final class DeckController: NSObject, NSWindowDelegate, NSMenuDelegate {
    private(set) var panel: NSPanel!
    var layoutID = "default"
    var layoutChanged: ((DeckLayout) -> Void)?
    private var layoutState = DeckLayout(id:"default",name:"標準 Deck")
    private let edge = DeckPanel(contentRect:NSRect(x:0,y:0,width:130,height:34),styleMask:[.borderless,.nonactivatingPanel],backing:.buffered,defer:false)
    private let tab = DeckTabView()
    private let hover = DeckHoverView()
    private let multiSelect = DeckTitleButton(frame: .zero)
    private let titleHint = DeckTitleHint(labelWithString: "")
    private weak var hoveredTitleButton: DeckTitleButton?
    private var titleHintWork: DispatchWorkItem?
    private var isSelectingMultiple: Bool { multiSelect.state == .on }
    private var draggingItems = false
    private let pin = DeckTitleButton(frame: .zero)
    private var paddingConstraints: [NSLayoutConstraint] = []
    private var applyingFrame = false
    private var enteredExpanded = false
    private var frameTicket = 0
    private var dismissTicket = 0
    private var restoredPosition = false
    private let status = NSTextField(labelWithString: "停止中")
    private let scroll = NSScrollView()
    private var rendered: [String: DeckRenderState] = [:]
    private var cells: [String: (DeckTile, NSTextField)] = [:]
    private var order: [String] = []
    private var selectedIDs = Set<String>()
    private var anchorID: String?
    private var containers: [String: NSView] = [:]
    private let grid = StudioSurface()
    private var geometry = ""
    private var naturalGridSize = NSSize(width:1,height:1)
    private var maximizeRestore: NSRect?
    private var resizeStart: NSSize?
    private var resizeByWidth: Bool?
    private var emptyCells: [Int:DeckTile] = [:]
    private var monitorTimer: Timer?
    private let monitors = DeckMonitorPool.shared
    private let monitorOwner = UUID().uuidString
    private var inputEnabled = false
    private var noticeTicket = 0
    private var openMenus = 0
    private var monitorPopover: NSPopover?
    private var adjustingMonitor: DeckMonitor?
    var historyTitles: (() -> (String,String))?
    var undoEdit: (() -> Void)?
    var redoEdit: (() -> Void)?
    var canUndoEdit: (() -> Bool)?
    var canRedoEdit: (() -> Bool)?
    var performAction: ((Mapping) -> Void)?
    private var configuration = Configuration()
    var execute: ((String) -> Void)?
    var editMany: (([String],DeckEdit) -> Void)?
    var edit: ((String, DeckEdit) -> Void)?
    var receiveFile: ((URL,String) -> Bool)?
    var receiveAction: ((String,String) -> Bool)?
    var receiveEmptyAction: ((String,Int) -> Bool)?
    var receiveEmptyFile: ((URL,Int) -> Bool)?
    var moveToSlot: (([String],String,Int) -> Bool)?
    var requestColor: (([String]) -> Void)?
    var requestIcon: (([String]) -> Void)?
    var requestRename: ((String) -> Void)?

    override init() {
        super.init()
        panel = DeckPanel(contentRect: NSRect(x: 0, y: 0, width: 550, height: 180),
            styleMask: [.titled, .closable, .resizable, .nonactivatingPanel], backing: .buffered, defer: false)
        panel.title = "RELYR Deck"
        panel.appearance = nil
        panel.titlebarAppearsTransparent = true
        panel.backgroundColor = .windowBackgroundColor
        panel.isOpaque = true
        panel.hasShadow = true
        panel.isReleasedWhenClosed = false
        panel.level = .floating
        panel.hidesOnDeactivate = false
        panel.collectionBehavior = [.canJoinAllSpaces, .canJoinAllApplications, .fullScreenAuxiliary]
        panel.isMovableByWindowBackground = true
        panel.center(); panel.delegate = self
        NotificationCenter.default.addObserver(self,selector:#selector(screensChanged),name:NSApplication.didChangeScreenParametersNotification,object:nil)
        edge.acceptsMouseMovedEvents = true
        edge.contentView = tab; edge.isOpaque = true; edge.backgroundColor = Studio.key; edge.level = .floating
        edge.isReleasedWhenClosed = false; edge.hidesOnDeactivate = false; edge.collectionBehavior = [.canJoinAllSpaces,.canJoinAllApplications,.fullScreenAuxiliary]; edge.delegate = self
        tab.expand = { [weak self] in self?.expand() }
        hover.entered = { [weak self] in
            guard let self = self else { return }
            self.enteredExpanded = true
            self.scheduleDismiss(self.configuration.deckOnLeave ?? .stay,delay:0.5,requiresOutside:true)
        }
        hover.exited = { [weak self] in
            guard let self = self,!self.applyingFrame else { return }
            self.scheduleDismiss(self.configuration.deckOnLeave ?? .stay,delay:0.5,requiresOutside:true)
        }
        pin.target = self; pin.action = #selector(togglePin)
        multiSelect.target = self; multiSelect.action = #selector(toggleMultiSelect)
        let collapse = DeckTitleButton(frame: .zero)
        collapse.target = self; collapse.action = #selector(collapseClicked)
        let zoom = DeckTitleButton(frame: .zero)
        zoom.target = self; zoom.action = #selector(toggleMaximize)
        let reset = DeckTitleButton(frame: .zero)
        reset.target = self; reset.action = #selector(resetSize)
        let titleButtons = [
            (pin, "pin", "パネルを固定"),
            (multiSelect, "checkmark.circle", "複数選択"),
            (collapse, "rectangle.compress.vertical", "折り畳む"),
            (zoom, "arrow.up.left.and.arrow.down.right", "最大化／元のサイズ"),
            (reset, "arrow.counterclockwise", "初期サイズに戻す")
        ]
        for (button, symbol, caption) in titleButtons {
            let toggle = button === pin || button === multiSelect
            button.setButtonType(toggle ? .pushOnPushOff : .momentaryPushIn)
            button.bezelStyle = .texturedRounded
            button.isBordered = false
            button.image = NSImage(systemSymbolName: symbol, accessibilityDescription: caption)
            button.imagePosition = .imageOnly
            button.setAccessibilityLabel(caption)
            button.controlSize = .small
            button.widthAnchor.constraint(equalToConstant: 28).isActive = true
            button.heightAnchor.constraint(equalToConstant: 26).isActive = true
            button.hoverChanged = { [weak self] button, inside in
                self?.titleButtonHover(button, inside: inside)
            }
        }
        updatePinAppearance()
        updateMultiSelectAppearance()
        let controls = NSStackView(views: [status] + titleButtons.map { $0.0 })
        controls.spacing = 4
        status.widthAnchor.constraint(equalToConstant:80).isActive = true
        let titlebarControls = NSTitlebarAccessoryViewController()
        titlebarControls.layoutAttribute = .right
        let titlebarHost = NSView(frame:NSRect(x:0,y:0,width:248,height:28))
        controls.translatesAutoresizingMaskIntoConstraints = false
        titlebarHost.addSubview(controls)
        NSLayoutConstraint.activate([
            controls.leadingAnchor.constraint(equalTo:titlebarHost.leadingAnchor),
            controls.trailingAnchor.constraint(equalTo:titlebarHost.trailingAnchor,constant:-8),
            controls.centerYAnchor.constraint(equalTo:titlebarHost.centerYAnchor)
        ])
        titlebarControls.view = titlebarHost
        panel.addTitlebarAccessoryViewController(titlebarControls)
        panel.contentView = hover
        scroll.hasVerticalScroller = false
        scroll.hasHorizontalScroller = false
        grid.transparent = true
        scroll.documentView = grid
        scroll.drawsBackground = false
        status.font = .systemFont(ofSize: 11)
        status.textColor = .secondaryLabelColor
        status.lineBreakMode = .byTruncatingTail; status.maximumNumberOfLines = 1
        status.setContentCompressionResistancePriority(.defaultLow,for:.horizontal)
        let root = NSStackView(views: [scroll])
        root.orientation = .vertical; root.alignment = .leading; root.spacing = 0
        root.translatesAutoresizingMaskIntoConstraints = false
        panel.contentView!.addSubview(root)
        paddingConstraints = [
            root.leadingAnchor.constraint(equalTo: panel.contentView!.leadingAnchor, constant: 12),
            root.trailingAnchor.constraint(equalTo: panel.contentView!.trailingAnchor, constant: -12),
            root.topAnchor.constraint(equalTo: panel.contentView!.topAnchor, constant: 12),
            root.bottomAnchor.constraint(equalTo: panel.contentView!.bottomAnchor, constant: -12)
        ]
        NSLayoutConstraint.activate(paddingConstraints + [scroll.widthAnchor.constraint(equalTo: root.widthAnchor)])
        var grips: [DeckResizeGrip] = []
        for left in [true,false] { for bottom in [true,false] {
            let grip = DeckResizeGrip(left:left,bottom:bottom)
            grips.append(grip)
            grip.translatesAutoresizingMaskIntoConstraints = false
            hover.addSubview(grip)
            NSLayoutConstraint.activate([
                grip.widthAnchor.constraint(equalToConstant:14),grip.heightAnchor.constraint(equalToConstant:14),
                left ? grip.leadingAnchor.constraint(equalTo:hover.leadingAnchor) : grip.trailingAnchor.constraint(equalTo:hover.trailingAnchor),
                bottom ? grip.bottomAnchor.constraint(equalTo:hover.bottomAnchor) : grip.topAnchor.constraint(equalTo:hover.topAnchor)
            ])
            grip.begin = { [weak self] in guard let self = self else { return }; self.dismissTicket += 1; self.windowWillStartLiveResize(Notification(name:NSWindow.willStartLiveResizeNotification,object:self.panel)) }
            grip.resize = { [weak self] size in guard let self = self else { return size }; return self.windowWillResize(self.panel,to:size) }
            grip.end = { [weak self] in guard let self = self else { return }; self.windowDidEndLiveResize(Notification(name:NSWindow.didEndLiveResizeNotification,object:self.panel)) }
        } }

        (panel as? DeckPanel)?.resizeCorner = { [weak panel] event in
            guard let panel = panel else { return false }
            let p = event.locationInWindow, size = panel.frame.size
            guard p.x >= 0,p.y >= 0,p.x <= size.width,p.y <= size.height,
                  ((p.y < 14 && (p.x < 14 || p.x > size.width-14)) || (p.y > size.height-8 && (p.x < 8 || p.x > size.width-8))),
                  let grip = grips.first(where:{ $0.left == (p.x < size.width/2) && $0.bottom == (p.y < size.height/2) }) else { return false }
            grip.mouseDown(with:event); return true
        }
    }

    private func titleButtonHover(_ button: DeckTitleButton, inside: Bool) {
        if !inside {
            if hoveredTitleButton === button { clearTitleHint() }
            return
        }
        clearTitleHint()
        hoveredTitleButton = button
        let work = DispatchWorkItem { [weak self, weak button] in
            guard let self, let button, self.hoveredTitleButton === button else { return }
            self.showTitleHint(for: button)
        }
        titleHintWork = work
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.18, execute: work)
    }

    private func showTitleHint(for button: DeckTitleButton) {
        guard panel.isVisible, button.window === panel, let caption = button.hint ?? button.accessibilityLabel() else { return }
        titleHint.stringValue = caption
        titleHint.alignment = .center
        titleHint.font = .systemFont(ofSize: 11, weight: .medium)
        titleHint.textColor = Studio.text
        titleHint.wantsLayer = true
        titleHint.layer?.backgroundColor = Studio.key.cgColor
        titleHint.layer?.borderColor = Studio.border.cgColor
        titleHint.layer?.borderWidth = 1
        titleHint.layer?.cornerRadius = 6
        let width = min(hover.bounds.width - 16, ceil(titleHint.intrinsicContentSize.width) + 18)
        guard width > 0 else { return }
        let anchor = hover.convert(button.convert(button.bounds, to: nil), from: nil)
        let x = min(max(8, anchor.midX - width / 2), hover.bounds.width - width - 8)
        titleHint.frame = NSRect(x: x, y: hover.bounds.height - 24, width: width, height: 20)
        hover.addSubview(titleHint, positioned: .above, relativeTo: nil)
    }

    private func clearTitleHint() {
        titleHintWork?.cancel()
        titleHintWork = nil
        hoveredTitleButton = nil
        titleHint.removeFromSuperview()
    }

    var isCollapsed: Bool { edge.isVisible }
    func show(collapsed: Bool) { if !isVisible { expand(); if collapsed { collapse() } } }
    var isVisible: Bool { panel.isVisible || edge.isVisible }
    func hide() { clearTitleHint(); monitorPopover?.close(); monitorPopover = nil; DeckFilePreview.shared.close(owners:Set(configuration.deck.map { $0.id })); dismissTicket += 1; monitorTimer?.invalidate(); monitorTimer = nil; monitors.release(owner:monitorOwner); applyingFrame = true; panel.orderOut(nil); edge.orderOut(nil); applyingFrame = false; enteredExpanded = false }
    func toggle() {
        if isVisible { hide() } else {
            if layoutState.showAtCursor == true {
                let point = NSEvent.mouseLocation
                let screen = NSScreen.screens.first { $0.frame.contains(point) }?.visibleFrame ?? NSScreen.main?.visibleFrame
                if let screen = screen {
                    let x = max(screen.minX,min(point.x-panel.frame.width/2,screen.maxX-panel.frame.width))
                    let y = max(screen.minY,min(point.y-panel.frame.height/2,screen.maxY-panel.frame.height))
                    applyingFrame = true; panel.setFrameOrigin(NSPoint(x:x,y:y)); applyingFrame = false
                }
            }
            expand()
        }
    }
    // Choose the display with the greatest intersection, then the nearest display.
    static func reachableFrame(_ frame: NSRect, screens: [NSRect]) -> NSRect {
        guard let area = screens.max(by: { a,b in
            func score(_ r: NSRect) -> CGFloat {
                let intersection = frame.intersection(r)
                if !intersection.isNull && intersection.width > 0 && intersection.height > 0 { return intersection.width * intersection.height }
                return -hypot(frame.midX-r.midX,frame.midY-r.midY)
            }
            return score(a) < score(b)
        }) else { return frame }
        let size = NSSize(width:min(frame.width,area.width),height:min(frame.height,area.height))
        return NSRect(x:max(area.minX,min(frame.minX,area.maxX-size.width)),
                      y:max(area.minY,min(frame.minY,area.maxY-size.height)),width:size.width,height:size.height)
    }
    private func constrainToDisplays() {
        let areas = NSScreen.screens.map { $0.visibleFrame }
        applyingFrame = true
        panel.setFrame(Self.reachableFrame(panel.frame,screens:areas),display:panel.isVisible)
        edge.setFrame(Self.reachableFrame(edge.frame,screens:areas),display:edge.isVisible)
        if let saved = maximizeRestore { maximizeRestore = Self.reachableFrame(saved,screens:areas) }
        fitGrid(); applyingFrame = false
    }
    @objc private func screensChanged() { constrainToDisplays(); saveFrames() }
    @objc private func undoDeckEdit() { undoEdit?() }
    @objc private func redoDeckEdit() { redoEdit?() }
    private func expand() {
        constrainToDisplays()
        dismissTicket += 1
        applyingFrame = true; edge.orderOut(nil); panel.orderFront(nil); Studio.transition(hover); applyingFrame = false
        enteredExpanded = panel.frame.contains(NSEvent.mouseLocation); refreshMonitors()
        scheduleDismiss(configuration.deckOnLeave ?? .stay,delay:0.6,requiresOutside:true)
    }
    @objc private func toggleMultiSelect() {
        updateMultiSelectAppearance()
        selectedIDs.removeAll()
        anchorID = nil
        update(configuration, enabled: inputEnabled)
        sizeToFit()
        Studio.transition(grid)
    }

    private func updateMultiSelectAppearance() {
        let active = multiSelect.state == .on
        multiSelect.image = NSImage(systemSymbolName: active ? "checkmark.circle.fill" : "checkmark.circle",
                                    accessibilityDescription: "複数選択")
        multiSelect.contentTintColor = active ? Studio.accent : nil
        multiSelect.hint = active ? "複数選択中 · クリックで追加・解除" : "複数選択 · 選んだボタンをまとめて移動"
        multiSelect.setAccessibilityHelp(multiSelect.hint)
    }

    private func updatePinAppearance() {
        let pinned = layoutState.pinned
        pin.state = pinned ? .on : .off
        pin.image = NSImage(systemSymbolName: pinned ? "pin.fill" : "pin", accessibilityDescription: "ピン留め")
        pin.contentTintColor = pinned ? Studio.accent : nil
        pin.setAccessibilityLabel(pinned ? "ピン留め中" : "ピン留め")
        pin.hint = pinned ? "ピン留め中 · クリックで解除" : "ピン留め · 自動で閉じないようにする"
        pin.setAccessibilityHelp(pin.hint)
    }

    @objc private func collapseClicked() { collapse() }
    @objc private func togglePin() {
        layoutState.pinned = pin.state == .on
        updatePinAppearance()
        layoutChanged?(layoutState)
        showNotice(layoutState.pinned ? "固定中：実行後・マウス離脱後の自動非表示は止まります" : "固定を解除しました")
        if !layoutState.pinned && panel.isVisible {
            scheduleDismiss(configuration.deckOnLeave ?? .stay,delay:0.35,requiresOutside:true)
        }
    }
    private func collapse() {
        clearTitleHint()
        monitorPopover?.close(); monitorPopover = nil; DeckFilePreview.shared.close(owners:Set(configuration.deck.map { $0.id }))
        monitorTimer?.invalidate(); monitorTimer = nil; monitors.release(owner:monitorOwner)
        applyingFrame = true
        let frame = maximizeRestore ?? panel.frame
        layoutState.expanded = DeckBounds(x:frame.minX,y:frame.minY,width:frame.width,height:frame.height)
        if let saved = layoutState.collapsed { edge.setFrame(NSRect(x:saved.x,y:saved.y,width:130,height:34),display:false) }
        else { let screen = panel.screen?.visibleFrame ?? frame; edge.setFrameOrigin(NSPoint(x:screen.maxX-130,y:min(screen.maxY-34,max(screen.minY,frame.midY)))) }
        edge.setFrame(Self.reachableFrame(edge.frame,screens:NSScreen.screens.map { $0.visibleFrame }),display:false)
        panel.orderOut(nil); edge.orderFront(nil); Studio.transition(tab); tab.armed = !edge.frame.contains(NSEvent.mouseLocation)
        applyingFrame = false; enteredExpanded = false; saveFrames()
    }
    private func scheduleDismiss(_ behavior: DeckDisplayBehavior, delay: Double, requiresOutside: Bool) {
        guard behavior != .stay else { return }
        dismissTicket += 1; let ticket = dismissTicket
        DispatchQueue.main.asyncAfter(deadline:.now()+delay) { [weak self] in
            guard let self = self,self.dismissTicket == ticket,self.panel.isVisible,!self.layoutState.pinned else { return }
            if requiresOutside && self.panel.frame.contains(NSEvent.mouseLocation) {
                // The pointer can pass through the title bar after leaving the
                // tracked content. Keep watching until it leaves the whole panel.
                self.scheduleDismiss(behavior,delay:0.2,requiresOutside:true); return
            }
            if DeckTile.activeDrags > 0 || NSEvent.pressedMouseButtons != 0 || self.openMenus > 0 || self.monitorPopover?.isShown == true || DeckFilePreview.shared.isInteracting(owners:Set(self.configuration.deck.map { $0.id })) {
                self.scheduleDismiss(behavior,delay:0.2,requiresOutside:requiresOutside); return
            }
            self.applyBehavior(behavior)
        }
    }
    private func applyBehavior(_ behavior: DeckDisplayBehavior) {
        guard !layoutState.pinned, panel.isVisible else { return }
        switch behavior { case .stay: break; case .collapse: collapse(); case .hide: hide() }
    }
    private func saveFrames() {
        guard !applyingFrame,restoredPosition,resizeStart == nil else { return }
        if panel.isVisible && maximizeRestore == nil { let f = panel.frame; layoutState.expanded = DeckBounds(x:f.minX,y:f.minY,width:f.width,height:f.height) }
        if edge.isVisible { let f = edge.frame; layoutState.collapsed = DeckBounds(x:f.minX,y:f.minY,width:f.width,height:f.height) }
        layoutChanged?(layoutState)
    }
    func windowDidMove(_ notification: Notification) {
        guard !applyingFrame else { return }; frameTicket += 1; let ticket = frameTicket
        DispatchQueue.main.asyncAfter(deadline:.now()+0.25) { [weak self] in guard let self = self,self.frameTicket == ticket else { return }; self.saveFrames() }
    }
    func windowWillClose(_ notification: Notification) { saveFrames(); hide() }

    func update(_ fullConfiguration: Configuration, enabled: Bool) {
        let changedAssignments = Studio.changedDeckAssignments(from:self.configuration,to:fullConfiguration)
        let animateChanges = panel.isVisible && !cells.isEmpty
        defer {
            if animateChanges {
                for id in changedAssignments { if let tile = cells[id]?.0 { Studio.assignmentCommit(tile) } }
            }
        }

        let previousPadding = layoutState.padding
        let previousPinned = layoutState.pinned
        let previousLeaveBehavior = self.configuration.deckOnLeave
        var configuration = fullConfiguration
        layoutState = fullConfiguration.deckLayout(layoutID)
        let visiblePositions = fullConfiguration.deckPositions(layoutID), visibleCount = fullConfiguration.deckSlotCount(layoutID)
        configuration.deck = fullConfiguration.deckItems(layoutID).filter { visiblePositions[$0.id,default:0] < visibleCount }
        configuration.deckColumns = layoutState.columns; configuration.deckLabels = layoutState.labels
        DeckFilePreview.shared.close(owners:Set(self.configuration.deck.map { $0.id }).subtracting(configuration.deck.map { $0.id }))
        self.configuration = configuration
        panel.title = "RELYR — " + layoutState.name; tab.title = layoutState.name + "  ⋮"; tab.needsDisplay = true
        updatePinAppearance()
        for (index,constraint) in paddingConstraints.enumerated() { constraint.constant = CGFloat(layoutState.padding) * (index == 1 || index == 3 ? -1 : 1) }
        applyingFrame = true; defer { applyingFrame = false }
        if !restoredPosition, let frame = layoutState.expanded { panel.setFrame(NSRect(x:frame.x,y:frame.y,width:frame.width,height:frame.height),display:false) }
        restoredPosition = true
        let displayPositions = fullConfiguration.deckPositions(layoutID)
        let ids = configuration.deck.sorted { displayPositions[$0.id,default:0] < displayPositions[$1.id,default:0] }.map { $0.id }
        selectedIDs.formIntersection(Set(ids))
        for id in order where !ids.contains(id) { containers.removeValue(forKey: id)?.removeFromSuperview(); cells.removeValue(forKey: id); rendered.removeValue(forKey:id) }
        for item in configuration.deck where cells[item.id] == nil {
            let button = DeckTile(title: "", target: self, action: #selector(clicked(_:)))
            button.allowsReordering = true
            button.identifier = NSUserInterfaceItemIdentifier(item.id); button.fill = Studio.key
            button.frame = NSRect(x: 0, y: 0, width: 54, height: 52); button.fitKeyLabel = true
            button.menu = contextMenu(item.id)
            button.dragAnchorID = item.id
            button.dragIDs = { [weak self] in
                guard let self = self else { return [] }
                if !self.isSelectingMultiple { return [item.id] }
                if !self.selectedIDs.contains(item.id) { self.selectedIDs = [item.id]; self.refreshSelection() }
                return self.order.filter { self.selectedIDs.contains($0) }
            }
            button.dragStateChanged = { [weak self] active in
                guard let self = self else { return }
                self.draggingItems = active
                for tile in self.emptyCells.values { tile.showsEmptyDropVisual = active }
            }
            button.movePreviewRoot = grid
            button.movePreviewContext = { [weak self] in
                guard let self = self, let slot = self.configuration.deckPositions(self.layoutID)[item.id] else { return nil }
                return (self.configuration,self.layoutID,slot)
            }
            button.moveItems = { [weak self] ids, anchor, target in self?.moveToSlot?(ids,anchor,target) ?? false }
            button.receiveFile = { [weak self] url in self?.receiveFile?(url,item.id) ?? false }
            button.fileURL = { [weak self] in
                guard let self = self, let current = self.configuration.deck.first(where: { $0.id == item.id }), let mapping = self.configuration.mapping(for:current), [.file,.launch].contains(mapping.action) else { return nil }
                return URL(fileURLWithPath:mapping.application)
            }
            button.enableDrop { [weak self] json in self?.receiveAction?(json,item.id) ?? false }
            button.hoverChanged = { [weak self,weak button] inside in
                guard let self = self,let button = button else { return }
                if inside { self.showNotice(item.title) }
                if inside,self.layoutState.hoverPreviews != false,let current = self.configuration.deck.first(where:{ $0.id == item.id }),let mapping = self.configuration.mapping(for:current),mapping.action == .file {
                    DeckFilePreview.shared.hover(URL(fileURLWithPath:mapping.application),owner:item.id,anchor:button)
                } else { DeckFilePreview.shared.cancel(owner:item.id) }
            }
            button.enableDeckDrops()
            let label = NSTextField(wrappingLabelWithString: item.title)
            label.alignment = .center; label.maximumNumberOfLines = 1; label.lineBreakMode = .byTruncatingTail
            label.font = .systemFont(ofSize: 11); label.frame = NSRect(x: 0, y: 53, width: 54, height: 14)
            let cell = StudioSurface(); cell.transparent = true; cell.addSubview(button); cell.addSubview(label); grid.addSubview(cell)
            cells[item.id] = (button,label); containers[item.id] = cell
        }
        let positions = fullConfiguration.deckPositions(layoutID), slotCount = fullConfiguration.deckSlotCount(layoutID)
        let occupied = Set(positions.values)
        for slot in Array(emptyCells.keys) where slot >= slotCount || occupied.contains(slot) { emptyCells.removeValue(forKey:slot)?.removeFromSuperview() }
        for slot in 0..<slotCount where !occupied.contains(slot) && emptyCells[slot] == nil {
            let tile = DeckTile("＋") {}; tile.isEmptyDropTarget = true; tile.fill = Studio.key.withAlphaComponent(0.4); tile.labelColor = Studio.muted; tile.toolTip = "空きマス \(slot+1) — Actionやファイルをドラッグ"
            tile.enableDrop { [weak self] json in self?.receiveEmptyAction?(json,slot) ?? false }
            tile.receiveFile = { [weak self] url in self?.receiveEmptyFile?(url,slot) ?? false }
            tile.movePreviewRoot = grid
            tile.emptyMoveSlot = slot
            tile.movePreviewContext = { [weak self] in self.map { ($0.configuration,$0.layoutID,slot) } }
            tile.moveItems = { [weak self] ids, anchor, target in self?.moveToSlot?(ids,anchor,target) ?? false }
            tile.contextBuilder = { [weak self] in
                let menu = NSMenu(); menu.autoenablesItems = false; menu.delegate = self
                let command = NSMenuItem(title:"クリップボードのファイルを登録",action:#selector(DeckController.contextAction(_:)),keyEquivalent:""); command.target = self; command.tag = 33; command.representedObject = slot; command.isEnabled = DeckFilePreview.clipboardFile() != nil; menu.addItem(command); return menu
            }
            tile.enableDeckDrops(); tile.showsEmptyDropVisual = draggingItems; grid.addSubview(tile); emptyCells[slot] = tile
        }
        let sameItems = order == ids
        let newGeometry = "\(isSelectingMultiple):\(configuration.columns):\(configuration.showsDeckLabels):\(slotCount):\(layoutState.padding):\(ids.map { positions[$0] ?? 0 })"
        if order != ids || geometry != newGeometry {
            let restoreSavedSize = geometry.isEmpty && layoutState.expanded != nil
            order = ids; geometry = newGeometry
            let columns = configuration.columns
            let gap = DeckMetrics.gap(labels:configuration.showsDeckLabels)
            let stepX = DeckMetrics.stepX(labels:configuration.showsDeckLabels), stepY = DeckMetrics.stepY(labels:configuration.showsDeckLabels)
            let displayedSlots = isSelectingMultiple ? slotCount : max(1, (configuration.deck.compactMap { positions[$0.id] }.max() ?? 0) + 1)
            let rows = max(1, (displayedSlots + columns - 1) / columns)
            for id in ids { let i = positions[id] ?? 0; if let cell = containers[id] { Studio.place(cell,at:NSRect(x: CGFloat(i % columns) * stepX, y: CGFloat(i / columns) * stepY, width: 54, height: configuration.showsDeckLabels ? 66 : 52),animated:sameItems) } }
            for (slot,tile) in emptyCells { tile.frame = NSRect(x:CGFloat(slot%columns)*stepX,y:CGFloat(slot/columns)*stepY,width:54,height:52) }
            let width = CGFloat(columns) * stepX - gap
            let height = CGFloat(rows) * stepY - gap + (configuration.showsDeckLabels ? 14 : 0)
            let changedSize = naturalGridSize != NSSize(width:width,height:height) || previousPadding != layoutState.padding
            naturalGridSize = NSSize(width:width,height:height)
            if changedSize && !restoreSavedSize && maximizeRestore == nil && !sameItems { sizeToFit() }
            if restoreSavedSize && !isSelectingMultiple {
                // Preserve the user's icon scale/width, removing only unused trailing rows.
                let width = panel.contentView?.bounds.width ?? panel.frame.width
                let height = naturalGridSize.height * max(0.01,(width-chromeSize.width)/naturalGridSize.width) + chromeSize.height
                panel.setContentSize(NSSize(width:width,height:height))
            }
            fitGrid()
        }
        for item in configuration.deck {
            guard let (button, label) = cells[item.id], let mapping = configuration.mapping(for: item) else { continue }
            label.isHidden = !configuration.showsDeckLabels
            button.monitorCaption = item.monitor?.caption ?? ""
            button.monitorKind = item.monitor
            if let kind = item.monitor, [.volume, .microphone, .brightness].contains(kind) {
                button.wheelAction = { [weak self] delta in self?.adjustMonitor(kind, delta: delta) }
            } else {
                button.wheelAction = nil
                button.monitorSamples = []
            }
            Studio.configureDeckLabel(label,text:fullConfiguration.deckTitle(item)); button.hoverAnimation = layoutState.hoverAnimation != false
            let state = DeckRenderState(item:item,mapping:mapping,keyboardLayout:configuration.keyboardLayout,profileName:configuration.profileName(item.profileID))
            guard rendered[item.id] != state else { continue }
            rendered[item.id] = state
            Studio.configureDeckLabel(label,text:fullConfiguration.deckTitle(item)); label.toolTip = item.title
            button.fill = Studio.color(item.colorHex) ?? Studio.key
            button.toolTip = item.monitor.map { "\($0.title)：\($0.caption)" } ?? "\(item.title) — \(configuration.profileName(item.profileID)): \(mapping.description(layout: configuration.keyboardLayout))"
            button.setAccessibilityLabel(item.title)
            switch mapping.action {
            case .key, .shortcut, .mouse: button.title = mapping.targetDescription(layout: configuration.keyboardLayout)
            case .launch: button.title = ""
            case .text: button.title = "文字"
            case .url: button.title = "↗"
            case .disabled: button.title = "無効"
            case .profile: button.title = "プロファイル"
            case .macro: button.title = "マクロ"
            case .gesture: button.title = "ジェスチャー"
            case .deck: button.title = "Deck"
            case .system: button.title = SystemCommand(rawValue:mapping.payload)?.title ?? "Action"
            case .window: button.title = "▣"
            case .file: button.title = "開く"
            }
            button.image = Studio.deckImage(mapping,iconPNG:item.iconPNG)
            button.focusRingType = .none
        }
        refreshSelection()
        refreshMonitors()
        setEnabled(enabled)
        if panel.isVisible, previousLeaveBehavior != configuration.deckOnLeave || (previousPinned && !layoutState.pinned) {
            dismissTicket += 1
            scheduleDismiss(configuration.deckOnLeave ?? .stay,delay:0.5,requiresOutside:true)
        }
    }

    private var chromeSize: NSSize { NSSize(width:CGFloat(layoutState.padding*2),height:CGFloat(layoutState.padding*2)) }
    private func sizeToFit() {
        let area = panel.screen?.visibleFrame ?? NSScreen.main?.visibleFrame ?? NSRect(x:0,y:0,width:1000,height:700)
        let chrome = chromeSize
        let scale = min(1,min((area.width-48-chrome.width)/max(1,naturalGridSize.width),(area.height-48-chrome.height)/max(1,naturalGridSize.height)))
        let top = panel.frame.maxY
        let width = max(360,naturalGridSize.width*scale+chrome.width)
        let height = naturalGridSize.height * max(0.01,(width-chrome.width)/naturalGridSize.width) + chrome.height
        panel.setContentSize(NSSize(width:width,height:height))
        panel.setFrameOrigin(NSPoint(x:panel.frame.minX,y:top-panel.frame.height))
    }
    private func fitGrid() {
        panel.contentView?.layoutSubtreeIfNeeded()
        let available = scroll.contentSize
        let scale = max(0.01,min(available.width/max(1,naturalGridSize.width),available.height/max(1,naturalGridSize.height)))
        grid.frame = NSRect(x:0,y:0,width:naturalGridSize.width*scale,height:naturalGridSize.height*scale)
        grid.bounds = NSRect(origin:.zero,size:naturalGridSize)
    }
    @objc private func toggleMaximize() {
        applyingFrame = true
        if let saved = maximizeRestore { maximizeRestore = nil; panel.setFrame(saved,display:true,animate:!Studio.reduceMotion) }
        else if let screen = panel.screen?.visibleFrame { maximizeRestore = panel.frame; panel.setFrame(screen,display:true,animate:!Studio.reduceMotion) }
        fitGrid(); applyingFrame = false
    }
    @objc private func resetSize() {
        applyingFrame = true; maximizeRestore = nil; sizeToFit(); fitGrid(); applyingFrame = false; saveFrames()
    }
    func windowDidResize(_ notification: Notification) {
        guard !applyingFrame else { return }; fitGrid(); saveFrames()
    }
    func windowWillStartLiveResize(_ notification: Notification) { guard !applyingFrame else { return }; maximizeRestore = nil; resizeStart = panel.contentRect(forFrameRect:panel.frame).size; resizeByWidth = nil }
    func windowDidEndLiveResize(_ notification: Notification) { resizeStart = nil; resizeByWidth = nil; saveFrames() }
    func windowWillResize(_ sender: NSWindow, to frameSize: NSSize) -> NSSize {
        guard sender === panel,!applyingFrame else { return frameSize }
        let content = panel.contentRect(forFrameRect:NSRect(origin:.zero,size:frameSize)).size
        let chrome = chromeSize
        let ratio = resizeStart.map { max(1,$0.height-chrome.height)/max(1,$0.width-chrome.width) } ?? (naturalGridSize.height/max(1,naturalGridSize.width))
        if resizeByWidth == nil,let start = resizeStart {
            let dx = abs(content.width-start.width),dy = abs(content.height-start.height)
            if max(dx,dy) < 2 { return panel.frameRect(forContentRect:NSRect(origin:.zero,size:start)).size }
            resizeByWidth = dx >= dy/max(0.01,ratio)
        }
        let proposedWidth = resizeByWidth == false ? (content.height-chrome.height)/max(0.01,ratio)+chrome.width : content.width
        let width = max(360,max(proposedWidth,(120-chrome.height)/max(0.01,ratio)+chrome.width))
        let height = (width-chrome.width)*ratio+chrome.height
        return panel.frameRect(forContentRect:NSRect(x:0,y:0,width:width,height:height)).size
    }
    private func refreshMonitors() {
        let kinds = Set(configuration.deck.compactMap { $0.monitor })
        guard panel.isVisible,!kinds.isEmpty else { monitorTimer?.invalidate(); monitorTimer = nil; monitors.release(owner:monitorOwner); return }
        let values = monitors.sample(owner:monitorOwner,kinds:kinds,updated:{ [weak self] in self?.refreshMonitors() })
        for item in configuration.deck { if let kind = item.monitor, let button = cells[item.id]?.0 { button.image = nil; button.title = kind == .autoExtract ? (configuration.archiveSettings?.enabled == true ? "ON" : "OFF") : (values[kind] ?? "—"); button.monitorSamples = monitors.histories[kind] ?? [] } }
        if monitorTimer == nil {
            let timer = Timer(timeInterval:1,repeats:true) { [weak self] _ in self?.refreshMonitors() }
            monitorTimer = timer; RunLoop.main.add(timer,forMode:.common)
        }
    }
    func showNotice(_ message: String) {
        guard isVisible else { return }
        noticeTicket += 1; let ticket = noticeTicket
        status.toolTip = message
        if message.hasPrefix("Action送信") { status.stringValue = "実行しました" }
        else { status.stringValue = message }
        DispatchQueue.main.asyncAfter(deadline:.now()+10) { [weak self] in
            guard let self = self,self.noticeTicket == ticket else { return }
            self.setEnabled(self.inputEnabled)
        }
    }
    func setEnabled(_ enabled: Bool) {
        noticeTicket += 1; status.toolTip = nil
        inputEnabled = enabled
        status.stringValue = isSelectingMultiple ? "複数選択中" : (enabled ? "" : "入力停止中")
        status.toolTip = isSelectingMultiple ? "クリックで選択・ドラッグで移動。丸いチェックを押すと実行に戻ります。" : (enabled ? nil : "実行には入力変換を有効にしてください")
        for (button, _) in cells.values { button.isEnabled = true }
    }

    func verifyPresentation(expectedButtons: Int) {
        panel.contentView?.layoutSubtreeIfNeeded()
        scroll.documentView?.layoutSubtreeIfNeeded()
        verifySmoke(cells.count == expectedButtons && panel.isVisible)
        verifySmoke(!panel.canBecomeKey && !panel.canBecomeMain)
        verifySmoke(panel.titlebarAccessoryViewControllers.count == 1,"Deck controls live in the title bar")
        verifySmoke(panel.isOpaque && panel.backgroundColor == .windowBackgroundColor && panel.titlebarAppearsTransparent,
                    "Deck title bar and body share one opaque adaptive background")
        verifySmoke(paddingConstraints.map { abs($0.constant) }.allSatisfy { $0 == CGFloat(layoutState.padding) },"Deck content has equal padding on all four edges")
        verifySmoke(panel.styleMask.contains(.nonactivatingPanel))
        for (button, _) in cells.values {
            // AppKit frames include bezel/shadow insets; width/height constraints size the alignment rect.
            let rect = button.alignmentRect(forFrame: button.frame)
            verifySmoke(rect.width == 54 && rect.height == 52, "Deck button alignment \(rect), frame \(button.frame)")
            verifySmoke(!inputEnabled, "Smoke test must never enable input")
            let originalExecute = execute; var executed = false
            execute = { _ in executed = true }; clicked(button); execute = originalExecute
            verifySmoke(!executed && button.isEnabled,"Stopped Deck remains editable without executing actions")
        }
    }

    static func verifyLifecycle() {
        let first = DeckController(), second = DeckController()
        let surfaces: [NSWindow] = [first.panel, first.edge]
        for surface in surfaces {
            verifySmoke(surface.collectionBehavior.contains(.canJoinAllSpaces) &&
                        surface.collectionBehavior.contains(.canJoinAllApplications) &&
                        surface.collectionBehavior.contains(.fullScreenAuxiliary),
                        "Deck surfaces follow Spaces and full-screen applications")
        }
        var configuration = Configuration()
        let other = DeckLayout(name:"Second",columns:2,labels:false)
        configuration.setDeckLayout(other); second.layoutID = other.id
        first.update(configuration,enabled:false); second.update(configuration,enabled:false)
        first.toggle(); second.toggle(); let secondFrame = second.panel.frame
        verifySmoke(first.panel.isVisible && second.panel.isVisible,"Independent Decks simultaneously visible")
        first.collapse()
        verifySmoke(!first.panel.isVisible && first.edge.isVisible && first.edge.frame.width == 130 && first.edge.frame.height == 34,"Collapsed surface is bounded and expanded panel hidden")
        verifySmoke(second.panel.isVisible && second.panel.frame == secondFrame,"Collapse leaves other Deck untouched")
        first.edge.setFrameOrigin(NSPoint(x:50,y:70)); first.saveFrames()
        let collapsed = first.layoutState.collapsed
        first.expand(); first.collapse()
        verifySmoke(first.layoutState.collapsed == collapsed,"Tab position survives expansion and collapse")
        first.expand()
        let initialPin = first.pin.image
        first.pin.performClick(nil)
        verifySmoke(first.layoutState.pinned && first.pin.state == .on &&
                    first.pin.image != initialPin && first.pin.contentTintColor != nil &&
                    first.pin.hint?.contains("ピン留め中") == true,
                    "Pin click shows the filled, tinted pin and explains its state")
        first.applyBehavior(.hide)
        verifySmoke(first.panel.isVisible, "Pin prevents automatic hiding")
        first.pin.performClick(nil)
        verifySmoke(!first.layoutState.pinned && first.pin.state == .off && first.pin.contentTintColor == nil,
                    "Unpin restores the inactive appearance")
        var savedPin = configuration
        var pinnedLayout = savedPin.deckLayout("default")
        pinnedLayout.pinned = true
        savedPin.setDeckLayout(pinnedLayout)
        first.update(savedPin, enabled: false)
        verifySmoke(first.pin.state == .on && first.pin.accessibilityLabel() == "ピン留め中",
                    "Saved pin state is visible on the panel")
        first.update(configuration, enabled: false)
        first.applyBehavior(.hide)
        verifySmoke(!first.isVisible,"Hide removes both surfaces")
        let display = NSRect(x:0,y:24,width:1000,height:700)
        let recovered = Self.reachableFrame(NSRect(x:2400,y:-500,width:1400,height:900),screens:[display])
        verifySmoke(display.contains(recovered),"Disconnected display and oversized Deck recover within visible frame")
        let secondDisplay = NSRect(x:-1200,y:24,width:1200,height:800)
        let onSecond = NSRect(x:-1000,y:100,width:400,height:300)
        verifySmoke(Self.reachableFrame(onSecond,screens:[display,secondDisplay]) == onSecond,"Valid secondary display position remains unchanged")
        verifySmoke(Self.reachableFrame(onSecond,screens:[]) == onSecond,"Empty display transition preserves frame")
        var undoCalls = 0
        first.undoEdit = { undoCalls += 1 }; first.canUndoEdit = { true }; first.canRedoEdit = { false }
        let historyMenu = first.contextMenu("fixture")
        first.menuWillOpen(historyMenu)
        verifySmoke(historyMenu.item(withTag:40)?.isEnabled == true && historyMenu.item(withTag:41)?.isEnabled == false,"Deck menu reflects shared editor history")
        first.undoDeckEdit(); verifySmoke(undoCalls == 1,"Deck undo invokes shared editor callback exactly once")
        first.menuDidClose(historyMenu)
        first.expand(); verifySmoke(first.enteredExpanded == first.panel.frame.contains(NSEvent.mouseLocation),"Expansion tracks actual pointer position")
        var monitored = configuration
        monitored.deck = [DeckItem(title:"Clock",profileID:"default",layer:.normal,input:0,directAction:Mapping(layer:.normal,input:0,action:.disabled),monitor:.clock)]
        first.update(monitored,enabled:false)
        verifySmoke(first.emptyCells.values.first.map { !$0.isHidden && $0.isEmptyDropTarget && !$0.showsEmptyDropVisual && $0.receiveFile != nil } == true,"Empty Deck slots remain invisible drop targets in run mode")
        verifySmoke(first.monitorTimer != nil && first.cells.values.first?.0.title.contains(":") == true,"Visible clock updates without hardware monitoring")
        let occupiedID = monitored.deck[0].id
        let beforeMoveFrame = first.panel.frame
        monitored.deck[0].slot = 8
        first.update(monitored,enabled:false)
        verifySmoke(first.cells[occupiedID]!.0.superview!.frame.minX == 2*72 && first.cells[occupiedID]!.0.superview!.frame.minY == 70,"Live sparse slot matches Windows geometry")
        verifySmoke(first.panel.frame == beforeMoveFrame,"Moving an item never resets panel size or position")
        verifySmoke(first.emptyCells[0] != nil && first.emptyCells[8] == nil,"Live empty slots never cover assigned cells")
        let normalFrame = first.panel.frame, savedFrame = first.layoutState.expanded
        first.toggleMaximize(); first.saveFrames()
        verifySmoke(first.layoutState.expanded == savedFrame,"Temporary maximize never overwrites saved bounds")
        first.toggleMaximize(); verifySmoke(first.panel.frame == normalFrame,"Deck maximize restores exact previous frame")
        first.windowWillStartLiveResize(Notification(name:NSWindow.willStartLiveResizeNotification,object:first.panel))
        let start = first.panel.frame.size
        let vertical = first.windowWillResize(first.panel,to:NSSize(width:start.width,height:start.height+40))
        verifySmoke(first.resizeByWidth == false && vertical.width > start.width,"Vertical drag selects height as resize driver")
        let verticalNext = first.windowWillResize(first.panel,to:NSSize(width:vertical.width+90,height:vertical.height+10))
        verifySmoke(first.resizeByWidth == false && verticalNext.width < vertical.width+90,"Resize driver remains locked for session")
        first.windowDidEndLiveResize(Notification(name:NSWindow.didEndLiveResizeNotification,object:first.panel))
        for left in [true,false] { for bottom in [true,false] {
            let resized = DeckResizeGrip.frame(start:normalFrame,size:vertical,left:left,bottom:bottom)
            verifySmoke((left ? abs(resized.maxX-normalFrame.maxX) < 0.001 : abs(resized.minX-normalFrame.minX) < 0.001) &&
                        (bottom ? abs(resized.maxY-normalFrame.maxY) < 0.001 : abs(resized.minY-normalFrame.minY) < 0.001),
                        "Corner resize preserves the opposite corner")
        } }
        let finderIcon = Studio.deckImage(Mapping(layer:.normal,input:0,action:.launch,application:"/System/Library/CoreServices/Finder.app"))
        for command in [SystemCommand.finderExisting,.finderNewWindow] {
            let image = Studio.deckImage(Mapping(layer:.normal,input:0,action:.system,payload:command.rawValue))
            verifySmoke(image != nil && image?.isTemplate == false && image?.tiffRepresentation == finderIcon?.tiffRepresentation,"Finder actions use native Finder artwork")
        }
        let padded = NSImage(size:NSSize(width:100,height:100))
        padded.lockFocus(); NSColor.red.setFill(); NSRect(x:20,y:20,width:60,height:60).fill(); padded.unlockFocus()
        let fitted = Studio.fittedAppArtwork(padded)
        verifySmoke(abs(fitted.size.width/fitted.size.height-1) < 0.01 && fitted.size.width < 150,"App artwork trims transparent padding")
        let artworkTile = DeckTile(frame:NSRect(x:0,y:0,width:54,height:52)); artworkTile.image = fitted
        verifySmoke(!artworkTile.showsTileChrome && artworkTile.imageRect(for:fitted).height == 52,"Artwork fills tile without button background")
        first.select(occupiedID,modifiers:[])
        first.cells[occupiedID]!.0.lastClickModifiers = []
        first.clicked(first.cells[occupiedID]!.0)
        verifySmoke(first.selectedIDs.isEmpty && !first.cells[occupiedID]!.0.selected,"Ordinary Deck click leaves no selection outline")

        verifySmoke(first.emptyCells.values.allSatisfy { !$0.isHidden && !$0.showsEmptyDropVisual },"Run mode keeps empty slots available without painting placeholders")
        let dragTile = first.cells[occupiedID]!.0
        verifySmoke(dragTile.dragIDs?() == [occupiedID] && first.selectedIDs.isEmpty,"Run-mode drag does not leave selection color")
        dragTile.dragStateChanged?(true)
        verifySmoke(first.emptyCells.values.allSatisfy { !$0.isHidden && $0.showsEmptyDropVisual },"Run-mode drag exposes empty destinations")
        dragTile.dragStateChanged?(false)
        verifySmoke(first.emptyCells.values.allSatisfy { !$0.isHidden && !$0.showsEmptyDropVisual },"Drag finish conceals empty destinations without disabling drops")
        first.multiSelect.performClick(nil)
        verifySmoke(first.emptyCells.values.allSatisfy { !$0.showsEmptyDropVisual },"Edit mode also conceals idle empty destinations")
        var invoked = false; first.execute = { _ in invoked = true }
        first.setEnabled(true); first.clicked(first.cells[occupiedID]!.0)
        verifySmoke(!invoked && first.cells[occupiedID]!.0.selected && first.cells[occupiedID]!.0.allowsReordering,"Editing selects without executing")
        first.multiSelect.performClick(nil); first.clicked(first.cells[occupiedID]!.0)
        verifySmoke(!invoked && first.status.stringValue.contains("Action"),"Disabled Deck tile explains why nothing ran")
        first.configuration.deck[0].directAction = Mapping(layer:.normal,input:0,action:.launch,application:"/System/Applications/Calculator.app")
        first.clicked(first.cells[occupiedID]!.0)
        verifySmoke(invoked && first.status.stringValue.contains("開いています") && !first.cells[occupiedID]!.0.selected && first.cells[occupiedID]!.0.allowsReordering,"App launch clicks give immediate feedback and execute")
        invoked = false; first.cells[occupiedID]!.0.lastClickModifiers = [.shift]
        first.clicked(first.cells[occupiedID]!.0)
        verifySmoke(!first.isSelectingMultiple && !invoked,"Modifier click cannot silently enter editing or execute")
        first.cells[occupiedID]!.0.lastClickModifiers = []
        first.setEnabled(false)
        let unchanged = first.cells[occupiedID]!.0
        monitored.deck[0].title = "Renamed"
        first.update(monitored,enabled:false)
        verifySmoke(first.cells[occupiedID]!.0 === unchanged,"Single edit reuses live tile")
        DeckFilePreview.verifyLifecycle(anchor:unchanged)
        first.refreshMonitors()
        let sharedSamples = first.monitors.sampleCount
        var secondMonitored = monitored; var secondClock = monitored.deck[0]; secondClock.id = UUID().uuidString; secondClock.layoutID = other.id; secondMonitored.deck.append(secondClock)
        second.update(secondMonitored,enabled:false)
        verifySmoke(first.monitors.subscriberCount == 2 && first.monitors.sampleCount == sharedSamples,"Visible Decks reuse shared clock sample")
        var large = monitored; var largeLayout = large.deckLayout("default"); largeLayout.columns = 18; largeLayout.rows = 18; large.setDeckLayout(largeLayout)
        first.update(large,enabled:false); first.panel.contentView?.layoutSubtreeIfNeeded()
        verifySmoke(first.cells.count + first.emptyCells.count == 324,"18 by 18 retains all visible cells")
        verifySmoke(first.grid.frame.width <= first.scroll.contentSize.width+1 && first.grid.frame.height <= first.scroll.contentSize.height+1,"Large Deck fits without clipping or scroll")
        let started = ProcessInfo.processInfo.systemUptime
        for _ in 0..<20 { first.update(large,enabled:false) }
        print("MEASURE: 20 native Deck updates (324 cells): \((ProcessInfo.processInfo.systemUptime-started)*1000) ms; not a physical frame-rate measurement")
        first.update(monitored,enabled:false)
        let previewRoot = URL(fileURLWithPath:CommandLine.arguments[2]).deletingPathExtension().path
        for (name,appearance,motion,contrast) in [("light",NSAppearance.Name.aqua,false,false),("dark",.darkAqua,false,false),("contrast",.darkAqua,false,true),("reduced-motion",.aqua,true,false)] {
            Studio.verificationAccessibility = (motion,contrast); first.panel.appearance = NSAppearance(named:appearance)
            first.panel.contentView?.needsDisplay = true
            saveSmokePreview(first.panel.contentView!,to:previewRoot+"-deck-"+name+".png")
        }
        let gallery = StudioSurface(frame:NSRect(x:0,y:0,width:650,height:270))
        for (index,kind) in DeckMonitor.allCases.enumerated() {
            let tile = DeckTile(frame:NSRect(x:12+CGFloat(index%9)*70,y:12+CGFloat(index/9)*120,width:60,height:60))
            tile.monitorKind = kind; tile.monitorCaption = kind.caption
            tile.title = [.clock,.timer].contains(kind) ? "12:34" : (kind == .date ? "11/04" : ([.wifi,.bluetooth,.microphone,.autoExtract].contains(kind) ? "ON" : "62%"))
            tile.monitorSamples = [12,32,25,54,35,62]; gallery.addSubview(tile)
            let label = Studio.label(kind.title,9,false); label.frame = NSRect(x:tile.frame.minX-4,y:tile.frame.maxY+6,width:68,height:30); gallery.addSubview(label)
        }
        for (name,appearance) in [("dark",NSAppearance.Name.darkAqua),("light",.aqua)] {
            gallery.appearance = NSAppearance(named:appearance)
            saveSmokePreview(gallery,to:previewRoot+"-monitor-gallery-"+name+".png")
        }
        verifySmoke(DeckMonitor.allCases.contains(.brightness),"Brightness is available in monitor catalog")
        verifySmoke((try? JSONDecoder().decode(DeckMonitor.self,from:JSONEncoder().encode(DeckMonitor.brightness))) == .brightness,"Brightness persists through export/import")
        var brightness = monitored
        brightness.deck[0].monitor = .brightness
        first.update(brightness, enabled: true)
        let brightnessTile = first.cells[occupiedID]!.0
        var wheelCommands: [SystemCommand] = []
        first.performAction = { mapping in
            if mapping.action == .system, let command = SystemCommand(rawValue: mapping.payload) {
                wheelCommands.append(command)
            }
        }
        let wheelDeltas: [Int32] = [1, -1, 1]
        for (index, delta) in wheelDeltas.enumerated() {
            if index == 2 { first.setEnabled(false) }
            let scrollEvent = CGEvent(scrollWheelEvent2Source: nil, units: .line, wheelCount: 1, wheel1: delta, wheel2: 0, wheel3: 0)!
            scrollEvent.timestamp = UInt64(index + 1) * 1_000_000_000
            brightnessTile.scrollWheel(with: NSEvent(cgEvent: scrollEvent)!)
        }
        verifySmoke(wheelCommands == [.brightnessUp, .brightnessDown] && first.monitorPopover == nil,
                    "Live Deck brightness wheel routes both directions without opening controls or running while disabled")
        first.update(monitored, enabled: false)
        verifySmoke(brightnessTile.wheelAction == nil, "Changing to a read-only monitor clears its wheel handler")
        var selectionFixture = monitored
        var adjacent = monitored.deck[0]
        adjacent.id = UUID().uuidString
        adjacent.slot = 9
        selectionFixture.deck.append(adjacent)
        first.update(selectionFixture, enabled: false)
        first.panel.contentView?.layoutSubtreeIfNeeded()
        first.showTitleHint(for: first.multiSelect)
        verifySmoke(first.titleHint.superview === first.hover &&
                    first.titleHint.stringValue == first.multiSelect.hint &&
                    first.hover.bounds.contains(first.titleHint.frame) &&
                    first.titleHint.hitTest(.zero) == nil,
                    "Deck title hint is visible within the nonactivating panel and passes clicks through")
        first.clearTitleHint()
        first.multiSelect.performClick(nil)
        verifySmoke(first.isSelectingMultiple, "Deck title button turns on multi-select")
        let selectedTile = first.cells[occupiedID]!.0
        let adjacentTile = first.cells[adjacent.id]!.0
        first.clicked(selectedTile)
        first.clicked(adjacentTile)
        verifySmoke(Set(selectedTile.dragIDs?() ?? []) == [occupiedID, adjacent.id],
                    "Deck title multi-select carries both tiles in the drag")
        first.clicked(adjacentTile)
        verifySmoke(first.selectedIDs == [occupiedID], "Deck title multi-select toggles the clicked tile")
        first.multiSelect.performClick(nil)
        verifySmoke(first.multiSelect.state == .off && first.selectedIDs.isEmpty,
                    "Turning off multi-select clears selections")
        Studio.verificationAccessibility = nil
        first.hide(); second.hide()
        verifySmoke(first.monitors.subscriberCount == 0,"Hiding last Deck releases all shared monitor requests")
        verifySmoke(first.monitorTimer == nil,"Hidden Deck releases monitoring timer")
        if CommandLine.arguments.count > 2 {
            DeckSettingsDrawer.verifyBehaviorUI(previewPath:URL(fileURLWithPath:CommandLine.arguments[2]).deletingPathExtension().path + "-deck-behavior.png")
        }
        print("PASS: independent Decks, collapse bounds, tab position, pin, hide, pointer-entry guard and visible-only monitors")
    }
    var inspectAction: ((Mapping) -> Void)?
    private func contextMenu(_ id: String) -> NSMenu {
        let menu = NSMenu(); menu.delegate = self; menu.autoenablesItems = false
        for (title,action,tag) in [("直前の編集を元に戻す",#selector(undoDeckEdit),40),("編集をやり直す",#selector(redoDeckEdit),41)] {
            let command = NSMenuItem(title:title,action:action,keyEquivalent:""); command.target = self; command.tag = tag; menu.addItem(command)
        }
        menu.addItem(.separator())
        for (title, tag) in [("動作の問題を確認…", 42),("名前を変更…", 0), ("前へ移動", -1), ("後へ移動", 1), ("Deckから削除", 2),("色：青",10),("色：緑",11),("色：橙",12),("色をリセット",13),("色を選択…",14),("アイコンを選択…",20),("アイコンを戻す",21),("登録ファイルをコピー",30),("Finderで表示",31),("ファイルをプレビュー",32),("クリップボードのファイルを登録",33)] {
            let item = NSMenuItem(title: title, action: #selector(contextAction(_:)), keyEquivalent: "")
            item.target = self; item.representedObject = id; item.tag = tag
            menu.addItem(item)
        }
        menu.addItem(.separator())
        for (title,tag) in [("1分タイマー",101),("3分タイマー",103),("10分タイマー",110),("30分タイマー",130),("時間を指定…",190),("タイマーを停止",191)] {
            let item = NSMenuItem(title:title,action:#selector(contextAction(_:)),keyEquivalent:""); item.target = self; item.tag = tag; item.representedObject = id; item.isHidden = true; menu.addItem(item)
        }
        return menu
    }
    func menuWillOpen(_ menu: NSMenu) {
        openMenus += 1
        if let titles = historyTitles?() { menu.item(withTag:40)?.title = titles.0; menu.item(withTag:41)?.title = titles.1 }
        menu.item(withTag:40)?.isEnabled = canUndoEdit?() ?? false
        menu.item(withTag:41)?.isEnabled = canRedoEdit?() ?? false
        for command in menu.items where (30...32).contains(command.tag) {
            let mapping = configuration.deck.first { $0.id == command.representedObject as? String }.flatMap { configuration.mapping(for:$0) }
            command.isHidden = mapping.map { $0.action != .file && $0.action != .launch } ?? true
        }
        if let command = menu.item(withTag:42) {
            command.isEnabled = configuration.deck.first { $0.id == command.representedObject as? String }.flatMap { configuration.mapping(for:$0) } != nil
        }
        for command in menu.items where command.tag == 33 { command.isEnabled = DeckFilePreview.clipboardFile() != nil }
        for command in menu.items where command.tag >= 100 {
            command.isHidden = !configuration.deck.contains { $0.id == command.representedObject as? String && $0.monitor == .timer }
        }
    }
    func menuDidClose(_ menu: NSMenu) { openMenus = max(0,openMenus-1) }
    private func adjustMonitor(_ kind: DeckMonitor, delta: CGFloat) {
        guard inputEnabled else {
            showNotice("調整するにはRELYRの入力を有効にしてください。")
            return
        }
        let command: SystemCommand
        switch kind {
        case .brightness: command = delta > 0 ? .brightnessUp : .brightnessDown
        case .volume: command = delta > 0 ? .volumeUp : .volumeDown
        case .microphone:
            let audio = SystemAudioBackend(input: true)
            if let value = audio.volume() {
                let step: Float = delta > 0 ? 0.0625 : -0.0625
                if !audio.setVolume(min(1, max(0, value + step))) {
                    showNotice("このマイクの音量は変更できません。")
                }
            }
            refreshMonitors()
            return
        default: return
        }
        performAction?(Mapping(layer: .normal, input: 0, action: .system, payload: command.rawValue))
        refreshMonitors()
    }
    private func showAudioControl(_ kind: DeckMonitor, anchor: NSView) {
        monitorPopover?.close(); adjustingMonitor = kind
        let body = StudioSurface(frame:NSRect(x:0,y:0,width:260,height:108))
        let title = Studio.label(kind.title,13,true); title.frame = NSRect(x:12,y:8,width:230,height:24); body.addSubview(title)
        let audio = SystemAudioBackend(input:kind == .microphone)
        let slider = NSSlider(value:Double(audio.volume() ?? 0),minValue:0,maxValue:1,target:self,action:#selector(audioSliderChanged(_:)))
        slider.isContinuous = false; slider.isEnabled = inputEnabled && audio.volume() != nil; slider.frame = NSRect(x:12,y:39,width:236,height:24); slider.setAccessibilityLabel(kind.title); body.addSubview(slider)
        let mute = NSButton(title:audio.muted() == true ? "消音を解除" : "消音",target:self,action:#selector(audioMuteChanged)); mute.isBordered = false; mute.frame = NSRect(x:12,y:74,width:110,height:24); mute.isEnabled = inputEnabled; body.addSubview(mute)
        let close = NSButton(title:"閉じる",target:self,action:#selector(closeAudioControl)); close.isBordered = false; close.frame = NSRect(x:146,y:74,width:100,height:24); body.addSubview(close)
        let controller = NSViewController(); controller.view = body
        let popover = NSPopover(); popover.behavior = .transient; popover.contentViewController = controller; monitorPopover = popover; popover.show(relativeTo:anchor.bounds,of:anchor,preferredEdge:.maxX)
    }
    @objc private func audioSliderChanged(_ sender: NSSlider) {
        guard inputEnabled,let kind = adjustingMonitor else { return }
        if !SystemAudioBackend(input:kind == .microphone).setVolume(Float(sender.doubleValue)) { status.stringValue = "この機器の音量はスライダーでは変更できません。ホイールを使用してください。" }
        refreshMonitors()
    }
    @objc private func audioMuteChanged() {
        guard inputEnabled,let kind = adjustingMonitor else { return }
        performAction?(Mapping(layer:.normal,input:0,action:.system,payload:(kind == .microphone ? SystemCommand.micMute : .mute).rawValue)); monitorPopover?.close(); refreshMonitors()
    }
    @objc private func closeAudioControl() { monitorPopover?.close(); monitorPopover = nil }
    private func refreshSelection() { for (id,cell) in cells { cell.0.selected = selectedIDs.contains(id) } }
    private func select(_ id: String, modifiers: NSEvent.ModifierFlags, toggle: Bool = false) {
        let additive = modifiers.contains(.command) || modifiers.contains(.control)
        if modifiers.contains(.shift), let anchor = anchorID, let a = order.firstIndex(of:anchor), let b = order.firstIndex(of:id) {
            let range = Set(order[min(a,b)...max(a,b)])
            selectedIDs = additive ? selectedIDs.union(range) : range
        } else if additive || toggle {
            if selectedIDs.contains(id) { selectedIDs.remove(id) } else { selectedIDs.insert(id) }
            anchorID = id
        } else { selectedIDs = [id]; anchorID = id }
        refreshSelection()
    }
    @objc private func clicked(_ sender: NSButton) {
        guard let id = sender.identifier?.rawValue else { return }
        let modifiers = (sender as? DeckTile)?.lastClickModifiers ?? []
        if isSelectingMultiple { select(id, modifiers: modifiers, toggle: true); return }
        guard modifiers.intersection([.command,.control,.shift]).isEmpty else {
            showNotice("複数選択するには丸いチェックを押してください。")
            return
        }
        selectedIDs.removeAll(); anchorID = nil; refreshSelection()
        if let kind = configuration.deck.first(where:{ $0.id == id })?.monitor {
            if kind == .volume || kind == .microphone { showAudioControl(kind,anchor:sender); return }
            if kind == .timer { status.stringValue = "タイマーは右クリックから時間を選んでください。"; return }
        }
        if !inputEnabled { showNotice("入力停止中：RELYRの入力を有効にしてください"); return }
        if let item = configuration.deck.first(where:{ $0.id == id }),let mapping = configuration.mapping(for:item),mapping.action == .file,DeckFilePreview.shared.show(URL(fileURLWithPath:mapping.application),anchor:sender,owner:id) { return }
        guard let item = configuration.deck.first(where:{ $0.id == id }), let mapping = configuration.mapping(for:item), mapping.action != .disabled else {
            showNotice("このマスには実行するActionがありません"); return
        }
        (sender as? StudioButton)?.animateAssignmentCommit()
        let behavior = configuration.deckAfterAction ?? .stay
        let actionNotice = mapping.action == .launch ? "\(item.title)を開いています…" : "\(item.title)を実行しています…"
        showNotice(layoutState.pinned && behavior != .stay ? actionNotice + " 固定中のためDeckは表示を続けます" : actionNotice)
        execute?(id)
        scheduleDismiss(behavior,delay:0.24,requiresOutside:false)
    }
    @objc private func contextAction(_ sender: NSMenuItem) {
        if let slot = sender.representedObject as? Int,sender.tag == 33 { if let url = DeckFilePreview.clipboardFile() { _ = receiveEmptyFile?(url,slot) }; return }
        guard let id = sender.representedObject as? String else { return }
        if sender.tag == 42 {
            if let item = configuration.deck.first(where: { $0.id == id }), let mapping = configuration.mapping(for:item) { inspectAction?(mapping) }
            return
        }
        if !selectedIDs.contains(id) { select(id,modifiers:[]) }
        let selected = order.filter { selectedIDs.contains($0) }
        if sender.tag >= 100,configuration.deck.first(where:{ $0.id == id })?.monitor == .timer {
            if sender.tag == 191 { DeckTimerService.shared.cancel() }
            else if sender.tag == 190 {
                let alert = NSAlert(); alert.messageText = "タイマー（分）"; alert.informativeText = "1〜1440分を指定してください。"
                let value = NSTextField(string:"5"); value.frame = NSRect(x:0,y:0,width:180,height:28); alert.accessoryView = value; alert.addButton(withTitle:"開始"); alert.addButton(withTitle:"キャンセル")
                alert.beginSheetModal(for:panel) { [weak self] response in
                    guard response == .alertFirstButtonReturn,let minutes = Double(value.stringValue),minutes.isFinite,(1...1440).contains(minutes) else { return }
                    DeckTimerService.shared.start(minutes:minutes); self?.refreshMonitors()
                }
            } else { DeckTimerService.shared.start(minutes:Double(sender.tag-100)) }
            refreshMonitors(); return
        }
        if sender.tag == 33 { if let url = DeckFilePreview.clipboardFile() { _ = receiveFile?(url,id) }; return }
        if (30...32).contains(sender.tag) {
            guard let item = configuration.deck.first(where:{ $0.id == id }),let mapping = configuration.mapping(for:item),[.file,.launch].contains(mapping.action) else { return }
            let url = URL(fileURLWithPath:mapping.application)
            guard FileManager.default.fileExists(atPath:url.path) else { status.stringValue = "登録ファイルが見つかりません。"; return }
            if sender.tag == 30 { NSPasteboard.general.clearContents(); NSPasteboard.general.writeObjects([url as NSURL]) }
            else if sender.tag == 31 { NSWorkspace.shared.activateFileViewerSelecting([url]) }
            else if let button = cells[id]?.0 { _ = DeckFilePreview.shared.show(url,anchor:button,owner:id) }; return
        }
        switch sender.tag {
        case 0: requestRename?(id)
        case 2: editMany?(selected,.remove)
        case 14: requestColor?(selected)
        case 20: requestIcon?(selected)
        case 21: editMany?(selected,.icon(nil))
        case 10...13:
            editMany?(selected,.color([10:"245A86",11:"187964",12:"A75F27"][sender.tag]))
        default:
            if let index = order.firstIndex(of: id), order.indices.contains(index + sender.tag),
               let target = configuration.deckPositions(layoutID)[order[index + sender.tag]] {
                _ = moveToSlot?(selected, id, target)
            }
        }
    }
}
