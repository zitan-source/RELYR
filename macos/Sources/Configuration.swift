import Foundation

enum Layer: String, Codable, CaseIterable {
    case normal, space, mouseRight, mouseBack, mouseForward, capsLock, menuBar, control, trackpad
    var title: String {
        switch self {
        case .trackpad: return "トラックパッド"
        case .normal: return "通常"
        case .space: return "Space"
        case .control: return "Control"
        case .capsLock: return "CapsLock (F13)"
        case .menuBar: return "メニューバー"
        case .mouseRight: return "右クリック"
        case .mouseBack: return "戻る"
        case .mouseForward: return "進む"
        }
    }
    var prefix: String { self == .normal ? "" : title + " + " }
    var mouseCode: UInt16? {
        switch self { case .mouseRight: return 257; case .mouseBack: return 259; case .mouseForward: return 260; default: return nil }
    }
}
enum Trigger: String, Codable, CaseIterable { case tap, hold }
enum KeyboardLayout: String, Codable, CaseIterable { case jis = "JIS", us = "US" }
enum InputPolicy {
    static func allows(_ code: UInt16, layer: Layer, trigger: Trigger = .tap) -> Bool {
        guard Keys.inputs.contains(where:{ $0.code == code }) else { return false }
        if layer == .trackpad { return trigger == .tap && ((270...273).contains(code) || (400...441).contains(code)) }
        if (400...441).contains(code) { return false }
        return !(layer == .control && code >= 256) && !(layer == .menuBar && (code == 256 || (code == 257 && trigger == .tap))) && !(layer == .normal && code == 256) && layer.mouseCode != code && !(trigger == .hold && (270...273).contains(code))
    }
}
enum SystemCommand: String, Codable, CaseIterable {
    case clipboardHistory, toggleAutoExtract, keyboardCleaner
    case wifiOn, wifiOff, wifiToggle, numpad, extendedKeypad
    case volumeUp, volumeDown, mute, micMute
    case mediaPlayPause, mediaNext, mediaPrevious, brightnessUp, brightnessDown
    case scrollUp, scrollDown, scrollLeft, scrollRight, showRelyr, navigateBack, navigateForward
    case finderExisting, finderNewWindow
    case lockScreen, showDesktop, emoji, inputSource, voiceOver, zoomToggle, zoomIn, zoomOut
    case screenshotFull, screenshotArea, screenshotPanel, screenshotFullClipboard, screenshotAreaClipboard
    case desktopLeft, desktopRight, missionControl, appWindows, spotlight
    case desktop1, desktop2, desktop3, desktop4, desktop5, desktop6, desktop7, desktop8, desktop9
    var desktopNumber: Int? { Int(rawValue.replacingOccurrences(of:"desktop",with:"")) }
    func shortcut(layout: KeyboardLayout) -> (UInt16, Modifiers)? {
        if self == .navigateBack { return (layout == .jis ? 30 : 33,.command) }
        if self == .navigateForward { return (layout == .jis ? 42 : 30,.command) }
        return shortcut
    }
    var shortcut: (UInt16, Modifiers)? {
        if let number = desktopNumber { return ([18,19,20,21,23,22,26,28,25][number - 1],.control) }
        switch self {
        case .screenshotFull: return (20,[.command,.shift])
        case .screenshotArea: return (21,[.command,.shift])
        case .screenshotPanel: return (23,[.command,.shift])
        case .screenshotFullClipboard: return (20,[.command,.shift,.control])
        case .screenshotAreaClipboard: return (21,[.command,.shift,.control])
        case .desktopLeft: return (123,.control)
        case .desktopRight: return (124,.control)
        case .missionControl: return (126,.control)
        case .appWindows: return (125,.control)
        case .spotlight: return (49,.command)
        case .navigateBack: return (33,.command)
        case .navigateForward: return (30,.command)
        case .lockScreen: return (12,[.command,.control])
        case .showDesktop: return (99,.command)
        case .emoji: return (49,[.command,.control])
        case .inputSource: return (49,.control)
        case .voiceOver: return (96,.command)
        case .zoomToggle: return (28,[.command,.option])
        case .zoomIn: return (24,[.command,.option])
        case .zoomOut: return (27,[.command,.option])
        default: return nil
        }
    }
    var category: String {
        switch self {
        case .volumeUp,.volumeDown,.mute,.micMute: return "音量"
        case .mediaPlayPause,.mediaNext,.mediaPrevious: return "メディア"
        case .scrollUp,.scrollDown,.scrollLeft,.scrollRight: return "マウス"
        case .navigateBack,.navigateForward: return "ブラウザー"
        case .emoji,.inputSource,.voiceOver,.zoomToggle,.zoomIn,.zoomOut: return "アクセシビリティ"
        case .keyboardCleaner,.brightnessUp,.brightnessDown,.lockScreen,.showRelyr,.wifiOn,.wifiOff,.wifiToggle: return "Mac基本操作"
        case .clipboardHistory,.toggleAutoExtract: return "文書・編集"
        case .numpad,.extendedKeypad: return "Deckパネル"
        case .screenshotFull,.screenshotArea,.screenshotPanel,.screenshotFullClipboard,.screenshotAreaClipboard: return "スクリーンショット"
        default: return "デスクトップ"
        }
    }
    var title: String {
        if let number = desktopNumber { return "デスクトップ\(number)へ切替" }
        switch self {
        case .desktop1,.desktop2,.desktop3,.desktop4,.desktop5,.desktop6,.desktop7,.desktop8,.desktop9: return "デスクトップへ切替"
        case .screenshotFull: return "画面全体を撮影"
        case .screenshotArea: return "範囲を選んで撮影"
        case .screenshotPanel: return "スクリーンショット・画面収録パネル"
        case .screenshotFullClipboard: return "画面全体をクリップボードへ"
        case .screenshotAreaClipboard: return "スクショ：範囲指定してコピー（すぐ貼り付け）"
        case .desktopLeft: return "左のデスクトップへ切替"
        case .desktopRight: return "右のデスクトップへ切替"
        case .missionControl: return "Mission Control"
        case .appWindows: return "アプリのウィンドウ一覧"
        case .spotlight: return "Spotlight"
        case .finderExisting: return "Finder：既存ウィンドウを表示"
        case .finderNewWindow: return "Finder：現在のデスクトップに新規ウィンドウ"
        case .volumeUp: return "音量を上げる"
        case .volumeDown: return "音量を下げる"
        case .mute: return "消音／解除"
        case .keyboardCleaner: return "キーボードクリーナー"
        case .clipboardHistory: return "クリップボード履歴（文字・画像）"
        case .wifiOn: return "Wi-FiをON"
        case .wifiOff: return "Wi-FiをOFF"
        case .wifiToggle: return "Wi-Fiを切り替え"
        case .numpad: return "テンキーを表示／非表示"
        case .extendedKeypad: return "ナビゲーション・方向キー・テンキー"
        case .micMute: return "マイクを消音／解除"
        case .mediaPlayPause: return "再生／一時停止"
        case .mediaNext: return "次の曲"
        case .mediaPrevious: return "前の曲"
        case .brightnessUp: return "画面の輝度（明るさ）を上げる"
        case .brightnessDown: return "画面の輝度（明るさ）を下げる"
        case .scrollUp: return "ホイール上"
        case .scrollDown: return "ホイール下"
        case .scrollLeft: return "横スクロール左"
        case .scrollRight: return "横スクロール右"
        case .showRelyr: return "RELYRを表示"
        case .toggleAutoExtract: return "自動解凍を切り替え"
        case .navigateBack: return "戻る（ページ・フォルダ）"
        case .navigateForward: return "進む（ページ・フォルダ）"
        case .lockScreen: return "画面をロック"
        case .showDesktop: return "デスクトップを表示（⌘F3）"
        case .emoji: return "絵文字・記号を表示"
        case .inputSource: return "入力ソースを切り替え（⌃Space）"
        case .voiceOver: return "VoiceOverを切り替え（⌘F5）"
        case .zoomToggle: return "画面ズームを切り替え"
        case .zoomIn: return "画面ズームを拡大"
        case .zoomOut: return "画面ズームを縮小"
        }
    }
}
enum ActionType: String, Codable, CaseIterable {
    case key, shortcut, launch, text, url, deck, window, file, system, mouse, gesture, macro, profile, disabled
    var isKey: Bool { self == .key || self == .shortcut }
    var title: String {
        switch self {
        case .profile: return "プロファイル切替"
        case .disabled: return "入力を無効化"
        case .macro: return "マクロ"
        case .gesture: return "ジェスチャー"
        case .key: return "キー"
        case .shortcut: return "ショートカット"
        case .launch: return "アプリ起動"
        case .text: return "文字列入力"
        case .url: return "URLを開く"
        case .deck: return "Deckパネル表示／非表示"
        case .window: return "ウィンドウ操作"
        case .file: return "ファイル／フォルダー"
        case .mouse: return "マウスクリック"
        case .system: return "システム操作"
        }
    }
}

