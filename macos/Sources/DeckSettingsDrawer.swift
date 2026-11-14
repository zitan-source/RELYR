import AppKit

/// A bounded, optional settings surface; opening it never changes saved values.
final class DeckSettingsDrawer: NSView {
    override var isFlipped: Bool { true }
    var changeLayout: ((DeckLayout) -> Void)?
    var changeBehavior: ((Int,DeckDisplayBehavior) -> Void)?
    var changeLink: ((Bool) -> Void)?
    var changeDefault: ((Bool) -> Void)?
    var closeDrawer: (() -> Void)?
    private var value = DeckLayout()
    private var configuration = Configuration()
    private var profile = "default"
    private let tabs = NSSegmentedControl(labels:["レイアウト","外観","動作"],trackingMode:.selectOne,target:nil,action:nil)
    private let scroll = NSScrollView()
    private let body = StudioSurface()
    private var choices: [Int:NSPopUpButton] = [:]
    private var checks: [Int:NSButton] = [:]
    init() {
        super.init(frame:.zero)
        let title = Studio.label("Deckをカスタマイズ",14); title.frame = NSRect(x:12,y:12,width:220,height:24); addSubview(title)
        let close = StudioButton("×") { [weak self] in self?.closeDrawer?() }; close.frame = NSRect(x:246,y:8,width:30,height:30); close.identifier = NSUserInterfaceItemIdentifier("drawer-close"); close.setAccessibilityLabel("Deckのカスタマイズを閉じる"); addSubview(close)
        tabs.selectedSegment = 0; tabs.target = self; tabs.action = #selector(tabChanged); addSubview(tabs)
        scroll.documentView = body; scroll.hasVerticalScroller = true; scroll.drawsBackground = false; addSubview(scroll)
        rebuild()
    }
    required init?(coder: NSCoder) { fatalError("init(coder:)") }
    override func layout() {
        super.layout(); subviews.first { $0.identifier?.rawValue == "drawer-close" }?.frame.origin.x = width-42
        tabs.frame = NSRect(x:12,y:46,width:width-24,height:28)
        scroll.frame = NSRect(x:12,y:84,width:width-24,height:max(1,bounds.height-96))
        body.frame.size.width = max(1,scroll.contentSize.width)
        for picker in choices.values { picker.frame.size.width = max(100,body.frame.width-picker.frame.minX-4) }
        for button in checks.values { button.frame.size.width = body.frame.width }
    }
    private var width: CGFloat { max(288,bounds.width) }
    func update(_ configuration: Configuration, layout: DeckLayout, profile: String) {
        self.configuration = configuration; value = layout; self.profile = profile; refresh()
    }
    @objc private func tabChanged() { rebuild() }
    private func rebuild() {
        body.subviews.forEach { $0.removeFromSuperview() }; choices = [:]; checks = [:]
        func choice(_ title: String,_ tag: Int,_ values: [String],_ row: Int) {
            let label = Studio.label(title,12); label.frame = NSRect(x:0,y:CGFloat(row*42+4),width:122,height:24); body.addSubview(label)
            let picker = NSPopUpButton(frame:NSRect(x:124,y:CGFloat(row*42),width:138,height:28)); picker.addItems(withTitles:values); picker.tag = tag; picker.target = self; picker.action = #selector(changed(_:)); picker.setAccessibilityLabel(title); body.addSubview(picker); choices[tag] = picker
        }
        func check(_ title: String,_ tag: Int,_ row: Int) {
            let button = StudioToggle(title:title,target:self,action:#selector(checked(_:))); button.tag = tag; button.frame = NSRect(x:0,y:CGFloat(row*38),width:264,height:28); body.addSubview(button); checks[tag] = button
        }
        switch tabs.selectedSegment {
        case 0:
            choice("列数",0,(1...18).map { "\($0) 列" },0); choice("行数",1,(1...18).map { "\($0) 行" },1)
            check("プロファイル別に切り替える",10,3); check("このプロファイルの既定Deck",11,4)
        case 1:
            choice("余白",2,(4...24).map { "\($0) px" },0)
            check("名前を表示",12,2); check("ファイルの拡張子を表示",13,3); check("ホバー時アニメーション",14,4)
        default:
            choice("実行後",3,DeckDisplayBehavior.allCases.map { $0.title },0)
            choice("マウス離脱後",4,DeckDisplayBehavior.allCases.map { $0.title },1)
            check("常時表示（固定）",17,3)
            let explanation = Studio.label("固定がONの間、自動非表示は動きません。ここで固定を解除できます。",11,false)
            explanation.maximumNumberOfLines = 2; explanation.lineBreakMode = .byWordWrapping
            explanation.frame = NSRect(x:0,y:150,width:240,height:38); body.addSubview(explanation)
            check("ポインタ位置に表示",15,5); check("ファイルのホバープレビュー",16,6)
        }
        body.frame.size.height = tabs.selectedSegment == 2 ? 260 : 198; refresh(); needsLayout = true
    }
    private func refresh() {
        choices[0]?.selectItem(at:value.columns-1); choices[1]?.selectItem(at:(value.rows ?? 5)-1); choices[2]?.selectItem(at:value.padding-4)
        choices[3]?.selectItem(at:DeckDisplayBehavior.allCases.firstIndex(of:configuration.deckAfterAction ?? .stay) ?? 0)
        choices[4]?.selectItem(at:DeckDisplayBehavior.allCases.firstIndex(of:configuration.deckOnLeave ?? .stay) ?? 0)
        for (tag,state) in [(10,value.profileGroupID != nil),(11,configuration.resolvedDeck(nil,profile:profile) == value.id),(12,value.labels),(13,value.showFileExtensions == true),(14,value.hoverAnimation != false),(15,value.showAtCursor == true),(16,value.hoverPreviews != false),(17,value.pinned)] { checks[tag]?.state = state ? .on : .off }
    }
    @objc private func changed(_ sender: NSPopUpButton) {
        switch sender.tag {
        case 0: value.columns = sender.indexOfSelectedItem+1
        case 1: value.rows = sender.indexOfSelectedItem+1
        case 2: value.panelPadding = sender.indexOfSelectedItem+4
        case 3,4:
            let behavior = DeckDisplayBehavior.allCases[sender.indexOfSelectedItem]
            if behavior != .stay && value.pinned { value.pinned = false; changeLayout?(value) }
            changeBehavior?(sender.tag-3,behavior); return
        default: return
        }
        changeLayout?(value)
    }
    @objc private func checked(_ sender: NSButton) {
        let on = sender.state == .on
        switch sender.tag {
        case 10: changeLink?(on); refresh(); return
        case 11: changeDefault?(on); refresh(); return
        case 12: value.labels = on
        case 13: value.showFileExtensions = on
        case 14: value.hoverAnimation = on
        case 15: value.showAtCursor = on
        case 16: value.hoverPreviews = on
        case 17: value.pinned = on
        default: return
        }
        changeLayout?(value)
    }

    static func verifyBehaviorUI(previewPath: String) {
        let drawer = DeckSettingsDrawer(); drawer.frame = NSRect(x:0,y:0,width:288,height:440)
        var configuration = Configuration(); configuration.deckAfterAction = .hide; configuration.deckOnLeave = .hide
        var layout = DeckLayout(); layout.pinned = true
        drawer.update(configuration,layout:layout,profile:"default")
        drawer.tabs.selectedSegment = 2; drawer.rebuild(); drawer.layoutSubtreeIfNeeded()
        verifySmoke(drawer.checks[17]?.state == .on && drawer.choices[3]?.titleOfSelectedItem == "非表示" && drawer.choices[4]?.titleOfSelectedItem == "非表示","Behavior drawer shows the pin and both hide choices together")
        verifySmoke(drawer.choices.values.allSatisfy { $0.frame.maxX <= drawer.body.bounds.maxX },"Behavior menus fit beside the scroller")
        saveSmokePreview(drawer,to:previewPath)
        var changed: DeckLayout?
        drawer.changeLayout = { changed = $0 }
        drawer.choices[3]?.selectItem(at:2)
        if let picker = drawer.choices[3] { drawer.changed(picker) }
        verifySmoke(changed?.pinned == false,"Selecting automatic hide releases a conflicting pin")
    }
}
