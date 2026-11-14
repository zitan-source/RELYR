import AppKit

private struct ProfileApplication {
    let id:String
    let name:String
    let url:URL?
}

private final class ApplicationRows: NSScrollView, NSTableViewDataSource, NSTableViewDelegate {
    let table = NSTableView()
    var items:[ProfileApplication] = [] { didSet { table.reloadData() } }
    var selectionChanged:(() -> Void)?
    var activate:(() -> Void)?
    override init(frame:NSRect) {
        super.init(frame:frame)
        let column = NSTableColumn(identifier:NSUserInterfaceItemIdentifier("app")); column.resizingMask = .autoresizingMask
        table.addTableColumn(column); table.headerView = nil; table.rowHeight = 52
        table.backgroundColor = Studio.background; table.autoresizingMask = [.width]
        table.frame = NSRect(x:0,y:0,width:200,height:0)
        table.dataSource = self; table.delegate = self; table.target = self; table.doubleAction = #selector(performAction)
        documentView = table; hasVerticalScroller = true; drawsBackground = false
    }
    required init?(coder:NSCoder) { fatalError() }
    var selection:ProfileApplication? { items.indices.contains(table.selectedRow) ? items[table.selectedRow] : nil }
    func numberOfRows(in tableView:NSTableView) -> Int { items.count }
    func tableViewSelectionDidChange(_ notification:Notification) { selectionChanged?() }
    func tableView(_ tableView:NSTableView,rowViewForRow row:Int) -> NSTableRowView? { StudioSelectionRow() }
    @objc private func performAction() { if selection != nil { activate?() } }
    func tableView(_ tableView:NSTableView,viewFor column:NSTableColumn?,row:Int) -> NSView? {
        let item = items[row], cell = NSTableCellView()
        let icon = NSImageView(); icon.image = item.url.map { NSWorkspace.shared.icon(forFile:$0.path) } ?? NSImage(named:NSImage.applicationIconName)
        let name = NSTextField(labelWithString:item.name), detail = NSTextField(labelWithString:item.id)
        name.font = .systemFont(ofSize:13,weight:.semibold); detail.font = .systemFont(ofSize:11)
        name.textColor = Studio.text; detail.textColor = Studio.muted
        name.lineBreakMode = .byTruncatingTail; detail.lineBreakMode = .byTruncatingMiddle
        cell.toolTip = item.name + "\n" + item.id
        for view in [icon,name,detail] { view.translatesAutoresizingMaskIntoConstraints = false; cell.addSubview(view) }
        NSLayoutConstraint.activate([icon.leadingAnchor.constraint(equalTo:cell.leadingAnchor,constant:8),icon.centerYAnchor.constraint(equalTo:cell.centerYAnchor),icon.widthAnchor.constraint(equalToConstant:28),icon.heightAnchor.constraint(equalToConstant:28),name.leadingAnchor.constraint(equalTo:icon.trailingAnchor,constant:10),name.trailingAnchor.constraint(equalTo:cell.trailingAnchor,constant:-8),name.topAnchor.constraint(equalTo:cell.topAnchor,constant:8),detail.leadingAnchor.constraint(equalTo:name.leadingAnchor),detail.trailingAnchor.constraint(equalTo:name.trailingAnchor),detail.topAnchor.constraint(equalTo:name.bottomAnchor,constant:3)])
        return cell
    }
}