struct Modifiers: OptionSet, Codable, Equatable {
    let rawValue: UInt
    static let command = Modifiers(rawValue: 1)
    static let control = Modifiers(rawValue: 2)
    static let option = Modifiers(rawValue: 4)
    static let shift = Modifiers(rawValue: 8)
    static let fn = Modifiers(rawValue: 16)
    static let all: Modifiers = [.command, .control, .option, .shift, .fn]
    var label: String {
        [(Self.fn, "fn "), (Self.control, "⌃"), (.option, "⌥"), (.shift, "⇧"), (.command, "⌘")]
            .filter { contains($0.0) }.map { $0.1 }.joined()
    }
}

struct Mapping: Codable, Equatable {
    var resolvedWindowShortcut: Mapping {
        guard action == .shortcut, modifiers == [.control,.fn] else { return self }
        let command: WindowCommand?
        switch output { case 123: command = .snapLeft; case 124: command = .snapRight; case 3: command = .maximize; case 15: command = .restoreOrMinimize; default: command = nil }
        guard let command = command else { return self }
        var result = self; result.action = .window; result.payload = command.rawValue; result.modifiers = []; return result
    }
    var layer: Layer
    var input: UInt16
    var action: ActionType
    var output: UInt16 = 0
    var modifiers: Modifiers = []
    var application: String = ""
    var payload: String = ""
    var trigger: Trigger = .tap
    var holdMilliseconds: Int = 500
    init(layer: Layer, input: UInt16, action: ActionType, output: UInt16 = 0,
         modifiers: Modifiers = [], application: String = "", payload: String = "", trigger: Trigger = .tap, holdMilliseconds: Int = 500) {
        self.layer = layer; self.input = input; self.action = action; self.output = output
        self.modifiers = modifiers; self.application = application; self.payload = payload
        self.trigger = trigger; self.holdMilliseconds = holdMilliseconds
    }
    private enum CodingKeys: String, CodingKey { case layer, input, action, output, modifiers, application, payload, trigger, holdMilliseconds }
    init(from decoder: Decoder) throws {
        let values = try decoder.container(keyedBy: CodingKeys.self)
        layer = try values.decode(Layer.self, forKey: .layer)
        input = try values.decode(UInt16.self, forKey: .input)
        action = try values.decode(ActionType.self, forKey: .action)
        output = try values.decode(UInt16.self, forKey: .output)
        modifiers = try values.decode(Modifiers.self, forKey: .modifiers)
        application = try values.decode(String.self, forKey: .application)
        payload = try values.decodeIfPresent(String.self, forKey: .payload) ?? ""
        trigger = try values.decodeIfPresent(Trigger.self, forKey: .trigger) ?? .tap
        holdMilliseconds = try values.decodeIfPresent(Int.self, forKey: .holdMilliseconds) ?? 500
    }
    var targetDescription: String { targetDescription(layout: .us) }
    func targetDescription(layout: KeyboardLayout) -> String {
        switch action {
        case .profile: return "プロファイル切替"
        case .disabled: return "入力を無効化"
        case .macro: return "マクロ"
        case .gesture: return "ジェスチャー（上下左右・中央）"
        case .mouse: return modifiers.label + (output <= 4 ? Keys.name(256 + output) : "マウスボタン番号 \(output) を押す")
        case .key, .shortcut: return modifiers.label + Keys.name(output, layout: layout)
        case .launch: return URL(fileURLWithPath: application).deletingPathExtension().lastPathComponent
        case .text: return "文字列：" + payload.prefix(24).replacingOccurrences(of: "\n", with: " ↵ ")
        case .url: return URL(string: payload)?.host ?? "URL"
        case .deck: return "Deckパネル表示／非表示"
        case .system: return modifiers.label + (SystemCommand(rawValue: payload)?.title ?? "システム操作")
        case .window: return WindowCommand(rawValue: payload)?.title ?? "ウィンドウ操作"
        case .file: return URL(fileURLWithPath: application).lastPathComponent
        }
    }
    var description: String { description(layout: .us) }
    func description(layout: KeyboardLayout) -> String {
        let source = layer.prefix + Keys.name(input, layout: layout)
        return "\(source)\(trigger == .hold ? " [HOLD]" : "")  →  \(targetDescription(layout: layout))"
    }
}

struct Profile: Codable, Equatable {
    var id = UUID().uuidString
    var name: String
    var bundleIdentifiers: [String] = []
    var mappings: [Mapping] = []
}

enum DeckMonitor: String, Codable, CaseIterable {
    case clock, date, cpu, memory, disk, autoExtract, uptime, battery, brightness, volume, microphone, wifi, bluetooth, networkUp, networkDown, networkStatus, timer
    var title: String {
        switch self {
        case .clock: return "時計"; case .date: return "日付"; case .cpu: return "CPU"; case .memory: return "メモリ"; case .disk: return "ディスク空き"; case .autoExtract: return "自動解凍 ON／OFF"
        case .uptime: return "連続稼働時間"; case .battery: return "バッテリー残量"; case .brightness: return "画面の輝度"; case .volume: return "音量"; case .microphone: return "マイク"; case .wifi: return "Wi-Fi"; case .bluetooth: return "Bluetooth"
        case .networkUp: return "ネットワーク送信"; case .networkDown: return "ネットワーク受信"; case .networkStatus: return "ネットワーク状態"; case .timer: return "タイマー"
        }
    }
    var caption: String {
        switch self {
        case .clock: return "CLOCK"; case .date: return "DATE"; case .cpu: return "CPU"; case .memory: return "RAM"; case .disk: return "SSD FREE"; case .autoExtract: return "EXTRACT"
        case .uptime: return "UPTIME"; case .battery: return "BATTERY"; case .brightness: return "BRIGHTNESS"; case .volume: return "VOLUME"; case .microphone: return "MIC"; case .wifi: return "WI-FI"; case .bluetooth: return "BLUETOOTH"
        case .networkUp: return "UPLOAD"; case .networkDown: return "DOWNLOAD"; case .networkStatus: return "NETWORK"; case .timer: return "TIMER"
        }
    }
}
struct DeckCountdown {
    private(set) var deadline: TimeInterval?
    mutating func start(seconds: Double, now: TimeInterval) -> Bool {
        guard seconds.isFinite,(1...86400).contains(seconds),now.isFinite else { return false }
        deadline = now+seconds; return true
    }
    mutating func cancel() { deadline = nil }
    func remaining(now: TimeInterval) -> Double { deadline.map { max(0,$0-now) } ?? 0 }
    mutating func consumeCompletion(now: TimeInterval) -> Bool {
        guard let deadline = deadline,now >= deadline else { return false }; self.deadline = nil; return true
    }
    func label(now: TimeInterval) -> String {
        guard deadline != nil else { return "READY" }
        let seconds = Int(ceil(remaining(now:now)))
        return seconds >= 3600 ? String(format:"%d:%02d:%02d",seconds/3600,seconds/60%60,seconds%60) : String(format:"%02d:%02d",seconds/60,seconds%60)
    }
}
enum DeckDisplayBehavior: String, Codable, CaseIterable {
    case stay, collapse, hide
    var title: String { self == .stay ? "表示を維持" : (self == .collapse ? "端に折り畳む" : "非表示") }
}
struct DeckBounds: Codable, Equatable {
    var x: Double, y: Double, width: Double, height: Double
    var valid: Bool { [x,y,width,height].allSatisfy { $0.isFinite && abs($0) < 100000 } && width >= 24 && height >= 24 }
}
struct DeckLayout: Codable, Equatable {
    var id = UUID().uuidString
    var name = "Deck"
    var columns = 6
    var labels = true
    var pinned = false
    var expanded: DeckBounds?
    var collapsed: DeckBounds?
    var showAtCursor: Bool? = nil
    var panelPadding: Int? = nil
    var rows: Int? = nil
    var hoverPreviews: Bool? = nil
    var showFileExtensions: Bool? = nil
    var hoverAnimation: Bool? = nil
    var profileGroupID: String? = nil
    var linkedProfileID: String? = nil
    var padding: Int { panelPadding ?? 12 }
}

/// Shared editor/overlay dimensions, matching Windows DeckPanelLayout.
enum DeckMetrics {
    static let width = 54.0, height = 52.0, labelHeight = 14.0
    static func gap(labels: Bool) -> Double { labels ? 18 : 4 }
    static func stepX(labels: Bool) -> Double { width + gap(labels: labels) }
    static func stepY(labels: Bool) -> Double { height + gap(labels: labels) }
}

struct DeckRenderState: Equatable {
    let item: DeckItem
    let mapping: Mapping
    let keyboardLayout: KeyboardLayout
    let profileName: String
}

struct DeckItem: Codable, Equatable {
    var id = UUID().uuidString
    var title: String
    var profileID: String
    var layer: Layer
    var input: UInt16
    var trigger: Trigger? = nil
    var directAction: Mapping? = nil
    var colorHex: String? = nil
    var layoutID: String? = nil
    var iconPNG: Data? = nil
    var monitor: DeckMonitor? = nil
    var slot: Int? = nil
}

enum ConfigurationError: LocalizedError {
    case invalid(String)
    var errorDescription: String? {
        switch self { case .invalid(let message): return message }
    }
}

struct RecoveryBackoff {
    private(set) var failures = 0
    private(set) var retryAt: TimeInterval = 0
    mutating func failed(now: TimeInterval) {
        failures = min(failures+1,5)
        retryAt = now + min(5,0.5 * pow(2,Double(failures-1)))
    }
    mutating func reset() { failures = 0; retryAt = 0 }
    func ready(now: TimeInterval) -> Bool { now >= retryAt }
}

