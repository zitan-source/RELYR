import AppKit

/// Mac-only release channel. Never uses /releases/latest (reserved for Windows).
final class MacUpdateController {
    static let shared = MacUpdateController()
    private var checking = false
    func check() {
        guard !checking else { return }
        checking = true
        Task { @MainActor in
            defer { checking = false }
            do {
                var releases: [MacRelease] = []
                var page = 1
                while true {
                    let url = URL(string:"https://api.github.com/repos/zitan-source/RELYR/releases?per_page=100&page=\(page)")!
                    var request = URLRequest(url:url, cachePolicy:.reloadIgnoringLocalCacheData, timeoutInterval:20)
                    request.setValue("application/vnd.github+json",forHTTPHeaderField:"Accept")
                    request.setValue("RELYR-Mac",forHTTPHeaderField:"User-Agent")
                    let (data,response) = try await URLSession.shared.data(for:request)
                    guard let http = response as? HTTPURLResponse, http.statusCode == 200 else { throw URLError(.badServerResponse) }
                    let batch = try JSONDecoder().decode([MacRelease].self,from:data)
                    releases += batch
                    if batch.count < 100 { break }
                    page += 1
                    guard page <= 20 else { throw URLError(.dataLengthExceedsMaximum) }
                }
                let current = Bundle.main.object(forInfoDictionaryKey:"CFBundleShortVersionString") as? String ?? ""
                let alert = NSAlert()
                if let release = MacRelease.latest(releases,current:current), let url = release.download {
                    alert.messageText = "Mac版 \(release.tag_name.dropFirst(7)) が利用できます"
                    alert.informativeText = "現在のバージョン：\(current)\nDMGをダウンロードし、RELYRを終了してからアプリケーションフォルダへ置き換えてください。設定は引き継がれます。"
                    alert.addButton(withTitle:"ダウンロード")
                    alert.addButton(withTitle:"あとで")
                    NSApp.activate(ignoringOtherApps:true)
                    if alert.runModal() == .alertFirstButtonReturn { NSWorkspace.shared.open(url) }
                } else {
                    alert.messageText = "新しいMac版はありません"
                    alert.informativeText = "現在のバージョン：\(current)\n公開済みのMac正式リリースを確認しました。"
                    alert.addButton(withTitle:"OK")
                    NSApp.activate(ignoringOtherApps:true)
                    alert.runModal()
                }
            } catch {
                let alert = NSAlert()
                alert.messageText = "更新を確認できませんでした"
                alert.informativeText = "通信状況を確認して、しばらくしてから再度お試しください。\n\(error.localizedDescription)"
                alert.runModal()
            }
        }
    }
}
