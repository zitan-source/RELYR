import AppKit

// Detect physical wheel movement independently of macOS natural scrolling.
// The scroll-output reversal preference must not reverse button identity.
private final class MouseDetectionButton: StudioButton {
    var hoverChanged: ((Bool) -> Void)?
    private var detectionTracking: NSTrackingArea?
    override func updateTrackingAreas() {
        super.updateTrackingAreas()
        if let area = detectionTracking { removeTrackingArea(area) }
        let area = NSTrackingArea(rect:.zero,options:[.mouseEnteredAndExited,.activeAlways,.inVisibleRect],owner:self,userInfo:nil)
        addTrackingArea(area); detectionTracking = area
    }
    override func mouseEntered(with event: NSEvent) { super.mouseEntered(with:event); hoverChanged?(true) }
    override func mouseExited(with event: NSEvent) { super.mouseExited(with:event); hoverChanged?(false) }
}

enum MouseLayoutInput {
    static func wheel(_ event: NSEvent) -> UInt16? {
        guard !event.hasPreciseScrollingDeltas, event.momentumPhase.isEmpty else { return nil }
        guard let cgEvent = event.cgEvent else { return nil }
        return NativeEvents.scrollInput(cgEvent,invertedFromDevice:event.isDirectionInvertedFromDevice)
    }
    static func code(_ source: Int, configuration: Configuration, deviceID: String? = nil) -> UInt16? {
        source >= 270 ? UInt16(exactly:source) : configuration.mouseInput(Int64(source), deviceID: deviceID)
    }
    static func label(_ source: Int) -> String {
        switch source {
        case 0:return "左クリック"
        case 1:return "右クリック"
        case 2:return "中クリック"
        case 270:return "ホイール ↑"
        case 271:return "ホイール ↓"
        case 272:return "チルト ←"
        case 273:return "チルト →"
        case 340:return "モード切替"
        default:return "ボタン \(source)"
        }
    }
    static let sources = Array(0...31) + Array(270...273) + [340]
    static let pasteboard = NSPasteboard.PasteboardType("io.relyr.mouse-layout-source")
}

final class MouseSilhouette: NSView {
    override var isFlipped: Bool { true }
    override func hitTest(_ point: NSPoint) -> NSView? { nil }
    override func draw(_ dirtyRect: NSRect) {
        let body = NSRect(x:bounds.width*0.26,y:bounds.height*0.035,width:bounds.width*0.48,height:bounds.height*0.93)
        let shape = NSBezierPath(roundedRect:body,xRadius:body.width*0.46,yRadius:body.width*0.46)
        Studio.key.withAlphaComponent(0.20).setFill(); shape.fill()
        Studio.muted.withAlphaComponent(0.22).setStroke(); shape.lineWidth = 1; shape.stroke()
        let seam = NSBezierPath(); seam.move(to:NSPoint(x:body.midX,y:body.minY)); seam.line(to:NSPoint(x:body.midX,y:body.minY+body.height*0.38)); seam.stroke()
    }
}