enum MacroPhase: String, Codable, CaseIterable { case pulse, down, up, move }
struct MacroStep: Codable, Equatable {
    var id = UUID().uuidString
    var delayMilliseconds = 0
    var action: Mapping? = nil
    var phase: MacroPhase = .pulse
    var x = 0.0, y = 0.0
    var relative = true
}
struct MacroDefinition: Codable, Equatable {
    var id = UUID().uuidString
    var name = "新しいマクロ"
    var steps: [MacroStep] = []
}
enum MacroPlan {
    static func expand(_ id: String, definitions: [MacroDefinition]) throws -> [MacroStep] {
        var result: [MacroStep] = []
        func append(_ step: MacroStep) throws {
            guard result.count < 10000 else { throw ConfigurationError.invalid("マクロは展開後10000手順までです。") }
            result.append(step)
        }
        func visit(_ id: String, chain: Set<String>) throws {
            guard chain.count < 16, !chain.contains(id), let macro = definitions.first(where: { $0.id == id }) else { throw ConfigurationError.invalid("マクロの循環参照・16段を超える呼び出し・参照切れがあります。") }
            for step in macro.steps {
                if let action = step.action, action.action == .macro {
                    if step.delayMilliseconds > 0 { try append(MacroStep(delayMilliseconds:step.delayMilliseconds)) }
                    try visit(action.payload,chain:chain.union([id]))
                } else { try append(step) }
            }
        }
        try visit(id,chain:[])
        return result
    }
}
/// Monotonic, cancellable schedule; no OS input or timers in this model.
struct MacroPlayback {
    let steps: [MacroStep]
    private(set) var index = 0
    private(set) var deadline: TimeInterval
    private(set) var cancelled = false
    init(steps: [MacroStep], now: TimeInterval) {
        self.steps = steps; deadline = now + Double(steps.first?.delayMilliseconds ?? 0)/1000
    }
    var finished: Bool { cancelled || index >= steps.count }
    mutating func cancel() { cancelled = true }
    mutating func next(now: TimeInterval) -> MacroStep? {
        guard !finished, now >= deadline else { return nil }
        let step = steps[index]; index += 1
        deadline = now + Double(index < steps.count ? steps[index].delayMilliseconds : 0)/1000
        return step
    }
}

struct GestureDefinition: Codable, Equatable {
    var id = UUID().uuidString
    var name = "新しいジェスチャー"
    var threshold = 12
    var locksCursor = true
    var actions: [String: Mapping] = [:]
}

/// Windows: four equal sectors, 110ms move-stop segments, repeat while held.
struct GestureSession {
    let definition: GestureDefinition
    let source: UInt16
    var dx = 0.0, dy = 0.0
    var lastMotion: TimeInterval
    var moved = false
    mutating func move(x: Double, y: Double, now: TimeInterval) {
        dx += x; dy += y; lastMotion = now
    }
    mutating func commit() -> Mapping? {
        let x = dx, y = dy
        guard max(abs(x),abs(y)) >= Double(definition.threshold) else { return nil }
        dx = 0; dy = 0
        moved = true
        let direction = abs(x) >= abs(y) ? (x >= 0 ? "right" : "left") : (y >= 0 ? "down" : "up")
        return definition.actions[direction]
    }
    mutating func finish() -> Mapping? {
        let final = commit()
        return moved ? final : definition.actions["center"]
    }
}

struct DisabledApplication: Codable, Equatable {
    var name: String
    var bundleIdentifier: String
}
struct ArchiveSettings: Codable, Equatable {
    var enabled = false
    var watchFolder = ""
    var destinationFolder = ""
    var trashAfterExtraction = false
}
// Positions use a 600 × 400 mouse canvas; source is a physical button or wheel direction.
struct MouseLayoutItem: Codable, Equatable {
    var source: Int
    var x: Double
    var y: Double
    var width: Double = 96
    var height: Double = 96
    static func snapped(_ value: Double) -> Double { (value / 12).rounded() * 12 }
    var squared: MouseLayoutItem {
        let side = min(396, max(96, Self.snapped(min(width,height))))
        return MouseLayoutItem(source:source,
            x:min(600-side,max(0,Self.snapped(x))),
            y:min(floor((400-side)/12)*12,max(0,Self.snapped(y))),width:side,height:side)
    }
    var valid: Bool {
        ((0...31).contains(source) || (270...273).contains(source) || source == 340) &&
        [x,y,width,height].allSatisfy { $0.isFinite } &&
        x >= 0 && y >= 0 && width >= 96 && height >= 48 && x + width <= 600.01 && y + height <= 400.01
    }
}
struct MouseButtonLayout: Codable, Equatable {
    var name: String
    var items: [MouseLayoutItem]
    static func standard(_ index: Int, forward: Int, back: Int) -> MouseButtonLayout {
        MouseButtonLayout(name:"マウス \(index + 1)", items:[
            MouseLayoutItem(source:0,x:144,y:0),
            MouseLayoutItem(source:1,x:360,y:0),
            MouseLayoutItem(source:2,x:252,y:108),
            MouseLayoutItem(source:forward,x:144,y:108),
            MouseLayoutItem(source:back,x:144,y:216),
            MouseLayoutItem(source:270,x:252,y:0),
            MouseLayoutItem(source:271,x:252,y:216),
            MouseLayoutItem(source:272,x:360,y:108),
            MouseLayoutItem(source:273,x:360,y:216)])
    }
}

// A mouse-specific assignment replaces one existing slot without changing
// keyboard assignments or the foreground application's profile.
struct MouseDeviceOverride: Codable, Equatable {
    var deviceID: String
    var profileID: String
    var layer: Layer
    var input: UInt16
    var trigger: Trigger
    var mapping: Mapping? // nil deliberately leaves this slot unassigned.
    static func supports(_ layer: Layer, _ input: UInt16) -> Bool {
        (256...340).contains(input) || layer == .mouseRight || layer == .mouseBack || layer == .mouseForward
    }
}

struct MouseLayerButtons: Codable, Equatable {
    var forward: Int
    var back: Int

    var isValid: Bool {
        (3...31).contains(forward) && (3...31).contains(back) && forward != back
    }
}

struct Configuration: Codable, Equatable {
    var disabledApplications: [DisabledApplication]? = nil
    var archiveSettings: ArchiveSettings? = nil
    func disablesInput(bundleIdentifier: String?) -> Bool {
        guard let id = bundleIdentifier, !id.isEmpty else { return false }
        return disabledApplications?.contains { $0.bundleIdentifier == id } == true
    }

