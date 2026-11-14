import AppKit

final class MacroBrowserView: NSView, NSSearchFieldDelegate {
    override var isFlipped:Bool { true }
    private let search = NSSearchField()
    let list = ProfileListView()
    private var entries:[(id:String,name:String)] = []
    private var selectedID = ""
    override init(frame:NSRect) {
        super.init(frame:frame)
        search.placeholderString = "マクロを検索"; search.delegate = self
        search.setAccessibilityLabel("マクロを検索")
        list.accessibilityName = "編集するマクロの一覧"
        addSubview(search); addSubview(list)
    }
    required init?(coder:NSCoder) { fatalError() }
    func update(_ entries:[(id:String,name:String)],selectedID:String) { self.entries = entries; self.selectedID = selectedID; filter() }
    private func filter() {
        let query = search.stringValue.trimmingCharacters(in:.whitespacesAndNewlines)
        list.update(entries.filter { query.isEmpty || $0.name.localizedCaseInsensitiveContains(query) },selectedID:selectedID)
    }
    func controlTextDidChange(_ obj:Notification) { filter() }
    override func layout() {
        super.layout(); search.frame = NSRect(x:0,y:0,width:bounds.width,height:32)
        list.frame = NSRect(x:0,y:44,width:bounds.width,height:max(0,bounds.height-44))
    }
}
