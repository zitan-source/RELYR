import AppKit

/// Shared drag transaction for editor and nonactivating live Deck.
final class DeckTile: StudioButton, NSDraggingSource {
    override var showsTileChrome: Bool { image?.isTemplate != false }
    override var tileCornerRadius: CGFloat { 10 }
    override func imageRect(for image: NSImage) -> NSRect {
        // App/custom artwork uses the tile, while template glyphs retain their readable size.
        guard !image.isTemplate, image.size.width > 0, image.size.height > 0 else {
            return super.imageRect(for:image)
        }
        let area = bounds
        let scale = min(area.width/image.size.width,area.height/image.size.height)
        let size = NSSize(width:image.size.width*scale,height:image.size.height*scale)
        return NSRect(x:bounds.midX-size.width/2,y:bounds.midY-size.height/2,width:size.width,height:size.height)
    }
    private(set) static var activeDrags = 0
    static let moveType = NSPasteboard.PasteboardType("io.github.zitan-source.relyr.deck-items")
    var isEmptyDropTarget = false { didSet { needsDisplay = true } }
    var showsEmptyDropVisual = false { didSet { if oldValue != showsEmptyDropVisual { needsDisplay = true } } }
    var monitorKind: DeckMonitor? { didSet { needsDisplay = true } }
    var monitorCaption = "" { didSet { if oldValue != monitorCaption { needsDisplay = true } } }
    var monitorSamples: [Double] = [] { didSet { needsDisplay = true } }
    var wheelAction: ((CGFloat) -> Void)?
    private var lastWheel = 0.0
    override func scrollWheel(with event: NSEvent) {
        guard let action = wheelAction else { super.scrollWheel(with:event); return }
        guard abs(event.scrollingDeltaY) > 0,event.timestamp-lastWheel >= 0.08 else { return }
        lastWheel = event.timestamp; action(event.scrollingDeltaY)
    }
    override func draw(_ dirtyRect: NSRect) {
        // Keep the view available for hit testing and drops without painting
        // hundreds of empty placeholders in the resting Deck.
        if isEmptyDropTarget && !showsEmptyDropVisual && !hoveringDrop && movePreviewNumber == nil && !invalidMovePreview { return }
        super.draw(dirtyRect)
        if movePreviewNumber != nil || invalidMovePreview {
            let color = invalidMovePreview ? NSColor.systemRed : Studio.accent
            let border = NSBezierPath(roundedRect:bounds.insetBy(dx:2,dy:2),xRadius:9,yRadius:9)
            color.withAlphaComponent(0.22).setFill(); border.fill(); color.setStroke(); border.lineWidth = 3; border.stroke()
            let label = invalidMovePreview ? "配置不可" : "移動 \(movePreviewNumber!)"
            let badge = NSRect(x:4,y:4,width:min(54,bounds.width-8),height:18)
            color.setFill(); NSBezierPath(roundedRect:badge,xRadius:4,yRadius:4).fill()
            let paragraph = NSMutableParagraphStyle(); paragraph.alignment = .center
            (label as NSString).draw(in:badge,withAttributes:[.font:NSFont.systemFont(ofSize:9,weight:.bold),.foregroundColor:NSColor.white,.paragraphStyle:paragraph])
        }
        guard !monitorCaption.isEmpty else { return }
        let paragraph = NSMutableParagraphStyle(); paragraph.alignment = .center
        (monitorCaption as NSString).draw(in:NSRect(x:2,y:bounds.height-14,width:bounds.width-4,height:9),withAttributes:[.font:NSFont.systemFont(ofSize:6,weight:.medium),.foregroundColor:Studio.muted,.paragraphStyle:paragraph])
        guard let kind = monitorKind else { return }
        let symbol: String
        let tint: NSColor
        switch kind {
        case .brightness: symbol = "sun.max.fill"; tint = .systemYellow
        case .volume: symbol = "speaker.wave.2.fill"; tint = .systemCyan
        case .microphone: symbol = "mic.fill"; tint = .systemPink
        case .battery: symbol = "battery.100"; tint = .systemGreen
        case .cpu: symbol = "cpu"; tint = .systemOrange
        case .memory: symbol = "memorychip"; tint = .systemPurple
        case .disk: symbol = "internaldrive"; tint = .systemBlue
        case .clock: symbol = "clock"; tint = Studio.accent
        case .date: symbol = "calendar"; tint = .systemRed
        case .uptime: symbol = "power"; tint = Studio.accent
        case .timer: symbol = "timer"; tint = .systemOrange
        case .wifi: symbol = "wifi"; tint = .systemBlue
        case .bluetooth: symbol = "antenna.radiowaves.left.and.right"; tint = .systemIndigo
        case .networkUp: symbol = "arrow.up.circle"; tint = .systemOrange
        case .networkDown: symbol = "arrow.down.circle"; tint = .systemCyan
        case .networkStatus: symbol = "network"; tint = .systemBlue
        case .autoExtract: symbol = "archivebox"; tint = .systemPurple
        }
        let glyphSize = min(13,bounds.height*0.20)
        if let glyph = NSImage(systemSymbolName:symbol,accessibilityDescription:kind.title)?.withSymbolConfiguration(.init(pointSize:glyphSize,weight:.medium))?.withSymbolConfiguration(.init(paletteColors:[tint])) {
            glyph.draw(in:NSRect(x:(bounds.width-glyphSize)/2,y:3,width:glyphSize,height:glyphSize),from:.zero,operation:.sourceOver,fraction:1,respectFlipped:true,hints:nil)
        }
        let strip = NSRect(x:8,y:bounds.height-4,width:max(0,bounds.width-16),height:3)
        if [.volume,.brightness,.battery].contains(kind) {
            NSColor.labelColor.withAlphaComponent(0.12).setFill()
            NSBezierPath(roundedRect:strip,xRadius:1.5,yRadius:1.5).fill()
            if title.hasSuffix("%"),let value = Double(title.dropLast()),value.isFinite {
                var fill = strip; fill.size.width *= CGFloat(min(100,max(0,value))/100)
                tint.setFill(); NSBezierPath(roundedRect:fill,xRadius:1.5,yRadius:1.5).fill()
            }
        } else if [.cpu,.memory,.networkUp,.networkDown].contains(kind),monitorSamples.count > 1 {
            let maximum = kind == .cpu ? 100 : max(1,monitorSamples.max() ?? 1)
            let path = NSBezierPath()
            for (index,value) in monitorSamples.enumerated() {
                let point = NSPoint(x:8+CGFloat(index)*(bounds.width-16)/CGFloat(monitorSamples.count-1),y:bounds.height-2-CGFloat(value/maximum)*3)
                if index == 0 { path.move(to:point) } else { path.line(to:point) }
            }
            tint.setStroke(); path.lineWidth = 1.5; path.stroke()
        } else if [.wifi,.bluetooth,.networkStatus,.microphone,.autoExtract].contains(kind) {
            let active = title == "ON" || title == "ONLINE"
            (active ? tint : Studio.muted).setFill()
            NSBezierPath(ovalIn:NSRect(x:bounds.midX-2,y:bounds.height-4,width:4,height:4)).fill()
        }
    }