    var macros: [MacroDefinition] = []
    var gestures: [GestureDefinition] = []
    var startInputOnLaunch: Bool? = nil
    var loginLaunchDesired: Bool? = nil
    var resumeInputOnLaunch: Bool? = nil
    var version = 2
    var mouseButtonLayouts: [MouseButtonLayout]? = nil
    var selectedMouseButtonLayout: Int? = nil
    var commonMouseButtonLayout: MouseButtonLayout? = nil
    var mouseButtonLayoutsByDevice: [String:MouseButtonLayout]? = nil
    var mouseDeviceOverrides: [MouseDeviceOverride]? = nil
    var mouseDeviceNames: [String:String]? = nil
    var mouseLayerButtonsByDevice: [String:MouseLayerButtons]? = nil
    func mouseButtonLayout(for deviceID: String?) -> MouseButtonLayout? {
        if let deviceID = deviceID, let layout = mouseButtonLayoutsByDevice?[deviceID] { return layout }
        if let layout = commonMouseButtonLayout { return layout }
        guard let index = selectedMouseButtonLayout, let layouts = mouseButtonLayouts, layouts.indices.contains(index) else { return nil }
        return layouts[index]
    }
    var typingPriority: Bool? = nil
    var spaceEnabled = true
    var keyboardLayout: KeyboardLayout = .jis
    var trackpadEdgeWidth: Double? = nil
    var edgeWidth: Double { trackpadEdgeWidth ?? 0.12 }
    var menuBarTrackpadEnabled: Bool? = nil
    var reversedWheelLayers: [Layer]? = nil
    var reverseMouseScroll: Bool? = nil
    var reversesMouseScroll: Bool { reverseMouseScroll ?? !(reversedWheelLayers ?? []).isEmpty }
    func reversesWheel(in layer: Layer) -> Bool { reversesMouseScroll }
    var mouseButtonNames: [String: String]? = nil
    func inputName(_ code: UInt16) -> String { mouseButtonNames?[String(code)] ?? Keys.name(code,layout:keyboardLayout) }
    func inputDetail(_ code: UInt16, deviceID: String? = nil) -> String {
        guard let physical = (0...31).first(where: { mouseInput(Int64($0), deviceID: deviceID) == code }) else { return inputName(code) }
        return "\(inputName(code))（macOSボタン番号 \(physical)）"
    }
    var forwardMouseButton: Int? = nil
    var backMouseButton: Int? = nil
    var forwardButton: Int { forwardMouseButton ?? 4 }
    var backButton: Int { backMouseButton ?? 3 }
    func layerButtons(for deviceID: String?) -> MouseLayerButtons {
        deviceID.flatMap { mouseLayerButtonsByDevice?[$0] } ?? MouseLayerButtons(forward: forwardButton, back: backButton)
    }
    func mouseInput(_ physical: Int64, deviceID: String? = nil) -> UInt16? {
        let buttons = layerButtons(for: deviceID)
        if physical == 32 { return 340 } // MX Master 4 HID++ wheel-mode button, not an OS mouse number.
        if physical == Int64(buttons.forward) { return 260 }
        if physical == Int64(buttons.back) { return 259 }
        if (0...2).contains(physical) { return UInt16(256 + physical) }
        return (3...31).contains(physical) ? UInt16(300 + physical) : nil
    }
    mutating func registerMouseButton(_ physical: Int, forward: Bool, deviceID: String? = nil) {
        guard (3...31).contains(physical) else { return }
        var buttons = layerButtons(for: deviceID)
        if forward {
            if physical == buttons.back { buttons.back = buttons.forward }
            buttons.forward = physical
        } else {
            if physical == buttons.forward { buttons.forward = buttons.back }
            buttons.back = physical
        }
        if let deviceID = deviceID {
            mouseLayerButtonsByDevice = mouseLayerButtonsByDevice ?? [:]
            mouseLayerButtonsByDevice?[deviceID] = buttons
        } else {
            forwardMouseButton = buttons.forward
            backMouseButton = buttons.back
        }
    }
    var clipboardHistoryEnabled: Bool? = nil
    var windowTarget: WindowTargetMode? = nil
    // The original mappings remain the standard profile, preserving v1 assignments exactly.
    var mappings: [Mapping] = []
    var profiles: [Profile] = []
    var manualProfileID = "default"
    var automaticProfiles = false
    var deck: [DeckItem] = []
    var deckLayouts: [DeckLayout]? = nil
    var deckDefaults: [String:String]? = nil
    var deckAfterAction: DeckDisplayBehavior? = nil
    var deckOnLeave: DeckDisplayBehavior? = nil
    var deckLayoutIDs: [String] { ["default"] + (deckLayouts ?? []).filter { $0.id != "default" }.map { $0.id } }
    func deckLayout(_ id: String) -> DeckLayout {
        (deckLayouts ?? []).first { $0.id == id } ?? DeckLayout(id:"default",name:"標準 Deck",columns:columns,labels:showsDeckLabels)
    }
    func visibleDeckIDs(profile: String) -> [String] {
        deckLayoutIDs.filter { deckLayout($0).linkedProfileID == nil || deckLayout($0).linkedProfileID == profile }
    }
    func resolvedDeck(_ requested: String?, profile: String) -> String? {
        let id = requested ?? deckDefaults?[profile] ?? deckDefaults?["*"] ?? "default"
        if deckLayoutIDs.contains(id) {
            let layout = deckLayout(id)
            if let group = layout.profileGroupID {
                if let variant = (deckLayouts ?? []).first(where:{ $0.profileGroupID == group && $0.linkedProfileID == profile }) { return variant.id }
            } else { return id }
        }
        return requested == nil ? visibleDeckIDs(profile:profile).first : nil
    }
    mutating func enableDeckProfiles(_ id: String, profile: String) throws {
        guard deckLayoutIDs.contains(id),(["default"]+profiles.map { $0.id }).contains(profile) else { throw ConfigurationError.invalid("Deckまたはプロファイルが見つかりません。") }
        var layout = deckLayout(id)
        guard layout.profileGroupID == nil else { return }
        layout.profileGroupID = UUID().uuidString; layout.linkedProfileID = profile; setDeckLayout(layout)
        try synchronizeDeckProfiles()
    }
    /// New profiles receive empty variants, matching Windows; existing content stays independent.
    mutating func synchronizeDeckProfiles() throws {
        let valid = Set(["default"]+profiles.map { $0.id })
        for layout in deckLayouts ?? [] where layout.linkedProfileID.map({ !valid.contains($0) }) == true {
            if layout.id == "default" { var shared = layout; shared.profileGroupID = nil; shared.linkedProfileID = nil; setDeckLayout(shared) }
            else { removeDeckLayout(layout.id,replacement:"default") }
        }
        let groups = Dictionary(grouping:(deckLayouts ?? []).filter { $0.profileGroupID != nil },by:{ $0.profileGroupID! })
        for (_,variants) in groups {
            guard let template = variants.first else { continue }
            for profile in valid.sorted() where !variants.contains(where:{ $0.linkedProfileID == profile }) {
                guard deckLayoutIDs.count < 16 else { throw ConfigurationError.invalid("プロファイル別Deckを作るには空きが必要です（最大16枚）。") }
                var copy = template; copy.id = UUID().uuidString; copy.linkedProfileID = profile
                copy.expanded = nil; copy.collapsed = nil; copy.pinned = false; setDeckLayout(copy)
            }
        }
        deckDefaults = deckDefaults?.filter { ($0.key == "*" || valid.contains($0.key)) && deckLayoutIDs.contains($0.value) }
    }
    @discardableResult mutating func disableDeckProfiles(_ id: String) -> String {
        let source = deckLayout(id)
        guard let group = source.profileGroupID else { return id }
        let variants = (deckLayouts ?? []).filter { $0.profileGroupID == group }
        var shared = variants.first { $0.linkedProfileID == "default" } ?? source
        shared.profileGroupID = nil; shared.linkedProfileID = nil; setDeckLayout(shared)
        for variant in variants where variant.id != shared.id { removeDeckLayout(variant.id,replacement:shared.id) }
        if deckDefaults == nil { deckDefaults = [:] }; deckDefaults?["*"] = shared.id
        return shared.id
    }
    mutating func removeDeckLayout(_ id: String, replacement: String) {
        guard id != replacement else { return }
        func remap(_ original: Mapping) -> Mapping { var value = original; if value.action == .deck && value.payload == id { value.payload = replacement }; return value }
        deck.removeAll { ($0.layoutID ?? "default") == id }; deckLayouts?.removeAll { $0.id == id }
        mappings = mappings.map(remap)
        for index in profiles.indices { profiles[index].mappings = profiles[index].mappings.map(remap) }
        for index in deck.indices { deck[index].directAction = deck[index].directAction.map(remap) }
        for index in macros.indices { for step in macros[index].steps.indices { macros[index].steps[step].action = macros[index].steps[step].action.map(remap) } }
        for index in gestures.indices { gestures[index].actions = gestures[index].actions.mapValues(remap) }
        deckDefaults = deckDefaults?.mapValues { $0 == id ? replacement : $0 }
    }
    func deckItems(_ id: String) -> [DeckItem] { deck.filter { ($0.layoutID ?? "default") == id } }
    func deckTitle(_ item: DeckItem) -> String {
        if let mapping = mapping(for:item),mapping.action == .launch {
            let original = URL(fileURLWithPath:mapping.application).deletingPathExtension().lastPathComponent
            // Shorten generated labels only; never rewrite the user's custom title.
            if item.title == original {
                let names = ["Microsoft Word":"Word","Microsoft Excel":"Excel","Microsoft PowerPoint":"PowerPoint","Microsoft Outlook":"Outlook","Microsoft OneNote":"OneNote"]
                if let concise = names[original] { return concise }
            }
        }

        guard deckLayout(item.layoutID ?? "default").showFileExtensions == true,let mapping = mapping(for:item),mapping.action == .file else { return item.title }
        let url = URL(fileURLWithPath:mapping.application)
        return item.title == url.deletingPathExtension().lastPathComponent ? url.lastPathComponent : item.title
    }
    func deckPositions(_ id: String) -> [String:Int] {
        let items = deckItems(id)
        var result = Dictionary(uniqueKeysWithValues:items.compactMap { item in item.slot.map { (item.id,$0) } })
        var used = Set(result.values), next = 0
        for item in items where result[item.id] == nil {
            while used.contains(next) { next += 1 }
            result[item.id] = next; used.insert(next)
        }
        return result
    }
    func deckSlotCount(_ id: String) -> Int {
        let layout = deckLayout(id)
        if let rows = layout.rows { return layout.columns * rows }
        let minimum = layout.columns * 5
        let occupied = (deckPositions(id).values.max() ?? -1)+1
        return min(324,max(minimum,((occupied+layout.columns-1)/layout.columns)*layout.columns))
    }
    func deckMoveDestinations(_ ids: Set<String>, layout: String, positions: [String:Int], anchoredAt anchor: String, toSlot target: Int) -> [(id: String, slot: Int)]? {
        let count = deckSlotCount(layout)
        let columns = deckLayout(layout).columns
        guard columns > 0, ids.contains(anchor), let origin = positions[anchor],
              (0..<count).contains(target), origin != target else { return nil }

        let offset = target - origin
        let columnOffset = target % columns - origin % columns
        var destinations: [(id: String, slot: Int)] = []
        for id in ids {
            guard let source = positions[id], (0..<count).contains(source),
                  (0..<columns).contains(source % columns + columnOffset),
                  (0..<count).contains(source + offset) else { return nil }
            destinations.append((id, source + offset))
        }
        return destinations.sorted { $0.slot < $1.slot }
    }