private final class MouseLayoutTile: NSView {
    let source: Int
    var palette = false
    var highlighted = false { didSet { needsDisplay = true } }
    var chosen = false { didSet { needsDisplay = true } }
    var changed: ((NSRect) -> Void)?
    var selected: (() -> Void)?
    var paletteDrag: ((NSPoint,Bool) -> Void)?
    init(source: Int) { self.source = source; super.init(frame:.zero); toolTip = MouseLayoutInput.label(source); setAccessibilityElement(true); setAccessibilityRole(.button); setAccessibilityLabel(MouseLayoutInput.label(source)) }
    required init?(coder:NSCoder) { fatalError() }
    override var isFlipped: Bool { true }
    override var acceptsFirstResponder: Bool { true }
    override func draw(_ dirtyRect:NSRect) {
        let shape = NSBezierPath(roundedRect:bounds.insetBy(dx:1,dy:1),xRadius:10,yRadius:10)
        (highlighted ? Studio.accent : Studio.key).setFill(); shape.fill()
        (chosen ? Studio.accent : Studio.muted.withAlphaComponent(0.35)).setStroke(); shape.lineWidth = chosen ? 2 : 1; shape.stroke()
        let style = NSMutableParagraphStyle(); style.alignment = .center
        (MouseLayoutInput.label(source) as NSString).draw(in:NSRect(x:5,y:max(4,(bounds.height-18)/2),width:bounds.width-10,height:20),withAttributes:[.font:NSFont.systemFont(ofSize:12,weight:.medium),.foregroundColor:Studio.text,.paragraphStyle:style])
        if !palette {
            Studio.muted.setStroke()
            for offset in [CGFloat(5),9] { let grip = NSBezierPath(); grip.move(to:NSPoint(x:bounds.width-offset-5,y:bounds.height-4)); grip.line(to:NSPoint(x:bounds.width-4,y:bounds.height-offset-5)); grip.stroke() }
        }
    }
    override func mouseDown(with event:NSEvent) {
        selected?()
        if palette { startPaletteDrag(event); return }
        guard let parent = superview, let window = window else { return }
        let start = frame, point = parent.convert(event.locationInWindow,from:nil)
        let local = convert(event.locationInWindow,from:nil)
        let resize = local.x > bounds.width-22 && local.y > bounds.height-22
        while let next = window.nextEvent(matching:[.leftMouseDragged,.leftMouseUp]) {
            if next.type == .leftMouseUp { break }
            let now = parent.convert(next.locationInWindow,from:nil), dx = now.x-point.x, dy = now.y-point.y
            if resize {
                let delta = abs(dx) >= abs(dy) ? dx : dy
                let maximum = floor(min(600-start.minX,400-start.minY)/12)*12
                let side = min(maximum,max(96,MouseLayoutItem.snapped(start.width+delta)))
                frame.size = NSSize(width:side,height:side)
            } else {
                frame.origin = NSPoint(x:min(600-start.width,max(0,MouseLayoutItem.snapped(start.minX+dx))),y:min(floor((400-start.height)/12)*12,max(0,MouseLayoutItem.snapped(start.minY+dy))))
            }
        }
        changed?(frame)
    }
    private func startPaletteDrag(_ event:NSEvent) {
        guard let window = window else { return }
        var moved = false
        while let next = window.nextEvent(matching:[.leftMouseDragged,.leftMouseUp]) {
            if next.type == .leftMouseUp {
                if moved { paletteDrag?(next.locationInWindow,true) }
                break
            }
            moved = true; paletteDrag?(next.locationInWindow,false)
        }
    }

}

private final class MouseLayoutSurface: NSView {
    var dropped: ((Int,NSPoint) -> Void)?
    private var dropPoint: NSPoint?
    override var isFlipped: Bool { true }
    override init(frame:NSRect) { super.init(frame:frame); registerForDraggedTypes([MouseLayoutInput.pasteboard]) }
    required init?(coder:NSCoder) { fatalError() }
    func showPreview(_ point: NSPoint?) { dropPoint = point; needsDisplay = true }
    override func draggingEntered(_ sender:NSDraggingInfo) -> NSDragOperation { draggingUpdated(sender) }
    override func draggingUpdated(_ sender:NSDraggingInfo) -> NSDragOperation {
        guard sender.draggingPasteboard.string(forType:MouseLayoutInput.pasteboard).flatMap(Int.init) != nil else { return [] }
        dropPoint = convert(sender.draggingLocation,from:nil); needsDisplay = true; return .copy
    }
    override func draggingExited(_ sender:NSDraggingInfo?) { dropPoint = nil; needsDisplay = true }
    override func performDragOperation(_ sender:NSDraggingInfo) -> Bool {
        defer { dropPoint = nil; needsDisplay = true }
        guard let source = sender.draggingPasteboard.string(forType:MouseLayoutInput.pasteboard).flatMap(Int.init), MouseLayoutInput.sources.contains(source) else { return false }
        dropped?(source,convert(sender.draggingLocation,from:nil)); return true
    }
    override func draw(_ dirtyRect:NSRect) {
        Studio.background.setFill(); bounds.fill()
        Studio.muted.withAlphaComponent(0.16).setFill()
        for x in stride(from:CGFloat(0),through:600,by:12) {
            for y in stride(from:CGFloat(0),through:400,by:12) { NSBezierPath(ovalIn:NSRect(x:x,y:y,width:1,height:1)).fill() }
        }
        if let point = dropPoint {
            Studio.accent.setStroke()
            let preview = NSBezierPath(roundedRect:NSRect(x:min(504,max(0,MouseLayoutItem.snapped(point.x-48))),y:min(300,max(0,MouseLayoutItem.snapped(point.y-48))),width:96,height:96),xRadius:10,yRadius:10)
            preview.lineWidth = 2; preview.stroke()
        }
    }
}