/// Windows ProfileManager: assigned apps and running/installed apps side by side.
final class ProfileApplicationsView: NSView, NSSearchFieldDelegate {
    override var isFlipped:Bool { true }
    private let assigned = ApplicationRows(), available = ApplicationRows()
    private let assignedTitle = Studio.label("対象に登録したアプリ",13)
    private let source = NSSegmentedControl(labels:["起動中","インストール済み"],trackingMode:.selectOne,target:nil,action:nil)
    private let search = NSSearchField()
    private let add = StudioButton(title:"",target:nil,action:nil), remove = StudioButton(title:"",target:nil,action:nil)
    private let refresh = StudioButton(title:"",target:nil,action:nil)
    private let message = Studio.label("",11,true)
    private var ids:[String] = [], installed:[ProfileApplication] = [], loading = false
    private var canEdit = false
    var change:(([String]) -> Void)?
    override init(frame:NSRect) {
        super.init(frame:frame)
        source.selectedSegment = 0; source.target = self; source.action = #selector(refreshSource)
        search.placeholderString = "アプリを検索"; search.delegate = self
        search.setAccessibilityLabel("プロファイルの対象アプリを検索")
        add.symbol = "arrow.left"; remove.symbol = "arrow.right"; refresh.symbol = "arrow.clockwise"
        for (button,title,action) in [(add,"選択したアプリを登録",#selector(addSelected)),(remove,"選択したアプリの登録を解除",#selector(removeSelected)),(refresh,"アプリ一覧を更新",#selector(refreshSource))] {
            button.target = self; button.action = action; button.toolTip = title; button.setAccessibilityLabel(title)
        }
        assigned.table.setAccessibilityLabel("登録した対象アプリ"); available.table.setAccessibilityLabel("登録できるアプリ")
        assigned.selectionChanged = { [weak self] in self?.updateButtons() }
        available.selectionChanged = { [weak self] in self?.updateButtons() }
        assigned.activate = { [weak self] in self?.removeSelected() }; available.activate = { [weak self] in self?.addSelected() }
        for view in [assignedTitle,source,search,assigned,available,add,remove,refresh,message] { addSubview(view) }
        refreshSource()
    }
    required init?(coder:NSCoder) { fatalError() }
    func update(ids:[String],editable:Bool) {
        guard self.ids != ids || canEdit != editable else { updateButtons(); return }
        self.ids = ids; canEdit = editable
        assigned.items = ids.map { id in
            let url = NSWorkspace.shared.urlForApplication(withBundleIdentifier:id)
            return ProfileApplication(id:id,name:url?.deletingPathExtension().lastPathComponent ?? id,url:url)
        }
        updateButtons()
    }
    private func updateButtons() {
        for control in [assignedTitle,source,search,assigned,available,add,remove,refresh] { control.isHidden = !canEdit }
        needsLayout = true
        add.isEnabled = canEdit && available.selection.map { !ids.contains($0.id) } == true
        remove.isEnabled = canEdit && assigned.selection != nil
        message.stringValue = !canEdit ? "標準は専用プロファイルのないアプリで使用します。" : loading ? "インストール済みアプリを読み込み中…" : "選択して矢印、またはダブルクリックで登録・解除"
    }
    @objc private func addSelected() {
        guard canEdit, let item = available.selection,!ids.contains(item.id) else { return }
        change?(ids+[item.id])
    }
    @objc private func removeSelected() {
        guard canEdit,let item = assigned.selection else { return }; change?(ids.filter { $0 != item.id })
    }
    @objc private func refreshSource() {
        if source.selectedSegment == 1 && !loading {
            loading = true; updateButtons()
            DispatchQueue.global(qos:.userInitiated).async { [weak self] in
                let fm = FileManager.default
                let roots = [URL(fileURLWithPath:"/Applications"),URL(fileURLWithPath:"/System/Applications"),fm.homeDirectoryForCurrentUser.appendingPathComponent("Applications")]
                var results:[String:ProfileApplication] = [:]
                for root in roots {
                    guard let walker = fm.enumerator(at:root,includingPropertiesForKeys:nil,options:[.skipsHiddenFiles,.skipsPackageDescendants]) else { continue }
                    for case let url as URL in walker {
                        if url.pathComponents.count > root.pathComponents.count+3 { walker.skipDescendants(); continue }
                        guard url.pathExtension.lowercased() == "app",let id = Bundle(url:url)?.bundleIdentifier else { continue }
                        results[id] = ProfileApplication(id:id,name:url.deletingPathExtension().lastPathComponent,url:url)
                    }
                }
                let result = results.values.sorted { $0.name.localizedStandardCompare($1.name) == .orderedAscending }
                DispatchQueue.main.async { [weak self] in self?.installed = result; self?.loading = false; self?.filter(); self?.updateButtons() }
            }
        }
        filter()
    }
    func controlTextDidChange(_ obj:Notification) { filter() }
    private func filter() {
        let running = NSWorkspace.shared.runningApplications.filter { $0.activationPolicy == .regular }.compactMap { app -> ProfileApplication? in
            guard let id = app.bundleIdentifier else { return nil }; return ProfileApplication(id:id,name:app.localizedName ?? id,url:app.bundleURL)
        }
        let values = source.selectedSegment == 0 ? running : installed
        var seen = Set<String>()
        let query = search.stringValue.trimmingCharacters(in:.whitespacesAndNewlines)
        available.items = values.filter { seen.insert($0.id).inserted && (query.isEmpty || ($0.name+" "+$0.id).localizedCaseInsensitiveContains(query)) }.sorted { $0.name.localizedStandardCompare($1.name) == .orderedAscending }
        updateButtons()
    }
    static func verify() {
        let view = ProfileApplicationsView()
        view.update(ids:[],editable:true)
        view.available.items = [ProfileApplication(id:"test.fixture",name:"Fixture",url:nil)]
        var changes:[[String]] = []
        view.change = { [weak view] ids in changes.append(ids); view?.update(ids:ids,editable:true) }
        view.available.table.selectRowIndexes(IndexSet(integer:0),byExtendingSelection:false)
        view.addSelected(); verifySmoke(changes == [["test.fixture"]])
        view.addSelected(); verifySmoke(changes.count == 1,"Duplicate app registration is ignored")
        view.assigned.table.selectRowIndexes(IndexSet(integer:0),byExtendingSelection:false)
        view.removeSelected(); verifySmoke(changes.last == [])
        view.update(ids:[],editable:false); view.addSelected()
        verifySmoke(changes.count == 2,"Default profile refuses app association")
        print("PASS: profile app add/remove, duplicate rejection and default protection; isolated view only")
    }
    override func layout() {
        super.layout(); let w = bounds.width,h = bounds.height,gap:CGFloat = 48, half = (w-gap)/2
        if !canEdit { message.frame = NSRect(x:0,y:0,width:w,height:min(40,h)); return }
        assignedTitle.frame = NSRect(x:0,y:0,width:half,height:32)
        source.frame = NSRect(x:half+gap,y:0,width:half-40,height:32)
        refresh.frame = NSRect(x:w-32,y:0,width:32,height:32)
        search.frame = NSRect(x:half+gap,y:40,width:half,height:28)
        assigned.frame = NSRect(x:0,y:76,width:half,height:h-104)
        available.frame = NSRect(x:half+gap,y:76,width:half,height:h-104)
        add.frame = NSRect(x:half+4,y:116,width:40,height:40); remove.frame = NSRect(x:half+4,y:164,width:40,height:40)
        message.frame = NSRect(x:0,y:h-24,width:w,height:24)
    }
}