    mutating func moveDeck(_ ids: Set<String>, layout: String, anchoredAt anchor: String, toSlot target: Int) -> Bool {
        let positions = deckPositions(layout)
        guard let destinations = deckMoveDestinations(ids, layout: layout, positions: positions, anchoredAt: anchor, toSlot: target) else { return false }
        let sources = Set(ids.compactMap { positions[$0] })
        let targets = Set(destinations.map(\.slot))
        let vacancies = sources.subtracting(targets).sorted()
        let displacedSlots = targets.subtracting(sources).sorted()
        let itemsBySlot = Dictionary(uniqueKeysWithValues: positions.map { ($0.value, $0.key) })

        var changed = positions
        for (id, slot) in destinations { changed[id] = slot }
        for (source, destination) in zip(displacedSlots, vacancies) {
            if let id = itemsBySlot[source] { changed[id] = destination }
        }
        for index in deck.indices where positions[deck[index].id] != nil { deck[index].slot = changed[deck[index].id] }
        return true
    }
    mutating func setDeckLayout(_ layout: DeckLayout) {
        var layouts = deckLayouts ?? []
        if let index = layouts.firstIndex(where: { $0.id == layout.id }) { layouts[index] = layout } else { layouts.append(layout) }
        deckLayouts = layouts
        if layout.id == "default" { deckColumns = layout.columns; deckLabels = layout.labels }
    }
    func ignoringDeckPresentation() -> Configuration {
        var result = self
        result.deckColumns = columns; result.deckLabels = showsDeckLabels
        result.deckLayouts = result.deckLayouts?.map { value in var copy = value; copy.expanded = nil; copy.collapsed = nil; copy.pinned = false; return copy }
        result.deckLayouts?.removeAll { $0.id == "default" && $0.name == "標準 Deck" && $0.columns == columns && $0.labels == showsDeckLabels && $0.showAtCursor == nil && $0.panelPadding == nil && $0.rows == nil && $0.profileGroupID == nil && $0.linkedProfileID == nil && $0.hoverPreviews == nil && $0.showFileExtensions == nil && $0.hoverAnimation == nil }
        if result.deckLayouts?.isEmpty == true { result.deckLayouts = nil }
        return result
    }
    func preservingDeckPresentation(from current: Configuration) -> Configuration {
        var result = self
        for existing in current.deckLayouts ?? [] where result.deckLayoutIDs.contains(existing.id) {
            var layout = result.deckLayout(existing.id)
            layout.expanded = existing.expanded; layout.collapsed = existing.collapsed; layout.pinned = existing.pinned
            if layout != result.deckLayout(existing.id) { result.setDeckLayout(layout) }
        }
        return result
    }
    var deckColumns: Int? = nil
    var deckLabels: Bool? = nil
    var columns: Int { deckColumns ?? 6 }
    var showsDeckLabels: Bool { deckLabels ?? true }

    init() {}

    private enum CodingKeys: String, CodingKey {
        case mouseButtonLayouts, selectedMouseButtonLayout, commonMouseButtonLayout, mouseButtonLayoutsByDevice, mouseDeviceOverrides, mouseDeviceNames, mouseLayerButtonsByDevice, typingPriority, reverseMouseScroll, trackpadEdgeWidth, menuBarTrackpadEnabled, disabledApplications, archiveSettings, deckDefaults, deckLayouts, deckAfterAction, deckOnLeave, clipboardHistoryEnabled, startInputOnLaunch, loginLaunchDesired, macros, gestures, resumeInputOnLaunch, mouseButtonNames, forwardMouseButton, backMouseButton, reversedWheelLayers, version, spaceEnabled, keyboardLayout, windowTarget, mappings, profiles, manualProfileID, automaticProfiles, deck, deckColumns, deckLabels
    }

    init(from decoder: Decoder) throws {
        let values = try decoder.container(keyedBy: CodingKeys.self)
        let originalVersion = try values.decode(Int.self, forKey: .version)
        guard originalVersion == 1 || originalVersion == 2 else {
            throw ConfigurationError.invalid("未対応の設定バージョンです。")
        }
        disabledApplications = try values.decodeIfPresent([DisabledApplication].self,forKey:.disabledApplications)
        archiveSettings = try values.decodeIfPresent(ArchiveSettings.self,forKey:.archiveSettings)
        deckDefaults = try values.decodeIfPresent([String:String].self,forKey:.deckDefaults)
        deckLayouts = try values.decodeIfPresent([DeckLayout].self,forKey:.deckLayouts)
        deckAfterAction = try values.decodeIfPresent(DeckDisplayBehavior.self,forKey:.deckAfterAction)
        deckOnLeave = try values.decodeIfPresent(DeckDisplayBehavior.self,forKey:.deckOnLeave)
        clipboardHistoryEnabled = try values.decodeIfPresent(Bool.self,forKey:.clipboardHistoryEnabled)
        macros = try values.decodeIfPresent([MacroDefinition].self,forKey:.macros) ?? []
        gestures = try values.decodeIfPresent([GestureDefinition].self,forKey:.gestures) ?? []
        startInputOnLaunch = try values.decodeIfPresent(Bool.self,forKey:.startInputOnLaunch)
        loginLaunchDesired = try values.decodeIfPresent(Bool.self,forKey:.loginLaunchDesired)
        resumeInputOnLaunch = try values.decodeIfPresent(Bool.self,forKey:.resumeInputOnLaunch)
        mouseButtonLayouts = try values.decodeIfPresent([MouseButtonLayout].self,forKey:.mouseButtonLayouts)
        selectedMouseButtonLayout = try values.decodeIfPresent(Int.self,forKey:.selectedMouseButtonLayout)
        commonMouseButtonLayout = try values.decodeIfPresent(MouseButtonLayout.self,forKey:.commonMouseButtonLayout)
        mouseButtonLayoutsByDevice = try values.decodeIfPresent([String:MouseButtonLayout].self,forKey:.mouseButtonLayoutsByDevice)
        mouseDeviceOverrides = try values.decodeIfPresent([MouseDeviceOverride].self,forKey:.mouseDeviceOverrides)
        mouseDeviceNames = try values.decodeIfPresent([String:String].self,forKey:.mouseDeviceNames)
        mouseLayerButtonsByDevice = try values.decodeIfPresent([String:MouseLayerButtons].self,forKey:.mouseLayerButtonsByDevice)
        typingPriority = try values.decodeIfPresent(Bool.self,forKey:.typingPriority)
        spaceEnabled = try values.decode(Bool.self, forKey: .spaceEnabled)
        keyboardLayout = try values.decodeIfPresent(KeyboardLayout.self, forKey: .keyboardLayout) ?? .us
        mouseButtonNames = try values.decodeIfPresent([String:String].self,forKey:.mouseButtonNames)
        forwardMouseButton = try values.decodeIfPresent(Int.self, forKey: .forwardMouseButton)
        backMouseButton = try values.decodeIfPresent(Int.self, forKey: .backMouseButton)
        trackpadEdgeWidth = try values.decodeIfPresent(Double.self, forKey: .trackpadEdgeWidth)
        menuBarTrackpadEnabled = try values.decodeIfPresent(Bool.self, forKey: .menuBarTrackpadEnabled)
        reverseMouseScroll = try values.decodeIfPresent(Bool.self,forKey:.reverseMouseScroll)
        reversedWheelLayers = try values.decodeIfPresent([Layer].self, forKey: .reversedWheelLayers)
        windowTarget = try values.decodeIfPresent(WindowTargetMode.self, forKey: .windowTarget)
        mappings = try values.decode([Mapping].self, forKey: .mappings)
        if originalVersion == 2 {
            profiles = try values.decode([Profile].self, forKey: .profiles)
            manualProfileID = try values.decode(String.self, forKey: .manualProfileID)
            automaticProfiles = try values.decode(Bool.self, forKey: .automaticProfiles)
            deck = try values.decode([DeckItem].self, forKey: .deck)
            deckColumns = try values.decodeIfPresent(Int.self, forKey: .deckColumns)
            deckLabels = try values.decodeIfPresent(Bool.self, forKey: .deckLabels)
        }
        try validate()
    }

