import Foundation

enum Layer: String, Codable, CaseIterable { case normal, space }
enum ActionType: String, Codable, CaseIterable {
    case key, shortcut, launch, text, url, deck
    var isKey: Bool { self == .key || self == .shortcut }
    var title: String {
        switch self {
        case .key: return "キー"
        case .shortcut: return "ショートカット"
        case .launch: return "アプリ起動"
        case .text: return "文字列入力"
        case .url: return "URLを開く"
        case .deck: return "Deck表示／非表示"
        }
    }
}

struct Modifiers: OptionSet, Codable, Equatable {
    let rawValue: UInt
    static let command = Modifiers(rawValue: 1)
    static let control = Modifiers(rawValue: 2)
    static let option = Modifiers(rawValue: 4)
    static let shift = Modifiers(rawValue: 8)
    static let all: Modifiers = [.command, .control, .option, .shift]
    var label: String {
        [(Self.control, "⌃"), (.option, "⌥"), (.shift, "⇧"), (.command, "⌘")]
            .filter { contains($0.0) }.map { $0.1 }.joined()
    }
}

struct Mapping: Codable, Equatable {
    var layer: Layer
    var input: UInt16
    var action: ActionType
    var output: UInt16 = 0
    var modifiers: Modifiers = []
    var application: String = ""
    var payload: String = ""
    init(layer: Layer, input: UInt16, action: ActionType, output: UInt16 = 0,
         modifiers: Modifiers = [], application: String = "", payload: String = "") {
        self.layer = layer; self.input = input; self.action = action; self.output = output
        self.modifiers = modifiers; self.application = application; self.payload = payload
    }
    private enum CodingKeys: String, CodingKey { case layer, input, action, output, modifiers, application, payload }
    init(from decoder: Decoder) throws {
        let values = try decoder.container(keyedBy: CodingKeys.self)
        layer = try values.decode(Layer.self, forKey: .layer)
        input = try values.decode(UInt16.self, forKey: .input)
        action = try values.decode(ActionType.self, forKey: .action)
        output = try values.decode(UInt16.self, forKey: .output)
        modifiers = try values.decode(Modifiers.self, forKey: .modifiers)
        application = try values.decode(String.self, forKey: .application)
        payload = try values.decodeIfPresent(String.self, forKey: .payload) ?? ""
    }
    var targetDescription: String {
        switch action {
        case .key, .shortcut: return modifiers.label + Keys.name(output)
        case .launch: return URL(fileURLWithPath: application).deletingPathExtension().lastPathComponent
        case .text: return "文字列：" + payload.prefix(24).replacingOccurrences(of: "\n", with: " ↵ ")
        case .url: return URL(string: payload)?.host ?? "URL"
        case .deck: return "Deck表示／非表示"
        }
    }
    var description: String {
        let source = (layer == .space ? "Space + " : "") + Keys.name(input)
        return "\(source)  →  \(targetDescription)"
    }
}

struct Profile: Codable, Equatable {
    var id = UUID().uuidString
    var name: String
    var bundleIdentifiers: [String] = []
    var mappings: [Mapping] = []
}

struct DeckItem: Codable, Equatable {
    var id = UUID().uuidString
    var title: String
    var profileID: String
    var layer: Layer
    var input: UInt16
}

enum ConfigurationError: LocalizedError {
    case invalid(String)
    var errorDescription: String? {
        switch self { case .invalid(let message): return message }
    }
}

struct Configuration: Codable, Equatable {
    var version = 2
    var spaceEnabled = true
    // The original mappings remain the standard profile, preserving v1 assignments exactly.
    var mappings: [Mapping] = []
    var profiles: [Profile] = []
    var manualProfileID = "default"
    var automaticProfiles = false
    var deck: [DeckItem] = []

    init() {}

    private enum CodingKeys: String, CodingKey {
        case version, spaceEnabled, mappings, profiles, manualProfileID, automaticProfiles, deck
    }

