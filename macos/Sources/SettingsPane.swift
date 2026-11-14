import AppKit

/// Category navigation reuses the actual controls; moving between pages never saves or resets settings.
final class SettingsPane: NSScrollView, NSSearchFieldDelegate {
    private let names = ["一般・起動", "入力とアクセシビリティ", "マウス", "ウィンドウ操作", "クリップボード", "バックアップとヘルプ", "キーボードと機能", "入力無効アプリ", "自動解凍"]
    private let symbols = ["gearshape", "hand.raised", "computermouse", "macwindow", "doc.on.clipboard", "externaldrive", "keyboard", "hand.raised.slash", "archivebox"]
    private let pages: [NSView]
    private let content = NSStackView()
    private let navigation = NSStackView()
    private let heading = Studio.label("一般・起動", 24)
    private let search = NSSearchField()
    private var buttons: [StudioButton] = []
    private var selectedPage = 0
    private var widthConstraints: [NSLayoutConstraint] = []
    private let empty = NSTextField(wrappingLabelWithString: "該当する設定がありません。別の言葉で検索してください。")

    init(pages: [NSView]) {
        self.pages = pages
        super.init(frame: .zero)
        precondition(pages.count == names.count)
        drawsBackground = false; hasVerticalScroller = true
        let document = StudioSurface(); documentView = document
        document.translatesAutoresizingMaskIntoConstraints = false
        let sidebar = NSStackView(); sidebar.orientation = .vertical; sidebar.alignment = .leading; sidebar.spacing = 16
        let title = Studio.label("設定", 24)
        search.placeholderString = "設定を検索"; search.delegate = self
        search.setAccessibilityLabel("設定を検索"); search.sendsSearchStringImmediately = true
        sidebar.addArrangedSubview(title); sidebar.addArrangedSubview(search)
        navigation.orientation = .vertical; navigation.alignment = .leading; navigation.spacing = 4
        sidebar.addArrangedSubview(navigation)
        for (index, name) in names.enumerated() {
            let button = StudioButton(title:name,target:self,action:#selector(selectCategory(_:)))
            button.tag = index; button.leftAligned = true; button.symbol = symbols[index]
            button.font = .systemFont(ofSize:12); button.setAccessibilityLabel(name)
            button.heightAnchor.constraint(equalToConstant:40).isActive = true
            navigation.addArrangedSubview(button); buttons.append(button)
            button.widthAnchor.constraint(equalTo:navigation.widthAnchor).isActive = true
        }
        content.orientation = .vertical; content.alignment = .leading; content.spacing = 18
        for child in [sidebar,content] { child.translatesAutoresizingMaskIntoConstraints = false; document.addSubview(child) }
        NSLayoutConstraint.activate([
            document.widthAnchor.constraint(equalTo:contentView.widthAnchor),
            sidebar.leadingAnchor.constraint(equalTo:document.leadingAnchor,constant:12),
            sidebar.topAnchor.constraint(equalTo:document.topAnchor,constant:16),
            sidebar.widthAnchor.constraint(equalToConstant:196),
            sidebar.bottomAnchor.constraint(lessThanOrEqualTo:document.bottomAnchor,constant:-24),
            search.widthAnchor.constraint(equalTo:sidebar.widthAnchor),
            navigation.widthAnchor.constraint(equalTo:sidebar.widthAnchor),
            content.leadingAnchor.constraint(equalTo:sidebar.trailingAnchor,constant:28),
            content.trailingAnchor.constraint(equalTo:document.trailingAnchor,constant:-20),
            content.topAnchor.constraint(equalTo:document.topAnchor,constant:16),
            content.bottomAnchor.constraint(lessThanOrEqualTo:document.bottomAnchor,constant:-24),
            document.heightAnchor.constraint(greaterThanOrEqualTo:contentView.heightAnchor)
        ])
        pages.forEach(Studio.themeControls)
        render()
    }
    required init?(coder: NSCoder) { fatalError("init(coder:) has not been implemented") }
    @objc private func selectCategory(_ sender: NSButton) {
        selectedPage = sender.tag; search.stringValue = ""; render()
        contentView.scroll(to:.zero); reflectScrolledClipView(contentView)
    }
    func controlTextDidChange(_ obj: Notification) { render() }
    private func searchableText(_ view: NSView) -> String {
        let own = (view as? NSTextField)?.stringValue ?? (view as? NSButton)?.title ?? ""
        return own + " " + view.subviews.map(searchableText).joined(separator:" ")
    }
    private func render() {
        NSLayoutConstraint.deactivate(widthConstraints); widthConstraints.removeAll()
        for view in content.arrangedSubviews { content.removeArrangedSubview(view); view.removeFromSuperview() }
        let term = search.stringValue.trimmingCharacters(in:.whitespacesAndNewlines)
        heading.stringValue = term.isEmpty ? names[selectedPage] : "検索結果"
        content.addArrangedSubview(heading)
        let indices = term.isEmpty ? [selectedPage] : pages.indices.filter { (names[$0] + " " + searchableText(pages[$0])).localizedCaseInsensitiveContains(term) }
        for index in indices { content.addArrangedSubview(pages[index]) }
        if indices.isEmpty { content.addArrangedSubview(empty) }

        widthConstraints = content.arrangedSubviews.map { $0.widthAnchor.constraint(equalTo:content.widthAnchor) }
        NSLayoutConstraint.activate(widthConstraints)
        for (index, button) in buttons.enumerated() { button.selected = term.isEmpty && index == selectedPage }
        needsLayout = true
    }
    func verifyNavigation() {
        let original = selectedPage
        for index in pages.indices {
            selectCategory(buttons[index]); layoutSubtreeIfNeeded()
            verifySmoke(content.arrangedSubviews.contains(pages[index]))
            verifySmoke(buttons.filter { $0.selected }.count == 1)
            if index >= 7 {
                let path = URL(fileURLWithPath:CommandLine.arguments[2]).deletingPathExtension().path + "-settings-\(index).png"
                saveSmokePreview(self,to:path)
            }
        }
        search.stringValue = "ホイール"; render()
        verifySmoke(content.arrangedSubviews.contains(pages[2]))
        search.stringValue = "no-such-setting-8392"; render()
        verifySmoke(content.arrangedSubviews.contains(empty))
        selectCategory(buttons[original])
        let baseline = content.constraints.count
        for _ in 0..<20 { selectCategory(buttons[(original+1)%pages.count]); selectCategory(buttons[original]) }
        verifySmoke(content.constraints.count == baseline,"Settings navigation must not accumulate layout constraints")
        print("PASS: settings category navigation, search and empty results reuse original controls")
    }
}