    func profileName(_ id: String) -> String { profiles.first { $0.id == id }?.name ?? "標準" }
    func mappings(for id: String) -> [Mapping] { id == "default" ? mappings : profiles.first { $0.id == id }?.mappings ?? [] }
    func mappings(for id: String, mouseDevice: String?) -> [Mapping] {
        var result = mappings(for:id)
        guard let mouseDevice = mouseDevice else { return result }
        for override in mouseDeviceOverrides ?? [] where override.deviceID == mouseDevice && override.profileID == id {
            result.removeAll { $0.layer == override.layer && $0.input == override.input && $0.trigger == override.trigger }
            if let mapping = override.mapping { result.append(mapping) }
        }
        return result
    }
    mutating func setMouseDeviceMapping(_ mapping: Mapping?, deviceID: String, profileID: String, layer: Layer, input: UInt16, trigger: Trigger) {
        var values = mouseDeviceOverrides ?? []
        values.removeAll { $0.deviceID == deviceID && $0.profileID == profileID && $0.layer == layer && $0.input == input && $0.trigger == trigger }
        values.append(MouseDeviceOverride(deviceID:deviceID,profileID:profileID,layer:layer,input:input,trigger:trigger,mapping:mapping))
        mouseDeviceOverrides = values
    }
    func activeProfileID(bundleIdentifier: String?) -> String {
        guard automaticProfiles else { return manualProfileID }
        return profiles.first { $0.bundleIdentifiers.contains(bundleIdentifier ?? "") }?.id ?? "default"
    }
    func mapping(for item: DeckItem) -> Mapping? {
        if let action = item.directAction { return action }
        return mappings(for: item.profileID).first { $0.layer == item.layer && $0.input == item.input && $0.trigger == (item.trigger ?? .tap) }
    }
    mutating func setMappings(_ values: [Mapping], for id: String) {
        if id == "default" { mappings = values }
        else if let index = profiles.firstIndex(where: { $0.id == id }) { profiles[index].mappings = values }
    }
    mutating func replaceMapping(_ value: Mapping, at index: Int?, profileID: String) {
        var values = mappings(for: profileID)
        if let index = index, values.indices.contains(index) {
            let old = values[index]
            values[index] = value
            for i in deck.indices where deck[i].directAction == nil && deck[i].profileID == profileID && deck[i].layer == old.layer && deck[i].input == old.input && (deck[i].trigger ?? .tap) == old.trigger {
                deck[i].layer = value.layer; deck[i].input = value.input; deck[i].trigger = value.trigger == .tap && deck[i].trigger == nil ? nil : value.trigger
            }
        } else { values.append(value) }
        setMappings(values, for: profileID)
    }
    mutating func removeMapping(at index: Int, profileID: String) {
        var values = mappings(for: profileID)
        guard values.indices.contains(index) else { return }
        let removed = values.remove(at: index)
        deck.removeAll { $0.directAction == nil && $0.profileID == profileID && $0.layer == removed.layer && $0.input == removed.input && ($0.trigger ?? .tap) == removed.trigger }
        setMappings(values, for: profileID)
    }
    mutating func removeProfile(_ id: String) {
        profiles.removeAll { $0.id == id }
        mouseDeviceOverrides?.removeAll { $0.profileID == id }
        deck.removeAll { $0.directAction == nil && $0.profileID == id }
        if manualProfileID == id { manualProfileID = "default" }
    }

    func validate() throws {
        guard (0.06...0.22).contains(edgeWidth) else { throw ConfigurationError.invalid("トラックパッドの端の幅が不正です。") }
        func validMouseLayout(_ layout: MouseButtonLayout) -> Bool {
            !layout.name.trimmingCharacters(in:.whitespacesAndNewlines).isEmpty && layout.name.count <= 30 &&
            layout.items.count <= 36 && Set(layout.items.map(\.source)).count == layout.items.count &&
            layout.items.allSatisfy { $0.valid }
        }
        if let layouts = mouseButtonLayouts {
            guard !layouts.isEmpty, layouts.count <= 5, layouts.allSatisfy(validMouseLayout),
                  selectedMouseButtonLayout.map({ layouts.indices.contains($0) }) ?? true else {
                throw ConfigurationError.invalid("マウスレイアウトの位置・サイズ・名前が不正です。")
            }
        } else if selectedMouseButtonLayout != nil {
            throw ConfigurationError.invalid("選択したマウスレイアウトがありません。")
        }
        let deviceLayouts = mouseButtonLayoutsByDevice ?? [:]
        guard commonMouseButtonLayout.map(validMouseLayout) ?? true, deviceLayouts.count <= 32,
              deviceLayouts.allSatisfy({ !$0.key.isEmpty && $0.key.count <= 80 && validMouseLayout($0.value) }) else {
            throw ConfigurationError.invalid("マウス別のボタン配置が不正です。")
        }

        let excluded = disabledApplications ?? []
        guard excluded.count <= 200, Set(excluded.map { $0.bundleIdentifier }).count == excluded.count,
              excluded.allSatisfy({ !$0.bundleIdentifier.isEmpty && $0.bundleIdentifier.count <= 255 && !$0.name.isEmpty }) else { throw ConfigurationError.invalid("入力無効アプリの登録が不正です。") }
        if let archive = archiveSettings {
            guard [archive.watchFolder,archive.destinationFolder].allSatisfy({ $0.isEmpty || ($0.hasPrefix("/") && !$0.contains("\0")) }) else { throw ConfigurationError.invalid("自動解凍のフォルダーは絶対パスで指定してください。") }
        }

        guard macros.count <= 100, Set(macros.map { $0.id }).count == macros.count else { throw ConfigurationError.invalid("マクロIDの重複、または100件を超えています。") }
        for macro in macros {
            guard !macro.id.isEmpty, !macro.name.isEmpty, macro.name.count <= 80, macro.steps.count <= 10000, Set(macro.steps.map { $0.id }).count == macro.steps.count else { throw ConfigurationError.invalid("マクロ名・手順数・手順IDが不正です。") }
            for step in macro.steps {
                guard (0...600000).contains(step.delayMilliseconds), step.x.isFinite, step.y.isFinite, abs(step.x) <= 100000, abs(step.y) <= 100000 else { throw ConfigurationError.invalid("待ち時間は0〜600000ms、座標は±100000以内です。") }
                if let action = step.action {
                    guard action.action != .gesture, step.phase != .move, step.phase == .pulse || action.action.isKey || action.action == .mouse else { throw ConfigurationError.invalid("押す／離すはキー・マウスだけに指定できます。ジェスチャーはマクロに入れられません。") }
                    try Self.validateMappings([action])
                } else if step.phase == .down || step.phase == .up { throw ConfigurationError.invalid("押す／離すの入力が未指定です。") }
            }
            _ = try MacroPlan.expand(macro.id,definitions:macros)
        }

        guard gestures.count <= 100, Set(gestures.map { $0.id }).count == gestures.count else { throw ConfigurationError.invalid("ジェスチャーIDが重複、または100件を超えています。") }
        for gesture in gestures {
            guard !gesture.id.isEmpty, !gesture.name.isEmpty, gesture.name.count <= 80, (3...100).contains(gesture.threshold), Set(gesture.actions.keys).isSubset(of:["up","down","left","right","center"]) else { throw ConfigurationError.invalid("ジェスチャーの名前・方向・感度（3〜100）が不正です。") }
            for action in gesture.actions.values {
                guard action.action != .gesture else { throw ConfigurationError.invalid("ジェスチャーの入れ子は指定できません。") }
                try Self.validateMappings([action])
            }
        }
        for group in [mappings] + profiles.map({ $0.mappings }) {
            for mapping in group where mapping.action == .gesture {
                guard !group.contains(where: { $0.layer == mapping.layer && $0.input == mapping.input && $0.trigger == .hold }) else { throw ConfigurationError.invalid("ジェスチャーと同じ入力のHOLDは併用できません。HOLDを解除してください。") }
            }
            for direction in 0..<4 {
                let scroll = UInt16(270 + direction), edge = UInt16(422 + direction)
                if group.contains(where:{ $0.layer == .trackpad && $0.input == scroll }) && group.contains(where:{ $0.layer == .trackpad && $0.input == edge }) {
                    throw ConfigurationError.invalid("2本指スクロールと端からの操作は同じ方向に併用できません。どちらか一方を選んでください。")
                }
            }
        }
        for item in deck {
            guard mapping(for:item)?.action != .gesture else { throw ConfigurationError.invalid("ジェスチャーの開始元はキー／マウスボタンです。Deckには方向ごとのActionを割り当ててください。") }
        }
        let all = mappings + profiles.flatMap { $0.mappings } + deck.compactMap { $0.directAction }
        let macroReferences = all + gestures.flatMap { Array($0.actions.values) } + macros.flatMap { $0.steps.compactMap { $0.action } }
        for mapping in macroReferences where mapping.action == .profile {
            guard mapping.payload == "default" || profiles.contains(where: { $0.id == mapping.payload }) else { throw ConfigurationError.invalid("切替先のプロファイルがありません。") }
        }
        for mapping in macroReferences where mapping.action == .macro {
            guard macros.contains(where: { $0.id == mapping.payload }) else { throw ConfigurationError.invalid("参照先のマクロがありません。") }
        }
        for mapping in all where mapping.action == .gesture {
            guard gestures.contains(where: { $0.id == mapping.payload }), mapping.trigger == .tap, !(270...273).contains(mapping.input), mapping.layer != .trackpad else { throw ConfigurationError.invalid("ジェスチャーは存在する定義をTAPへ割り当ててください。ホイールには割り当てできません。") }
        }
        for (key,name) in mouseButtonNames ?? [:] {
            guard let code = UInt16(key), code >= 256, Keys.inputs.contains(where: { $0.code == code }), !name.isEmpty, name.count <= 40 else { throw ConfigurationError.invalid("マウスボタンの名前が不正です。") }
        }
        guard (3...31).contains(forwardButton), (3...31).contains(backButton), forwardButton != backButton else {
            throw ConfigurationError.invalid("進む／戻るには異なるサイドボタンを登録してください。")
        }
        guard version == 2 else { throw ConfigurationError.invalid("未対応の設定バージョンです。") }
        guard profiles.count <= 32, deck.count <= 5184 else { throw ConfigurationError.invalid("プロファイルは32個、Deckは324個までです。") }
        guard (1...18).contains(columns) else { throw ConfigurationError.invalid("Deckの列数は1〜18です。") }
        var ids: Set<String> = ["default"]
        var applications = Set<String>()
        for profile in profiles {
            guard !profile.id.isEmpty, ids.insert(profile.id).inserted,
                  !profile.name.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty, profile.name.count <= 80 else {
                throw ConfigurationError.invalid("プロファイルのIDまたは名前が不正です。")
            }
            for identifier in profile.bundleIdentifiers {
                guard identifier.contains("."), !identifier.contains(where: { $0.isWhitespace }),
                      identifier.count <= 255, applications.insert(identifier).inserted else {
                    throw ConfigurationError.invalid("対象アプリが不正か、複数のプロファイルへ重複登録されています。")
                }
            }
            try Self.validateMappings(profile.mappings)
        }
        guard ids.contains(manualProfileID) else { throw ConfigurationError.invalid("使用プロファイルが見つかりません。") }
        try Self.validateMappings(mappings)
        if let overrides = mouseDeviceOverrides {
            guard overrides.count <= 2048 else { throw ConfigurationError.invalid("マウス別の割り当てが多すぎます。") }
            var slots = Set<String>()
            for item in overrides {
                guard !item.deviceID.isEmpty, item.deviceID.count <= 80, ids.contains(item.profileID),
                      MouseDeviceOverride.supports(item.layer,item.input),
                      InputPolicy.allows(item.input,layer:item.layer,trigger:item.trigger),
                      slots.insert("\(item.deviceID):\(item.profileID):\(item.layer.rawValue):\(item.input):\(item.trigger.rawValue)").inserted else {
                    throw ConfigurationError.invalid("マウス別の割り当て先が不正です。")
                }
                if let mapping = item.mapping {
                    guard mapping.layer == item.layer, mapping.input == item.input, mapping.trigger == item.trigger else {
                        throw ConfigurationError.invalid("マウス別の割り当てが異なるキーを指しています。")
                    }
                    try Self.validateMappings([mapping])
                }
            }
        }
        let deviceLayerButtons = mouseLayerButtonsByDevice ?? [:]
        guard deviceLayerButtons.count <= 32, deviceLayerButtons.allSatisfy({
            !$0.key.isEmpty && $0.key.count <= 80 && $0.value.isValid
        }) else {
            throw ConfigurationError.invalid("マウス別のレイヤーボタンが不正です。")
        }
        guard (mouseDeviceNames ?? [:]).count <= 32,
              (mouseDeviceNames ?? [:]).allSatisfy({ !$0.key.isEmpty && $0.key.count <= 80 && !$0.value.isEmpty && $0.value.count <= 80 }) else {
            throw ConfigurationError.invalid("マウスの名前が不正です。")
        }
        let layouts = deckLayouts ?? []
        guard layouts.count <= 16, Set(layouts.map { $0.id }).count == layouts.count else { throw ConfigurationError.invalid("Deckは16枚までです。識別子を重複できません。") }
        var variants = Set<String>()
        for layout in layouts {
            if let group = layout.profileGroupID {
                guard !group.isEmpty, let profile = layout.linkedProfileID, ids.contains(profile), variants.insert(group+":"+profile).inserted else { throw ConfigurationError.invalid("Deckのプロファイル連動が不正です。") }
            } else if layout.linkedProfileID != nil { throw ConfigurationError.invalid("Deckのグループが見つかりません。") }
            guard !layout.id.isEmpty,!layout.name.trimmingCharacters(in:.whitespacesAndNewlines).isEmpty,layout.name.count <= 80,
                  (1...18).contains(layout.columns),(4...24).contains(layout.padding),(1...18).contains(layout.rows ?? 5),layout.expanded?.valid != false,layout.collapsed?.valid != false else { throw ConfigurationError.invalid("Deckの名前・配置・列数が不正です。") }
        }
        guard deckLayoutIDs.count <= 16, deckLayoutIDs.allSatisfy({ deckItems($0).count <= 324 }) else { throw ConfigurationError.invalid("Deckは16枚、各324ボタンまでです。") }
        for id in deckLayoutIDs {
            let slots = deckItems(id).compactMap { $0.slot }
            guard slots.allSatisfy({ (0..<324).contains($0) }), Set(slots).count == slots.count else { throw ConfigurationError.invalid("Deckのマス位置が重複または範囲外です。") }
        }
        var deckIDs = Set<String>()
        for item in deck {
            guard deckLayoutIDs.contains(item.layoutID ?? "default"), !item.id.isEmpty, deckIDs.insert(item.id).inserted, (item.directAction != nil || ids.contains(item.profileID)),
                  !item.title.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty, item.title.count <= 80,
                  mapping(for: item) != nil else { throw ConfigurationError.invalid("Deckの名前または割り当て参照が不正です。") }
            if let action = item.directAction { try Self.validateMappings([action]) }
            guard (item.iconPNG?.count ?? 0) <= 524288 else { throw ConfigurationError.invalid("Deckアイコンが大きすぎます。") }
            if let color = item.colorHex, !Self.validColor(color) { throw ConfigurationError.invalid("Deckの色が不正です。") }
        }
    }