final class MouseLayoutEditor: NSObject {
    let panel: NSPanel
    private let configuration: Configuration
    private var layout: MouseButtonLayout
    private let templates: [MouseButtonLayout]
    private var monitor: Any?
    private var mxMaster4Observer: UUID?
    private var detecting = false
    private var flashGeneration = 0
    private var tiles: [Int:MouseLayoutTile] = [:]
    private var paletteTiles: [Int:MouseLayoutTile] = [:]
    private var selectedSource: Int?
    private let canvas = MouseLayoutSurface(frame:NSRect(x:0,y:0,width:600,height:400))
    private let paletteScroll = NSScrollView()
    private let picker = NSPopUpButton()
    private let hint = NSTextField(wrappingLabelWithString:"押したボタンを確認して、右の一覧からマウスへドラッグ。12ptのグリッドに揃います。右下の角で正方形のままサイズを調整できます。")
    private var detectButton: MouseDetectionButton!
    private var capturedButtons = Set<Int>()
    private var removeButton: StudioButton!
    var save: ((MouseButtonLayout) -> Bool)?
    init(configuration: Configuration, deviceID: String? = nil, deviceName: String = "すべてのマウス（共通）") {
        self.configuration = configuration
        let buttons = configuration.layerButtons(for: deviceID)
        let standard = MouseButtonLayout.standard(0,forward:buttons.forward,back:buttons.back)
        templates = [MouseButtonLayout(name:"標準配置",items:standard.items)] + (configuration.mouseButtonLayouts ?? [])
        let current = configuration.mouseButtonLayout(for: deviceID) ?? standard
        layout = MouseButtonLayout(name:String(deviceName.prefix(30)),items:current.items.map { $0.squared })
        panel = NSPanel(contentRect:NSRect(x:0,y:0,width:960,height:620),styleMask:[.titled],backing:.buffered,defer:false)
        super.init()
        panel.title = "マウスボタンレイアウト"; panel.isReleasedWhenClosed = false
        let root = StudioSurface(frame:NSRect(x:0,y:0,width:960,height:620)); panel.contentView = root
        let title = Studio.label("マウスボタンレイアウト",20,true); title.frame = NSRect(x:24,y:20,width:500,height:30); root.addSubview(title)
        let deviceLabel = Studio.label(deviceName,14)
        deviceLabel.frame = NSRect(x:24,y:62,width:420,height:30)
        deviceLabel.toolTip = deviceName
        root.addSubview(deviceLabel)
        picker.frame = NSRect(x:452,y:62,width:226,height:30)
        picker.addItems(withTitles:["ひな形を読み込む…"] + templates.map { $0.name })
        picker.setAccessibilityLabel("ボタン配置のひな形")
        picker.target = self
        picker.action = #selector(loadTemplate)
        root.addSubview(picker)
        detectButton = MouseDetectionButton("ここに置いて検出",symbol:"scope") { }
        detectButton.hoverChanged = { [weak self] inside in self?.setDetecting(inside) }
        detectButton.frame = NSRect(x:710,y:62,width:226,height:34); root.addSubview(detectButton)
        hint.frame = NSRect(x:24,y:106,width:912,height:38); hint.font = .systemFont(ofSize:12); hint.textColor = Studio.muted; root.addSubview(hint)
        canvas.frame.origin = NSPoint(x:24,y:150); root.addSubview(canvas)
        let silhouette = MouseSilhouette(frame:canvas.bounds); canvas.addSubview(silhouette)
        let list = StudioSurface(frame:NSRect(x:0,y:0,width:280,height:CGFloat(MouseLayoutInput.sources.count)*56))
        paletteScroll.frame = NSRect(x:650,y:150,width:286,height:400); paletteScroll.hasVerticalScroller = true; paletteScroll.drawsBackground = false; paletteScroll.documentView = list; root.addSubview(paletteScroll)
        for (row,source) in MouseLayoutInput.sources.enumerated() {
            let tile = MouseLayoutTile(source:source); tile.palette = true; tile.frame = NSRect(x:8,y:CGFloat(row)*56,width:252,height:48)
            tile.toolTip = MouseLayoutInput.code(source,configuration:configuration,deviceID:deviceID).map { configuration.inputDetail($0,deviceID:deviceID) }
            tile.paletteDrag = { [weak self] location,finished in
                guard let self = self else { return }
                let point = self.canvas.convert(location,from:nil)
                let inside = self.canvas.bounds.contains(point)
                self.canvas.showPreview(!finished && inside ? point : nil)
                if finished && inside { self.canvas.dropped?(source,point) }
            }
            list.addSubview(tile); paletteTiles[source] = tile
        }
        removeButton = StudioButton("配置から外す",symbol:"trash") { [weak self] in
            guard let self = self, let source = self.selectedSource else { return }
            self.layout.items.removeAll { $0.source == source }; self.selectedSource = nil; self.render()
        }
        removeButton.frame = NSRect(x:24,y:572,width:150,height:32); root.addSubview(removeButton)
        let cancel = StudioButton("キャンセル") { [weak self] in self?.finish(false) }; cancel.frame = NSRect(x:690,y:572,width:116,height:32); root.addSubview(cancel)
        let done = StudioButton("保存",symbol:"checkmark") { [weak self] in self?.finish(true) }; done.fill = Studio.accent; done.frame = NSRect(x:820,y:572,width:116,height:32); root.addSubview(done)
        canvas.dropped = { [weak self] source,point in
            guard let self = self else { return }
            let item = MouseLayoutItem(source:source,x:point.x-48,y:point.y-48).squared
            self.layout.items.removeAll { $0.source == source }; self.layout.items.append(item); self.selectedSource = source; self.render()
        }
        render()
    }
    deinit { stopMonitoring() }
    func stopMonitoring() {
        if let monitor = monitor { NSEvent.removeMonitor(monitor) }; monitor = nil
        if let observer = mxMaster4Observer { MXMaster4HID.shared.remove(observer); mxMaster4Observer = nil }
    }
    func startMonitoring() {
        MXMaster4HID.shared.onError = { [weak self] message in self?.hint.stringValue = message }
        mxMaster4Observer = MXMaster4HID.shared.observe { [weak self] down in
            guard down, let self = self, self.detecting else { return }
            self.flash(340)
        }
        monitor = NSEvent.addLocalMonitorForEvents(matching:[.leftMouseDown,.leftMouseUp,.rightMouseDown,.rightMouseUp,.otherMouseDown,.otherMouseUp,.scrollWheel,.keyDown]) { [weak self] event in
            guard let self = self, event.window === self.panel else { return event }
            if [.leftMouseUp,.rightMouseUp,.otherMouseUp].contains(event.type), self.capturedButtons.remove(event.buttonNumber) != nil { return nil }
            let withinDetection = self.detectButton.bounds.contains(self.detectButton.convert(event.locationInWindow,from:nil))
            if event.type != .keyDown && withinDetection && !self.detecting { self.setDetecting(true) }
            guard self.detecting else { return event }
            if event.type == .keyDown {
                if event.keyCode == 53 { self.setDetecting(false) }
                else { self.hint.stringValue = "キー入力（番号 \(event.keyCode)）として届いています。Logi Options+でこのボタンを標準のマウスボタンに戻すか、キーボード側へ割り当ててください。" }
                return nil
            }
            let location = self.detectButton.convert(event.locationInWindow,from:nil)
            if !self.detectButton.bounds.contains(location) { self.setDetecting(false); return event }
            if event.type == .scrollWheel { if let code = MouseLayoutInput.wheel(event) { self.flash(Int(code)) }; return nil }
            if [.leftMouseDown,.rightMouseDown,.otherMouseDown].contains(event.type) { self.capturedButtons.insert(event.buttonNumber); self.flash(event.buttonNumber) }
            return nil
        }
    }
    private func flash(_ source:Int) {
        guard let row = paletteTiles[source] else { return }
        flashGeneration += 1; let generation = flashGeneration
        paletteTiles.values.forEach { $0.highlighted = false }; tiles.values.forEach { $0.highlighted = false }
        selectedSource = source
        paletteTiles.forEach { $0.value.chosen = $0.key == source }
        tiles.forEach { $0.value.chosen = $0.key == source }
        removeButton.isEnabled = tiles[source] != nil
        row.highlighted = true; tiles[source]?.highlighted = true
        row.scrollToVisible(row.bounds)
        hint.stringValue = "\(MouseLayoutInput.label(source))を検出しました。右の一覧から、このボタンをドラッグしてください。"
        DispatchQueue.main.asyncAfter(deadline:.now()+1.2) { [weak self] in
            guard let self = self, generation == self.flashGeneration else { return }
            self.paletteTiles.values.forEach { $0.highlighted = false }; self.tiles.values.forEach { $0.highlighted = false }
        }
    }
    private func setDetecting(_ value:Bool) {
        if value && !detecting { MXMaster4HID.shared.requestPermissionForCapture() }
        detecting = value; detectButton.title = value ? "検出中" : "ここに置いて検出"; detectButton.selected = value
        hint.stringValue = value ? "ここにポインタを置いたまま、ボタン・ホイール・左右チルトを操作してください。離れると配置編集に戻ります。" : "右の一覧からドラッグして配置。配置したボタンの右下の角でサイズを変更できます。"
    }
    @objc private func loadTemplate() {
        let selected = picker.indexOfSelectedItem - 1
        guard templates.indices.contains(selected) else { return }
        layout.items = templates[selected].items.map { $0.squared }
        selectedSource = nil
        picker.selectItem(at:0)
        render()
    }

