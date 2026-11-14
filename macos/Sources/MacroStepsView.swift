import AppKit

/// Native multi-selection list with drag insertion and standard clipboard commands.
private final class MacroPairRail: NSView {
    var segments: [(lane:Int,begins:Bool,ends:Bool)] = []
    override var isFlipped: Bool { true }
    override func draw(_ dirtyRect: NSRect) {
        for segment in segments {
            let color = [Studio.accent,NSColor.systemPurple,NSColor.systemOrange][segment.lane % 3]
            color.setStroke()
            let path = NSBezierPath(); path.lineWidth = 2
            let x = CGFloat(segment.lane)*7+2, middle = bounds.midY
            path.move(to:NSPoint(x:segment.begins ? x+5 : x,y:segment.begins ? middle : 0))
            if segment.begins { path.line(to:NSPoint(x:x,y:middle)) }
            path.line(to:NSPoint(x:x,y:segment.ends ? middle : bounds.maxY))
            if segment.ends { path.line(to:NSPoint(x:x+5,y:middle)) }
            path.stroke()
        }
    }
}

final class MacroStepsView: NSScrollView, NSTableViewDataSource, NSTableViewDelegate {
    private final class Table: NSTableView {
        var copySteps: (() -> Void)?
        var pasteSteps: (() -> Void)?
        var deleteSteps: (() -> Void)?
        var contextMenu: ((Int) -> NSMenu)?
        override func menu(for event: NSEvent) -> NSMenu? {
            contextMenu?(row(at:convert(event.locationInWindow,from:nil)))
        }
        override func keyDown(with event: NSEvent) {
            if event.modifierFlags.contains(.command), event.keyCode == 8 { copySteps?(); return }
            if event.modifierFlags.contains(.command), event.keyCode == 9 { pasteSteps?(); return }
            if event.keyCode == 51 { deleteSteps?(); return }
            super.keyDown(with:event)
        }
        @objc func copy(_ sender: Any?) { copySteps?() }
        @objc func paste(_ sender: Any?) { pasteSteps?() }
    }
    private static let stepsType = NSPasteboard.PasteboardType("io.relyr.macro-steps")
    private static let moveType = NSPasteboard.PasteboardType("io.relyr.macro-move")
    private let table = Table()
    private var steps: [MacroStep]
    private var executionStepID: String?
    private var executionFailed = false
    func showExecution(stepID: String?, failed: Bool) {
        guard executionStepID != stepID || executionFailed != failed else { return }
        let old = executionStepID
        executionStepID = stepID; executionFailed = failed
        let rows = IndexSet(steps.indices.filter { steps[$0].id == old || steps[$0].id == stepID })
        table.reloadData(forRowIndexes:rows,columnIndexes:IndexSet(integer:0))
        if let row = steps.firstIndex(where: { $0.id == stepID }) { table.scrollRowToVisible(row) }
    }
    var canPasteSteps: Bool {
        guard let data = clipboard.data(forType:Self.stepsType), let decoded = try? JSONDecoder().decode([MacroStep].self,from:data) else { return false }
        return !decoded.isEmpty
    }
    var onNotice: ((String) -> Void)?
    var selectionChanged:((Set<String>) -> Void)?
    var selectedStepIDs:Set<String> { Set(table.selectedRowIndexes.compactMap { steps.indices.contains($0) ? steps[$0].id : nil }) }
    func selectIDs(_ ids:Set<String>) { table.selectRowIndexes(IndexSet(steps.indices.filter { ids.contains(steps[$0].id) }),byExtendingSelection:false) }
    func tableViewSelectionDidChange(_ notification:Notification) { selectionChanged?(selectedStepIDs) }
    private let clipboard: NSPasteboard
    private let layout: KeyboardLayout
    private let changed: ([MacroStep]) -> Void
    private let edit: (String) -> Void
    private let dropAction: (String,String?) -> Bool
    init(steps: [MacroStep], layout: KeyboardLayout, clipboard: NSPasteboard = .general, changed: @escaping ([MacroStep]) -> Void, edit: @escaping (String) -> Void, dropAction: @escaping (String,String?) -> Bool) {
        self.steps = steps; self.layout = layout; self.clipboard = clipboard; self.changed = changed; self.edit = edit; self.dropAction = dropAction
        super.init(frame:.zero)
        hasVerticalScroller = true; drawsBackground = false
        table.addTableColumn(NSTableColumn(identifier:NSUserInterfaceItemIdentifier("step")))
        table.headerView = nil; table.rowHeight = 52; table.allowsMultipleSelection = true
        table.usesAlternatingRowBackgroundColors = false; table.backgroundColor = Studio.background; table.columnAutoresizingStyle = .lastColumnOnlyAutoresizingStyle
        table.dataSource = self; table.delegate = self; table.target = self; table.doubleAction = #selector(editSelected)
        table.registerForDraggedTypes([Self.moveType,Studio.dragType]); table.setDraggingSourceOperationMask(.move,forLocal:true)
        table.copySteps = { [weak self] in self?.copySelection() }; table.pasteSteps = { [weak self] in self?.pasteSelection() }; table.deleteSteps = { [weak self] in self?.deleteSelection() }
        table.setAccessibilityLabel("マクロの手順。複数選択、ドラッグで並べ替え、Command Cでコピー、Command Vで貼り付け")
        table.contextMenu = { [weak self] row in self?.makeContextMenu(row:row) ?? NSMenu() }
        documentView = table
    }
    required init?(coder: NSCoder) { fatalError() }
    func numberOfRows(in tableView: NSTableView) -> Int { steps.count }
    func tableView(_ tableView: NSTableView, viewFor tableColumn: NSTableColumn?, row: Int) -> NSView? {
        let step = steps[row]
        var name = step.phase == .move ? "マウス移動 (\(Int(step.x)), \(Int(step.y)))" : step.action?.targetDescription(layout:layout) ?? "待機"
        if step.action?.action == .mouse && (step.phase == .down || step.phase == .up) { name = name.replacingOccurrences(of:"クリック",with:"ボタン") }
        let cell = NSTableCellView()
        let executing = step.id == executionStepID
        let number = NSTextField(labelWithString:executing ? (executionFailed ? "!" : "▶") : String(row+1))
        let title = NSTextField(labelWithString:name)
        let phaseLabel = step.phase == .down ? "押し続ける" : step.phase == .up ? "離す" : step.phase == .move ? "カーソル移動" : step.action?.action == .mouse ? "1クリック" : step.action == nil ? "待機" : "1回実行"
        let detail = NSTextField(labelWithString:phaseLabel)
        let symbol = step.phase == .down ? "arrow.down.circle.fill" : step.phase == .up ? "arrow.up.circle" : step.phase == .move ? "cursorarrow.motionlines" : step.action?.action == .mouse ? "cursorarrow.click" : step.action == nil ? "clock" : "play.circle"
        let phaseIcon = NSImageView()
        phaseIcon.image = NSImage(systemSymbolName:symbol,accessibilityDescription:phaseLabel)
        phaseIcon.contentTintColor = step.phase == .down ? Studio.accent : step.phase == .up ? NSColor.systemOrange : Studio.text
        phaseIcon.toolTip = phaseLabel
        let delay = NSTextField(labelWithString:step.delayMilliseconds > 0 ? String(format:"%.2f 秒",Double(step.delayMilliseconds)/1000) : "")
        var pending: [String:[Int]] = [:], pairs: [(start:Int,end:Int,lane:Int)] = []
        for (index,entry) in steps.enumerated() {
            guard let action = entry.action else { continue }
            let identity = action.action.rawValue + ":" + String(action.output)
            if entry.phase == .down { pending[identity,default:[]].append(index) }
            if entry.phase == .up, let start = pending[identity]?.popLast() { pairs.append((start,index,0)) }
        }
        pairs.sort { $0.start < $1.start }
        for index in pairs.indices {
            let occupied = Set(pairs.prefix(index).filter { $0.end >= pairs[index].start }.map { $0.lane })
            pairs[index].lane = (0...pairs.count).first { !occupied.contains($0) } ?? 0
        }
        let activePairs = pairs.filter { $0.start <= row && row <= $0.end }
        let endpoint = pairs.first { $0.start == row || $0.end == row }
        if let endpoint = endpoint {
            phaseIcon.contentTintColor = [Studio.accent,NSColor.systemPurple,NSColor.systemOrange][endpoint.lane % 3]
            detail.stringValue = step.phase == .down ? "押す → \(endpoint.end+1)で離す" : "離す ← \(endpoint.start+1)で押下"
        } else if step.phase == .up || step.phase == .down {
            detail.stringValue = step.phase == .up ? "対応する押下なし" : "対応する解放なし"
            phaseIcon.image = NSImage(systemSymbolName:"exclamationmark.circle",accessibilityDescription:detail.stringValue)
            phaseIcon.contentTintColor = .systemOrange
        }
        let rail = MacroPairRail()
        rail.segments = activePairs.map { ($0.lane,row == $0.start,row == $0.end) }
        let bar: NSView = rail
        number.font = .systemFont(ofSize:13,weight:.semibold); number.textColor = executing ? (executionFailed ? .systemRed : Studio.accent) : Studio.text
        title.font = .systemFont(ofSize:13,weight:.semibold); title.textColor = Studio.text
        detail.font = .systemFont(ofSize:11); detail.textColor = Studio.muted
        delay.font = .monospacedDigitSystemFont(ofSize:12,weight:.regular); delay.textColor = Studio.accent; delay.alignment = .right
        title.lineBreakMode = .byTruncatingTail; title.toolTip = name
        for view in [bar,number,phaseIcon,title,detail,delay] { view.translatesAutoresizingMaskIntoConstraints = false; cell.addSubview(view) }
        NSLayoutConstraint.activate([
            bar.leadingAnchor.constraint(equalTo:cell.leadingAnchor,constant:8),bar.centerYAnchor.constraint(equalTo:cell.centerYAnchor),bar.widthAnchor.constraint(equalToConstant:CGFloat(max(1,(pairs.map { $0.lane }.max() ?? 0)+1))*7+6),bar.heightAnchor.constraint(equalToConstant:54),
            number.leadingAnchor.constraint(equalTo:bar.trailingAnchor,constant:12),number.widthAnchor.constraint(equalToConstant:28),number.centerYAnchor.constraint(equalTo:cell.centerYAnchor),
            phaseIcon.leadingAnchor.constraint(equalTo:number.trailingAnchor,constant:4),phaseIcon.widthAnchor.constraint(equalToConstant:24),phaseIcon.heightAnchor.constraint(equalToConstant:24),phaseIcon.centerYAnchor.constraint(equalTo:cell.centerYAnchor),
            title.leadingAnchor.constraint(equalTo:phaseIcon.trailingAnchor,constant:10),title.topAnchor.constraint(equalTo:cell.topAnchor,constant:detail.stringValue.isEmpty ? 17 : 8),title.trailingAnchor.constraint(lessThanOrEqualTo:delay.leadingAnchor,constant:-8),
            detail.leadingAnchor.constraint(equalTo:title.leadingAnchor),detail.topAnchor.constraint(equalTo:title.bottomAnchor,constant:3),
            delay.trailingAnchor.constraint(equalTo:cell.trailingAnchor,constant:-12),delay.widthAnchor.constraint(equalToConstant:80),delay.centerYAnchor.constraint(equalTo:cell.centerYAnchor)])
        cell.toolTip = "\(row+1). \(name) · 実行前の待機 \(step.delayMilliseconds) ms"
        cell.textField = title; cell.setAccessibilityLabel((executing ? (executionFailed ? "失敗した手順。" : "実行中の手順。") : "") + "\(row+1). \(name)、実行前に\(step.delayMilliseconds)ミリ秒待機、\(detail.stringValue)")
        return cell
    }
    func tableView(_ tableView:NSTableView,rowViewForRow row:Int) -> NSTableRowView? { StudioSelectionRow() }
    @objc private func editSelected() { if steps.indices.contains(table.clickedRow) { edit(steps[table.clickedRow].id) } }
    private func makeContextMenu(row: Int) -> NSMenu {
        // Preserve a multi-selection when its member is clicked; otherwise target the clicked row.
        if steps.indices.contains(row) {
            if !table.selectedRowIndexes.contains(row) { table.selectRowIndexes(IndexSet(integer:row),byExtendingSelection:false) }
        } else { table.deselectAll(nil) }
        let menu = NSMenu(); menu.autoenablesItems = false
        let count = table.selectedRowIndexes.count
        func item(_ title:String, _ selector:Selector, enabled:Bool) {
            let entry = NSMenuItem(title:title,action:selector,keyEquivalent:"")
            entry.target = self; entry.isEnabled = enabled; menu.addItem(entry)
        }
        item("手順を編集…",#selector(editContextSelection),enabled:count == 1)
        menu.addItem(.separator())
        item("コピー",#selector(copyContextSelection),enabled:count > 0)
        let canPaste = canPasteSteps
        item(count > 0 ? "選択した手順の後に貼り付け" : "末尾に貼り付け",#selector(pasteContextSelection),enabled:canPaste)
        item("複製",#selector(duplicateContextSelection),enabled:count > 0)
        menu.addItem(.separator())
        item("削除",#selector(deleteContextSelection),enabled:count > 0)
        return menu
    }
    @objc private func editContextSelection() {
        guard table.selectedRowIndexes.count == 1, let row = table.selectedRowIndexes.first else { return }
        edit(steps[row].id)
    }
    @objc private func copyContextSelection() { copySelection() }
    @objc private func pasteContextSelection() { pasteSelection() }
    @objc private func deleteContextSelection() { deleteSelection() }
    @objc private func duplicateContextSelection() {
        guard let last = table.selectedRowIndexes.last else { return }
        let copies = table.selectedRowIndexes.map { index -> MacroStep in
            var step = steps[index]; step.id = UUID().uuidString; return step
        }
        var result = steps; result.insert(contentsOf:copies,at:last+1); changed(result)
    }
    func copySelection() {
        let selected = table.selectedRowIndexes.compactMap { steps.indices.contains($0) ? steps[$0] : nil }
        guard !selected.isEmpty, let data = try? JSONEncoder().encode(selected) else { return }
        clipboard.clearContents(); clipboard.setData(data,forType:Self.stepsType)
    }
    func pasteSelection() {
        guard let data = clipboard.data(forType:Self.stepsType), let pasted = try? JSONDecoder().decode([MacroStep].self,from:data), !pasted.isEmpty else { onNotice?("先にマクロの手順を選び、コピーしてください。通常の文字や画像は手順として貼り付けできません。"); return }
        var result = steps; let index = table.selectedRowIndexes.last.map { $0+1 } ?? steps.count
        result.insert(contentsOf:pasted.map { var step = $0; step.id = UUID().uuidString; return step },at:index); changed(result)
    }
    func deleteSelection() { let selected = table.selectedRowIndexes; guard !selected.isEmpty else { return }; changed(steps.enumerated().filter { !selected.contains($0.offset) }.map { $0.element }) }
    func tableView(_ tableView: NSTableView, pasteboardWriterForRow row: Int) -> NSPasteboardWriting? {
        let item = NSPasteboardItem(); item.setString(steps[row].id,forType:Self.moveType); return item
    }
    func tableView(_ tableView: NSTableView, validateDrop info: NSDraggingInfo, proposedRow row: Int, proposedDropOperation operation: NSTableView.DropOperation) -> NSDragOperation {
        tableView.setDropRow(row,dropOperation:.above)
        if info.draggingSource as? NSTableView === table { return .move }
        return info.draggingPasteboard.string(forType:Studio.dragType) != nil ? .copy : []
    }
    private func moveSteps(_ ids: Set<String>, to row: Int) {
        let picked = steps.filter { ids.contains($0.id) }
        let index = steps.prefix(max(0,row)).filter { !ids.contains($0.id) }.count
        var result = steps.filter { !ids.contains($0.id) }; result.insert(contentsOf:picked,at:index); changed(result)
    }
    static func verifyEditing() {
        let board = NSPasteboard(name:NSPasteboard.Name("RELYR-macro-test-"+UUID().uuidString))
        defer { board.releaseGlobally() }
        let steps = [MacroStep(delayMilliseconds:100),MacroStep(delayMilliseconds:200),MacroStep(delayMilliseconds:300)]
        var result: [MacroStep] = []
        let view = MacroStepsView(steps:steps,layout:.jis,clipboard:board,changed:{result=$0},edit:{_ in},dropAction:{_,_ in false})
        verifySmoke(!view.canPasteSteps,"Empty clipboard is not pasteable")
        view.selectIDs([steps[0].id]); view.showExecution(stepID:steps[2].id,failed:false)
        verifySmoke(view.selectedStepIDs == [steps[0].id] && view.executionStepID == steps[2].id,"Execution highlight never changes edit selection")
        view.showExecution(stepID:steps[2].id,failed:true)
        verifySmoke(view.executionFailed,"Failure stays on the failed step")
        view.showExecution(stepID:nil,failed:false)
        verifySmoke(view.executionStepID == nil,"Completion clears execution marker")
        view.table.selectRowIndexes(IndexSet([0,1]),byExtendingSelection:false); view.copySelection()
        view.table.selectRowIndexes(IndexSet(integer:2),byExtendingSelection:false); view.pasteSelection()
        verifySmoke(result.map { $0.delayMilliseconds } == [100,200,300,100,200],"Macro multiple-step paste order")
        verifySmoke(Set(result.map { $0.id }).count == 5,"Pasted steps get fresh IDs")
        view.moveSteps(Set([steps[0].id,steps[1].id]),to:3)
        verifySmoke(result.map { $0.delayMilliseconds } == [300,100,200],"Drag moves group to target without step-by-step arrows")
        view.table.selectRowIndexes(IndexSet([0,2]),byExtendingSelection:false); view.deleteSelection()
        verifySmoke(result == [steps[1]],"Multi-selection deletes only chosen steps")
        view.table.selectRowIndexes(IndexSet([0,1]),byExtendingSelection:false)
        let groupMenu = view.makeContextMenu(row:1)
        verifySmoke(view.table.selectedRowIndexes == IndexSet([0,1]),"Right click retains selected group")
        verifySmoke(groupMenu.items.first?.isEnabled == false,"Group cannot open single-step editor")
        view.copyContextSelection(); view.duplicateContextSelection()
        verifySmoke(result.map { $0.delayMilliseconds } == [100,200,100,200,300],"Context duplicate preserves order")
        verifySmoke(Set(result.map { $0.id }).count == 5,"Context duplicate has fresh IDs")
        _ = view.makeContextMenu(row:2)
        view.deleteContextSelection()
        verifySmoke(result == Array(steps.prefix(2)),"Right click targets unselected row")
        let emptyMenu = view.makeContextMenu(row:-1)
        verifySmoke(view.table.selectedRowIndexes.isEmpty && emptyMenu.items.contains { $0.title == "末尾に貼り付け" && $0.isEnabled },"Blank area allows append paste")
        view.pasteContextSelection()
        verifySmoke(result.map { $0.delayMilliseconds } == [100,200,300,100,200],"Context paste uses copied group")
        print("PASS: macro context menu selection, edit availability, copy/paste, duplicate and delete")
        print("PASS: macro multi-copy/paste, unique IDs, group drag reorder, multi-delete; isolated clipboard")
    }
    func tableView(_ tableView: NSTableView, acceptDrop info: NSDraggingInfo, row: Int, dropOperation: NSTableView.DropOperation) -> Bool {
        if info.draggingSource as? NSTableView === table {
            let ids = Set((info.draggingPasteboard.pasteboardItems ?? []).compactMap { $0.string(forType:Self.moveType) })
            guard !ids.isEmpty else { return false }
            moveSteps(ids,to:row); return true
        }
        guard let json = info.draggingPasteboard.string(forType:Studio.dragType) else { return false }
        return dropAction(json,steps.indices.contains(row) ? steps[row].id : nil)
    }
}
