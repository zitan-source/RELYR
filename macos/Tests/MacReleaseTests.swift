import Foundation
@main struct Tests {
    static func main() {
        func release(_ tag:String, draft:Bool = false, preview:Bool = false, wrong:Bool = false) -> MacRelease {
            let name = "RELYR-Mac-\(tag.replacingOccurrences(of:"macos-v",with:""))-arm64.dmg"
            return MacRelease(tag_name:tag,draft:draft,prerelease:preview,assets:[.init(name:name,browser_download_url:"https://\(wrong ? "example.com" : "github.com")/zitan-source/RELYR/releases/download/\(tag)/\(name)")])
        }
        let valid = release("macos-v0.3.0")
        let candidates = [release("v99.0.0"), release("macos-v9.0.0",draft:true),release("macos-v8.0.0",preview:true),release("macos-v7.0.0",wrong:true),valid,release("macos-v0.1.0")]
        precondition(MacRelease.latest(candidates,current:"0.2.0")?.tag_name == valid.tag_name)
        precondition(MacRelease.latest(candidates,current:"0.3.0") == nil)
        precondition(MacRelease.latest(candidates,current:"broken") == nil)
        for value in ["1.2","1.2.3.4","1.-2.3","1.2.3-beta", "１.2.3"] { precondition(MacRelease.version(value) == nil) }
        precondition(MacRelease.latest([release("macos-v0.9.0"),release("macos-v0.10.0")],current:"0.2.0")?.tag_name == "macos-v0.10.0")
        print("PASS: Mac release selection, Windows isolation, version ordering, drafts, prereleases, URL validation")
    }
}