    static func validColor(_ value: String) -> Bool {
        value.count == 6 && value.unicodeScalars.allSatisfy { (48...57).contains($0.value) || (65...70).contains($0.value) || (97...102).contains($0.value) }
    }
    enum ScopeOperation: Int, CaseIterable {
        case copyLayers, copyProfiles, removeLayers, removeProfiles
        var title: String {
            switch self {
            case .copyLayers: return "すべてのレイヤーへ割り当て"
            case .copyProfiles: return "すべてのプロファイルへ割り当て"
            case .removeLayers: return "すべてのレイヤーから削除"
            case .removeProfiles: return "すべてのプロファイルから削除"
            }
        }
    }
    func applyingScope(_ operation: ScopeOperation, profile: String, layer: Layer, inputs: Set<UInt16>) -> Configuration {
        var result = self
        let source = mappings(for: profile).filter { $0.layer == layer && inputs.contains($0.input) }
        let targetProfiles = operation == .copyProfiles || operation == .removeProfiles ? ["default"] + profiles.map { $0.id } : [profile]
        let targetLayers = operation == .copyLayers || operation == .removeLayers ? Layer.allCases : [layer]
        for profile in targetProfiles {
            for layer in targetLayers {
                if operation == .copyLayers || operation == .copyProfiles {
                    for var mapping in source where InputPolicy.allows(mapping.input, layer: layer, trigger: mapping.trigger) {
                        mapping.layer = layer
                        let index = result.mappings(for: profile).firstIndex { $0.layer == layer && $0.input == mapping.input && $0.trigger == mapping.trigger }
                        result.replaceMapping(mapping, at: index, profileID: profile)
                    }
                } else {
                    for index in result.mappings(for: profile).indices.reversed() {
                        let mapping = result.mappings(for: profile)[index]
                        if mapping.layer == layer && inputs.contains(mapping.input) { result.removeMapping(at: index, profileID: profile) }
                    }
                }
            }
        }
        return result
    }
    private static func validateMappings(_ mappings: [Mapping]) throws {
        guard mappings.count <= 512 else { throw ConfigurationError.invalid("1プロファイルの割り当ては512個までです。") }
        var seen = Set<String>()
        for mapping in mappings {
            guard InputPolicy.allows(mapping.input, layer: mapping.layer, trigger: mapping.trigger) else {
                throw ConfigurationError.invalid("入力キーは一覧から選んでください。Space・修飾キー・通常の左クリック・レイヤー自身は予約されています。")
            }
            guard (50...10000).contains(mapping.holdMilliseconds) else { throw ConfigurationError.invalid("長押し時間は50〜10000msです。") }
            guard seen.insert("\(mapping.layer.rawValue):\(mapping.input):\(mapping.trigger.rawValue)").inserted else {
                throw ConfigurationError.invalid("同じレイヤー・入力キーの割り当てが重複しています。")
            }
            guard mapping.modifiers.subtracting(.all).isEmpty else {
                throw ConfigurationError.invalid("未対応の修飾キーです。")
            }
            switch mapping.action {
            case .disabled: break
            case .profile: guard !mapping.payload.isEmpty else { throw ConfigurationError.invalid("切替先プロファイルを選択してください。") }
            case .macro: guard !mapping.payload.isEmpty else { throw ConfigurationError.invalid("マクロを選択してください。") }
            case .gesture: guard !mapping.payload.isEmpty else { throw ConfigurationError.invalid("ジェスチャーを選択してください。") }
            case .launch:
                guard mapping.application.hasPrefix("/"), mapping.application.hasSuffix(".app") else {
                    throw ConfigurationError.invalid("起動する.appを選択してください。")
                }
            case .text:
                guard !mapping.payload.isEmpty, mapping.payload.utf16.count <= 4096 else {
                    throw ConfigurationError.invalid("文字列は1〜4096文字（UTF-16）にしてください。")
                }
            case .url:
                guard mapping.payload.count <= 2048, let url = URLComponents(string: mapping.payload),
                      ["http", "https"].contains(url.scheme?.lowercased() ?? ""),
                      let host = url.host, !host.isEmpty, url.url != nil else {
                    throw ConfigurationError.invalid("http:// または https:// で始まるURLを指定してください。")
                }
            case .mouse:
                guard mapping.output <= 31 else { throw ConfigurationError.invalid("マウス出力のボタン番号は0〜31です。") }
            case .system:
                guard SystemCommand(rawValue: mapping.payload) != nil else { throw ConfigurationError.invalid("システムActionが不正です。") }
            case .window:
                guard WindowCommand(rawValue: mapping.payload) != nil else { throw ConfigurationError.invalid("未対応のウィンドウ操作です。") }
            case .file:
                guard mapping.application.hasPrefix("/"), mapping.application.count <= 4096, !mapping.application.contains("\0") else { throw ConfigurationError.invalid("ファイルの絶対パスが不正です。") }
            case .deck: break
            case .key, .shortcut:
                guard Keys.outputs.contains(where: { $0.code == mapping.output }) else {
                    throw ConfigurationError.invalid("出力キーは一覧から選んでください。")
                }
                guard mapping.action != .shortcut || !mapping.modifiers.isEmpty else {
                    throw ConfigurationError.invalid("ショートカットには修飾キーを1つ以上指定してください。")
                }
                guard mapping.action != .key || mapping.modifiers.isEmpty else {
                    throw ConfigurationError.invalid("修飾キーを指定する場合はショートカットを選んでください。")
                }
            }
        }
    }
}

