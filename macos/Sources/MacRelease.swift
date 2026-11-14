import Foundation

struct MacRelease: Decodable {
    struct Asset: Decodable { let name: String; let browser_download_url: String }
    let tag_name: String
    let draft: Bool
    let prerelease: Bool
    let assets: [Asset]
    static func version(_ text: String) -> [Int]? {
        let parts = text.split(separator: ".", omittingEmptySubsequences: false)
        guard parts.count == 3 else { return nil }
        let values = parts.compactMap { part -> Int? in
            guard !part.isEmpty, part.allSatisfy({ $0.isASCII && $0.isNumber }) else { return nil }
            return Int(part)
        }
        return values.count == 3 ? values : nil
    }
    var number: [Int]? { tag_name.hasPrefix("macos-v") ? Self.version(String(tag_name.dropFirst(7))) : nil }
    var download: URL? {
        guard number != nil else { return nil }
        let name = "RELYR-Mac-\(tag_name.dropFirst(7))-arm64.dmg"
        guard let asset = assets.first(where: { $0.name == name }),
              let url = URL(string: asset.browser_download_url), url.scheme == "https",
              url.host == "github.com", url.user == nil, url.password == nil,
              url.query == nil, url.fragment == nil,
              url.path == "/zitan-source/RELYR/releases/download/\(tag_name)/\(name)" else { return nil }
        return url
    }
    static func latest(_ releases: [MacRelease], current: String) -> MacRelease? {
        guard let installed = version(current) else { return nil }
        return releases.filter { !$0.draft && !$0.prerelease && $0.download != nil && installed.lexicographicallyPrecedes($0.number!) }
            .max { $0.number!.lexicographicallyPrecedes($1.number!) }
    }
}