    var hoverChanged: ((Bool) -> Void)?
    var hoverAnimation = true
    private func animateHover(_ inside: Bool) {
        guard let layer = layer else { return }
        layer.removeAnimation(forKey:"deck-hover")
        let target: Float = inside && hoverAnimation && !Studio.reduceMotion ? 0.94 : 1
        Studio.fade(layer,to:target,duration:inside ? 0.14 : 0.10,key:"deck-hover-opacity")
    }
    override func mouseEntered(with event: NSEvent) { super.mouseEntered(with:event); animateHover(true); hoverChanged?(true) }
    override func mouseExited(with event: NSEvent) { super.mouseExited(with:event); animateHover(false); hoverChanged?(false) }
    override func viewDidMoveToWindow() { super.viewDidMoveToWindow(); animateHover(false); if window == nil { hoverChanged?(false) } }
    var dragStateChanged: ((Bool) -> Void)?
    var allowsReordering = true
    weak var movePreviewRoot: NSView?
    var movePreviewContext: (() -> (Configuration,String,Int)?)?
    private var movePreviewNumber: Int? { didSet { needsDisplay = true } }
    private var invalidMovePreview = false { didSet { needsDisplay = true } }
    private static weak var previewRoot: NSView?
    private static func previewTiles(_ root: NSView) -> [DeckTile] {
        (root as? DeckTile).map { [$0] } ?? root.subviews.flatMap { previewTiles($0) }
    }
    private static func clearMovePreview() {
        if let root = previewRoot { for tile in previewTiles(root) { tile.movePreviewNumber = nil; tile.invalidMovePreview = false } }
        previewRoot = nil
    }
    private struct MovePayload: Codable { var ids: [String]; var anchor: String }
    var dragAnchorID: String?
    var emptyMoveSlot: Int?
    private func previewMove(_ payload: MovePayload) -> Bool {
        Self.clearMovePreview()
        guard let root = movePreviewRoot, let (config, layout, hovered) = movePreviewContext?() else { return false }
        Self.previewRoot = root
        let ids = Set(payload.ids)
        let positions = config.deckPositions(layout)
        guard ids.count == payload.ids.count,
              let destinations = config.deckMoveDestinations(ids, layout: layout, positions: positions, anchoredAt: payload.anchor, toSlot: hovered) else {
            invalidMovePreview = true
            return false
        }
        let numbers = Dictionary(uniqueKeysWithValues: destinations.enumerated().map { ($0.element.slot, $0.offset + 1) })
        for tile in Self.previewTiles(root) {
            if let slot = tile.dragAnchorID.flatMap({ positions[$0] }) ?? tile.emptyMoveSlot {
                tile.movePreviewNumber = numbers[slot]
            }
        }
        return true
    }
    var dragIDs: (() -> [String])?
    var fileURL: (() -> URL?)?
    var moveItems: (([String],String,Int) -> Bool)?
    var receiveFile: ((URL) -> Bool)?
    func enableDeckDrops() { registerForDraggedTypes([Self.moveType,.fileURL,Studio.dragType]) }
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }
    override func mouseDown(with event: NSEvent) {
        guard isEnabled else { return }
        lastClickModifiers = event.modifierFlags
        guard allowsReordering, dragIDs != nil, let window = window else { super.mouseDown(with:event); return }
        isHighlighted = true
        defer { isHighlighted = false }
        while let next = window.nextEvent(matching:[.leftMouseDragged,.leftMouseUp]) {
            if next.type == .leftMouseUp {
                if bounds.contains(convert(next.locationInWindow,from:nil)) { sendAction(action,to:target) }
                return
            }
            if hypot(next.locationInWindow.x-event.locationInWindow.x,next.locationInWindow.y-event.locationInWindow.y) < 5 { continue }
            guard let ids = dragIDs?(), !ids.isEmpty, let anchor = dragAnchorID, let data = try? JSONEncoder().encode(MovePayload(ids:ids,anchor:anchor)) else { return }
            let payload = NSPasteboardItem(); payload.setData(data,forType:Self.moveType)
            if let url = fileURL?() { payload.setString(url.absoluteString,forType:.fileURL) }
            let dragging = NSDraggingItem(pasteboardWriter:payload)
            let preview = NSImage(size:bounds.size)
            preview.lockFocus(); draw(bounds); preview.unlockFocus()
            dragging.setDraggingFrame(bounds,contents:preview)
            beginDraggingSession(with:[dragging],event:next,source:self)
            return
        }
    }
    func draggingSession(_ session: NSDraggingSession, willBeginAt screenPoint: NSPoint) { Self.activeDrags += 1; dragStateChanged?(true) }
    func draggingSession(_ session: NSDraggingSession, endedAt screenPoint: NSPoint, operation: NSDragOperation) { Self.clearMovePreview(); Self.activeDrags = max(0,Self.activeDrags-1); dragStateChanged?(false) }
    func draggingSession(_ session: NSDraggingSession, sourceOperationMaskFor context: NSDraggingContext) -> NSDragOperation {
        context == .withinApplication ? .move : (fileURL?() == nil ? [] : .copy)
    }
    override func draggingEntered(_ sender: NSDraggingInfo) -> NSDragOperation { draggingUpdated(sender) }
    override func draggingUpdated(_ sender: NSDraggingInfo) -> NSDragOperation {
        guard isEnabled else { return [] }
        if isEmptyDropTarget { showsEmptyDropVisual = true }
        if let data = sender.draggingPasteboard.data(forType:Self.moveType), let payload = try? JSONDecoder().decode(MovePayload.self,from:data), moveItems != nil { hoveringDrop = false; return previewMove(payload) ? .move : [] }
        if sender.draggingPasteboard.string(forType:.fileURL) != nil, receiveFile != nil { hoveringDrop = true; return .copy }
        return super.draggingUpdated(sender)
    }
    override func draggingExited(_ sender: NSDraggingInfo?) { Self.clearMovePreview(); showsEmptyDropVisual = false; super.draggingExited(sender) }
    override func performDragOperation(_ sender: NSDraggingInfo) -> Bool {
        defer { hoveringDrop = false; showsEmptyDropVisual = false; Self.clearMovePreview() }
        guard isEnabled else { return false }
        if let data = sender.draggingPasteboard.data(forType:Self.moveType),
           let payload = try? JSONDecoder().decode(MovePayload.self, from: data) {
            guard Set(payload.ids).count == payload.ids.count,
                  let (_, _, hovered) = movePreviewContext?() else { return false }
            return moveItems?(payload.ids, payload.anchor, hovered) ?? false
        }
        if let text = sender.draggingPasteboard.string(forType:.fileURL), let url = URL(string:text),url.isFileURL { return receiveFile?(url) ?? false }
        return super.performDragOperation(sender)
    }
}