    init(from decoder: Decoder) throws {
        let values = try decoder.container(keyedBy: CodingKeys.self)
        let originalVersion = try values.decode(Int.self, forKey: .version)
        guard originalVersion == 1 || originalVersion == 2 else {
            throw ConfigurationError.invalid("未対応の設定バージョンです。")
        }
        spaceEnabled = try values.decode(Bool.self, forKey: .spaceEnabled)
        mappings = try values.decode([Mapping].self, forKey: .mappings)
        if originalVersion == 2 {
            profiles = try values.decode([Profile].self, forKey: .profiles)
            manualProfileID = try values.decode(String.self, forKey: .manualProfileID)
            automaticProfiles = try values.decode(Bool.self, forKey: .automaticProfiles)
            deck = try values.decode([DeckItem].self, forKey: .deck)
        }
        try validate()
    }

    func profileName(_ id: String) -> String { profiles.first { $0.id == id }?.name ?? "標準" }
    func mappings(for id: String) -> [Mapping] { id == "default" ? mappings : profiles.first { $0.id == id }?.mappings ?? [] }
    func activeProfileID(bundleIdentifier: String?) -> String {
        guard automaticProfiles else { return manualProfileID }
        return profiles.first { $0.bundleIdentifiers.contains(bundleIdentifier ?? "") }?.id ?? "default"
    }
    func mapping(for item: DeckItem) -> Mapping? {
        mappings(for: item.profileID).first { $0.layer == item.layer && $0.input == item.input }
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
            for i in deck.indices where deck[i].profileID == profileID && deck[i].layer == old.layer && deck[i].input == old.input {
                deck[i].layer = value.layer; deck[i].input = value.input
            }
        } else { values.append(value) }
        setMappings(values, for: profileID)
    }
    mutating func removeMapping(at index: Int, profileID: String) {
        var values = mappings(for: profileID)
        guard values.indices.contains(index) else { return }
        let removed = values.remove(at: index)
        deck.removeAll { $0.profileID == profileID && $0.layer == removed.layer && $0.input == removed.input }
        setMappings(values, for: profileID)
    }
    mutating func removeProfile(_ id: String) {
        profiles.removeAll { $0.id == id }
        deck.removeAll { $0.profileID == id }
        if manualProfileID == id { manualProfileID = "default" }
    }

    func validate() throws {
        guard version == 2 else { throw ConfigurationError.invalid("未対応の設定バージョンです。") }
        guard profiles.count <= 32, deck.count <= 48 else { throw ConfigurationError.invalid("プロファイルは32個、Deckは48個までです。") }
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
        var deckIDs = Set<String>()
        for item in deck {
            guard !item.id.isEmpty, deckIDs.insert(item.id).inserted, ids.contains(item.profileID),
                  !item.title.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty, item.title.count <= 80,
                  mapping(for: item) != nil else { throw ConfigurationError.invalid("Deckの名前または割り当て参照が不正です。") }
        }
    }

    private static func validateMappings(_ mappings: [Mapping]) throws {
        guard mappings.count <= 512 else { throw ConfigurationError.invalid("1プロファイルの割り当ては512個までです。") }
        var seen = Set<String>()
        for mapping in mappings {
            guard Keys.inputs.contains(where: { $0.code == mapping.input }) else {
                throw ConfigurationError.invalid("入力キーは一覧から選んでください。Spaceと修飾キーは予約されています。")
            }
            guard seen.insert("\(mapping.layer.rawValue):\(mapping.input)").inserted else {
                throw ConfigurationError.invalid("同じレイヤー・入力キーの割り当てが重複しています。")
            }
            guard mapping.modifiers.subtracting(.all).isEmpty else {
                throw ConfigurationError.invalid("未対応の修飾キーです。")
            }
            switch mapping.action {
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
        (102,"英数 (JIS)"),(104,"かな (JIS)")
    ].map { KeyChoice(code: UInt16($0.0), name: $0.1) }
    static let inputs = outputs.filter { $0.code != 49 }
    static func name(_ code: UInt16) -> String { outputs.first { $0.code == code }?.name ?? "Key \(code)" }
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