enum TextOutput {
    /// Keep Quartz text events short without splitting UTF-16 surrogate pairs.
    static func chunks(_ text: String) -> [[UInt16]] {
        var result: [[UInt16]] = [], current: [UInt16] = []
        for scalar in text.unicodeScalars {
            let units = Array(String(scalar).utf16)
            if current.count + units.count > 20 { result.append(current); current = [] }
            current.append(contentsOf: units)
        }
        if !current.isEmpty { result.append(current) }
        return result
    }
}

struct KeyChoice {
    let code: UInt16
    let name: String
}

enum Keys {
    // macOS virtual key codes are physical positions. Labels follow the US layout.
    static let outputs: [KeyChoice] = [
        (0,"A"),(11,"B"),(8,"C"),(2,"D"),(14,"E"),(3,"F"),(5,"G"),(4,"H"),
        (34,"I"),(38,"J"),(40,"K"),(37,"L"),(46,"M"),(45,"N"),(31,"O"),(35,"P"),
        (12,"Q"),(15,"R"),(1,"S"),(17,"T"),(32,"U"),(9,"V"),(13,"W"),(7,"X"),(16,"Y"),(6,"Z"),
        (18,"1"),(19,"2"),(20,"3"),(21,"4"),(23,"5"),(22,"6"),(26,"7"),(28,"8"),(25,"9"),(29,"0"),
        (36,"Return"),(48,"Tab"),(49,"Space"),(51,"Delete"),(117,"Forward Delete"),(53,"Escape"),
        (123,"←"),(124,"→"),(125,"↓"),(126,"↑"),(115,"Home"),(119,"End"),(116,"Page Up"),(121,"Page Down"),
        (27,"-"),(24,"="),(33,"["),(30,"]"),(42,"\\"),(41,";"),(39,"'"),(43,","),(47,"."),(44,"/"),(50,"`"),
        (122,"F1"),(120,"F2"),(99,"F3"),(118,"F4"),(96,"F5"),(97,"F6"),
        (98,"F7"),(100,"F8"),(101,"F9"),(109,"F10"),(103,"F11"),(111,"F12"),
        (102,"英数 (JIS)"),(104,"かな (JIS)"),(93,"¥ (JIS)"),(94,"_ (JIS)"),
        (65,"テンキー ."),(67,"テンキー ×"),(69,"テンキー +"),(71,"Clear"),(75,"テンキー ÷"),(76,"テンキー Enter"),(78,"テンキー −"),(81,"テンキー ="),
        (82,"テンキー 0"),(83,"テンキー 1"),(84,"テンキー 2"),(85,"テンキー 3"),(86,"テンキー 4"),(87,"テンキー 5"),(88,"テンキー 6"),(89,"テンキー 7"),(91,"テンキー 8"),(92,"テンキー 9")
    ].map { KeyChoice(code: UInt16($0.0), name: $0.1) }
    static let mouseInputs: [KeyChoice] = [(256,"左クリック"),(257,"右クリック"),(258,"中クリック"),(259,"戻る"),(260,"進む"),(270,"ホイール上"),(271,"ホイール下"),(272,"ホイール左"),(273,"ホイール右")].map { KeyChoice(code: UInt16($0.0), name: $0.1) }
    static let extraMouseInputs = (3...31).map { KeyChoice(code: UInt16(300 + $0), name: "ボタン番号 \($0)") } + [KeyChoice(code: 340, name: "MX Master 4 モード切替")]
    static let touchInputs: [KeyChoice] = [(400,"3本指タップ"),(401,"3本指 上スワイプ"),(402,"3本指 下スワイプ"),(403,"3本指 左スワイプ"),(404,"3本指 右スワイプ"),(405,"4本指タップ"),(406,"4本指 上スワイプ"),(407,"4本指 下スワイプ"),(408,"4本指 左スワイプ"),(409,"4本指 右スワイプ"),(410,"ピンチイン"),(411,"ピンチアウト"),(412,"右回転"),(413,"左回転"),(414,"左上タップ"),(415,"左上ダブルタップ"),(416,"右上タップ"),(417,"右上ダブルタップ"),(418,"左下タップ"),(419,"左下ダブルタップ"),(420,"右下タップ"),(421,"右下ダブルタップ"),(422,"2本指 下端から上へ"),(423,"2本指 上端から下へ"),(424,"2本指 右端から左へ"),(425,"2本指 左端から右へ")].map { KeyChoice(code:UInt16($0.0),name:$0.1) }
    static let edgeInputs: [KeyChoice] = (1...2).flatMap { fingers in
        ["上端を左へ","上端を右へ","下端を左へ","下端を右へ","左端を上へ","左端を下へ","右端を上へ","右端を下へ"].enumerated().map {
            KeyChoice(code: UInt16(426 + (fingers - 1) * 8 + $0.offset), name: "\(fingers)本指 \($0.element)")
        }
    }
    static let inputs = outputs.filter { $0.code != 49 } + mouseInputs + extraMouseInputs + touchInputs + edgeInputs
    static func outputs(for layout: KeyboardLayout) -> [KeyChoice] {
        let absent: Set<UInt16> = layout == .jis ? [50] : [93,94,102,104]
        return outputs.filter { !absent.contains($0.code) }
    }
    static func name(_ code: UInt16, layout: KeyboardLayout = .us) -> String {
        if layout == .jis, let label = [24:"^",33:"@",30:"[",42:"]",39:":",93:"¥",94:"_",102:"英数",104:"かな"][Int(code)] { return label }
        return (outputs + mouseInputs + extraMouseInputs + touchInputs + edgeInputs).first { $0.code == code }?.name ?? "Key \(code)"
    }
}

final class ConfigurationStore {
    let url: URL
    init(url: URL) { self.url = url }
    func load() throws -> Configuration {
        guard FileManager.default.fileExists(atPath: url.path) else { return Configuration() }
        return try Self.read(url)
    }
    func save(_ config: Configuration) throws {
        try config.validate()
        try FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
        if FileManager.default.fileExists(atPath: url.path),
           let raw = try JSONSerialization.jsonObject(with: Data(contentsOf: url)) as? [String: Any],
           raw["version"] as? Int == 1 {
            // An upgrade must leave the original v1 file recoverable.
            let backup = url.deletingLastPathComponent().appendingPathComponent("configuration.v1.backup.json")
            if !FileManager.default.fileExists(atPath: backup.path) { try FileManager.default.copyItem(at: url, to: backup) }
        }
        try Self.encoded(config).write(to: url, options: .atomic)
    }
    static func read(_ url: URL) throws -> Configuration {
        let size = try url.resourceValues(forKeys: [.fileSizeKey]).fileSize ?? 0
        guard size <= 1_048_576 else { throw ConfigurationError.invalid("設定ファイルは1MB以下にしてください。") }
        let data = try Data(contentsOf: url)
        guard data.count <= 1_048_576 else { throw ConfigurationError.invalid("設定ファイルが大きすぎます。") }
        let config = try JSONDecoder().decode(Configuration.self, from: data)
        try config.validate()
        return config
    }
    static func encoded(_ config: Configuration) throws -> Data {
        try config.validate()
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
        let data = try encoder.encode(config)
        guard data.count <= 1_048_576 else { throw ConfigurationError.invalid("設定ファイルが1MBを超えています。") }
        return data
    }
    func importConfiguration(from source: URL) throws -> Configuration {
        try importConfiguration(Self.read(source))
    }
    func importConfiguration(_ config: Configuration) throws -> Configuration {
        let data = try Self.encoded(config)
        if FileManager.default.fileExists(atPath: url.path) {
            let backup = url.deletingLastPathComponent().appendingPathComponent("configuration.before-import-\(UUID().uuidString).json")
            try FileManager.default.copyItem(at: url, to: backup)
        }
        // Import can recover a corrupt file, so use the already validated bytes directly.
        try FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
        try data.write(to: url, options: .atomic)
        return config
    }
}