/// Lightweight gallery thumbnail; no action execution or monitor subscriptions.
final class DeckLayoutCard: StudioButton {
    var layout = DeckLayout()
    var positions: [Int:NSColor] = [:]
    var count = 0
    override func draw(_ dirtyRect: NSRect) {
        super.draw(dirtyRect)
        let columns = layout.columns, rows = max(1,(count+columns-1)/columns)
        let area = NSRect(x:16,y:16,width:bounds.width-32,height:bounds.height-76)
        let scale = min(area.width/CGFloat(columns*12),area.height/CGFloat(rows*10))
        for slot in 0..<count {
            let rect = NSRect(x:area.minX+CGFloat(slot%columns)*12*scale,y:area.minY+CGFloat(slot/columns)*10*scale,width:10*scale,height:8*scale)
            (positions[slot] ?? Studio.muted.withAlphaComponent(0.22)).setFill()
            NSBezierPath(roundedRect:rect,xRadius:2,yRadius:2).fill()
        }
        let paragraph = NSMutableParagraphStyle(); paragraph.lineBreakMode = .byTruncatingTail
        (layout.name as NSString).draw(in:NSRect(x:16,y:bounds.height-50,width:bounds.width-32,height:22),withAttributes:[.font:NSFont.systemFont(ofSize:14,weight:.semibold),.foregroundColor:Studio.text,.paragraphStyle:paragraph])
        ("\(columns) × \(rows) ・ \(positions.count) アクション" as NSString).draw(in:NSRect(x:16,y:bounds.height-26,width:bounds.width-32,height:18),withAttributes:[.font:NSFont.systemFont(ofSize:11),.foregroundColor:Studio.muted])
    }
}
