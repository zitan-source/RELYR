import Foundation

enum Layer: String, Codable, CaseIterable { case normal, space }
enum ActionType: String, Codable, CaseIterable { case key, shortcut, launch }

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
    var description: String {
        let source = (layer == .space ? "Space + " : "") + Keys.name(input)
        let target = action == .launch ? URL(fileURLWithPath: application).deletingPathExtension().lastPathComponent
            : modifiers.label + Keys.name(output)
        return "\(source)  →  \(target)"
    }
}

enum ConfigurationError: LocalizedError {
    case invalid(String)
    var errorDescription: String? {
        switch self { case .invalid(let message): return message }
    }
}

struct Configuration: Codable, Equatable {
    var version = 1
    var spaceEnabled = true
    var mappings: [Mapping] = []

    func validate() throws {
        guard version == 1 else { throw ConfigurationError.invalid("未対応の設定バージョンです。") }
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
            if mapping.action == .launch {
                guard mapping.application.hasPrefix("/"), mapping.application.hasSuffix(".app") else {
                    throw ConfigurationError.invalid("起動する.appを選択してください。")
                }
            } else {
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
        let config = try JSONDecoder().decode(Configuration.self, from: Data(contentsOf: url))
        try config.validate()
        return config
    }
    func save(_ config: Configuration) throws {
        try config.validate()
        try FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
        try encoder.encode(config).write(to: url, options: .atomic)
    }
}
