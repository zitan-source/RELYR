import AppKit

final class StudioSelectionRow: NSTableRowView {
    override func drawSelection(in dirtyRect:NSRect) {
        Studio.selected.setFill()
        NSBezierPath(roundedRect:bounds.insetBy(dx:2,dy:1),xRadius:7,yRadius:7).fill()
    }
}

// ProfileManagerWindow.xaml: a persistent profile list, separate from the
// profile currently used by the input engine. Selection only changes editing.
final class ProfileListView: NSScrollView, NSTableViewDataSource, NSTableViewDelegate {
    private let table = NSTableView()
    private var entries: [(id:String, name:String)] = []
    private var updating = false
    var selected: ((String) -> Void)?
    var rename: (() -> Void)?
    var duplicate: (() -> Void)?
    var remove: (() -> Void)?
    var editable = true
    var accessibilityName:String = "編集するプロファイルの一覧" { didSet { table.setAccessibilityLabel(accessibilityName) } }

    override init(frame:NSRect) {
        super.init(frame:frame)
        let column = NSTableColumn(identifier:NSUserInterfaceItemIdentifier("profile"))
        column.resizingMask = .autoresizingMask
        table.addTableColumn(column); table.headerView = nil
        table.rowHeight = 40; table.intercellSpacing = NSSize(width:0,height:4)
        table.frame = NSRect(x:0,y:0,width:208,height:0); table.autoresizingMask = [.width]
        table.backgroundColor = Studio.background; table.selectionHighlightStyle = .regular
        table.dataSource = self; table.delegate = self; table.allowsEmptySelection = false
        table.setAccessibilityLabel("編集するプロファイルの一覧")
        table.target = self; table.doubleAction = #selector(renameSelected)
        documentView = table; hasVerticalScroller = true; drawsBackground = false
        let menu = NSMenu(); menu.autoenablesItems = false; menu.delegate = self
        table.menu = menu
    }
    required init?(coder:NSCoder) { fatalError() }
    func update(_ entries:[(id:String,name:String)], selectedID:String) {
        updating = true; self.entries = entries; table.reloadData()
        if let index = entries.firstIndex(where:{$0.id == selectedID}) {
            table.selectRowIndexes(IndexSet(integer:index),byExtendingSelection:false)
        }
        updating = false
    }
    var selectedID:String? { entries.indices.contains(table.selectedRow) ? entries[table.selectedRow].id : nil }
    func numberOfRows(in tableView:NSTableView) -> Int { entries.count }
    func tableView(_ tableView:NSTableView, viewFor tableColumn:NSTableColumn?, row:Int) -> NSView? {
        let cell = NSTableCellView()
        let label = NSTextField(labelWithString:entries[row].name)
        label.font = .systemFont(ofSize:14,weight:.semibold); label.textColor = Studio.text
        label.lineBreakMode = .byTruncatingTail; label.toolTip = entries[row].name
        label.translatesAutoresizingMaskIntoConstraints = false; cell.addSubview(label); cell.textField = label
        NSLayoutConstraint.activate([label.leadingAnchor.constraint(equalTo:cell.leadingAnchor,constant:10),
            label.trailingAnchor.constraint(equalTo:cell.trailingAnchor,constant:-10),
            label.centerYAnchor.constraint(equalTo:cell.centerYAnchor)])
        return cell
    }
    func tableView(_ tableView:NSTableView, rowViewForRow row:Int) -> NSTableRowView? { StudioSelectionRow() }
    func tableViewSelectionDidChange(_ notification:Notification) {
        guard !updating, let id = selectedID else { return }; selected?(id)
    }
    @objc private func renameSelected() { if editable && selectedID != "default" { rename?() } }
    @objc private func duplicateSelected() { if editable { duplicate?() } }
    @objc private func removeSelected() { if editable && selectedID != "default" { remove?() } }
}

extension ProfileListView: NSMenuDelegate {
    func menuNeedsUpdate(_ menu:NSMenu) {
        if entries.indices.contains(table.clickedRow) {
            table.selectRowIndexes(IndexSet(integer:table.clickedRow),byExtendingSelection:false)
        }
        menu.removeAllItems()
        for (title,action,allowed) in [("複製",#selector(duplicateSelected),editable),
            ("名前を変更…",#selector(renameSelected),editable && selectedID != "default"),
            ("削除…",#selector(removeSelected),editable && selectedID != "default")] {
            let item = NSMenuItem(title:title,action:action,keyEquivalent:"")
            item.target = self; item.isEnabled = allowed && selectedID != nil; menu.addItem(item)
        }
    }
}