    private func render() {
        tiles.values.forEach { $0.removeFromSuperview() }; tiles.removeAll()
        for item in layout.items {
            let tile = MouseLayoutTile(source:item.source); tile.frame = NSRect(x:item.x,y:item.y,width:item.width,height:item.height); tile.chosen = selectedSource == item.source
            tile.selected = { [weak self] in
                guard let self = self else { return }; self.selectedSource = item.source
                self.tiles.forEach { $0.value.chosen = $0.key == item.source }; self.removeButton.isEnabled = true
            }
            tile.changed = { [weak self] rect in
                guard let self = self, let slot = self.layout.items.firstIndex(where: { $0.source == item.source }) else { return }
                self.layout.items[slot] = MouseLayoutItem(source:item.source,x:rect.minX,y:rect.minY,width:rect.width,height:rect.height)
            }
            canvas.addSubview(tile); tiles[item.source] = tile
        }
        removeButton.isEnabled = selectedSource != nil
    }
    func verifyPresentation(previewPath:String) {
        verifySmoke(!templates.isEmpty && paletteTiles.count == 37,"Templates and every mouse input are available")
        canvas.dropped?(10,NSPoint(x:590,y:390))
        verifySmoke(layout.items.last?.source == 10 && layout.items.last?.valid == true,"Drop clamps button inside mouse canvas")
        canvas.dropped?(10,NSPoint(x:100,y:100))
        verifySmoke(layout.items.filter { $0.source == 10 }.count == 1,"Dragging a listed button again moves it instead of duplicating")
        tiles[10]?.changed?(NSRect(x:50,y:50,width:160,height:80))
        verifySmoke(layout.items.last?.width == 160,"Resize persists through the tile callback")
        flash(10)
        verifySmoke(paletteTiles[10]?.highlighted == true && tiles[10]?.highlighted == true,"Detection illuminates palette and placed button together")
        picker.selectItem(at:1); loadTemplate()
        verifySmoke(picker.indexOfSelectedItem == 0 && !layout.items.contains { $0.source == 10 },"Loading a template copies positions without switching device")
        for (title,appearance) in [("dark",NSAppearance.Name.darkAqua),("light",.aqua)] {
            panel.appearance = NSAppearance(named:appearance)
            saveSmokePreview(panel.contentView!,to:previewPath+"-mouse-layout-"+title+".png")
        }
        for amount in [Int32(-1),1] {
            let event = CGEvent(scrollWheelEvent2Source:nil,units:.line,wheelCount:2,wheel1:0,wheel2:amount,wheel3:0)!
            let native = NSEvent(cgEvent:event)!
            verifySmoke(MouseLayoutInput.wheel(native) == (amount > 0 ? 272 : 273),"Both tilt directions detected without posting input")
        }
        stopMonitoring()
    }
    private func finish(_ commit:Bool) {
        if commit {
            guard save?(layout) == true else { hint.stringValue = "保存できませんでした。もう一度お試しください。"; return }
        }
        setDetecting(false); stopMonitoring()
        if let parent = panel.sheetParent { parent.endSheet(panel,returnCode:commit ? .OK : .cancel) }
    }
}
