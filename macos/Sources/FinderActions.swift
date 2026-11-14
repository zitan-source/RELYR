import AppKit

/// Finder window creation must precede activation, otherwise macOS may first
/// switch to the Space containing an existing Finder window.
enum FinderActions {
    static func script(newWindow: Bool) -> String {
        if newWindow {
            return """
            tell application "Finder"
                set newWindow to make new Finder window
                set target of newWindow to home
                set index of newWindow to 1
                activate
            end tell
            """
        }
        return """
        tell application "Finder"
            if (count of Finder windows) is 0 then
                make new Finder window
            end if
            set collapsed of front Finder window to false
            set index of front Finder window to 1
            activate
        end tell
        """
    }
    // Use Finder's normal document-open route. Enumerating every Finder window
    // through Apple events made clicks wait behind unrelated busy windows.
    static func openFolder(_ path: String, completion: @escaping (String?) -> Void) {
        let folder = URL(fileURLWithPath:path,isDirectory:true)
        completion(NSWorkspace.shared.open(folder) ? nil : "フォルダーを開けませんでした。場所とアクセス権を確認してください。")
    }
    private static let queue = DispatchQueue(label:"relyr.finder-actions")
    static func perform(newWindow: Bool, completion: @escaping (String?) -> Void) {
        execute(script(newWindow:newWindow),completion:completion)
    }
    private static func execute(_ source: String, completion: @escaping (String?) -> Void) {
        queue.async {
            var error: NSDictionary?
            guard let command = NSAppleScript(source:source) else {
                DispatchQueue.main.async { completion("Finder操作を作成できませんでした。") }; return
            }
            command.executeAndReturnError(&error)
            let message: String?
            if let error = error {
                message = (error[NSAppleScript.errorNumber] as? Int) == -1743
                    ? "Finderの操作許可が必要です。システム設定のプライバシーとセキュリティ → オートメーションでRELYRのFinder操作を許可してください。"
                    : "Finderを開けませんでした：\(error[NSAppleScript.errorMessage] ?? error)"
            } else { message = nil }
            DispatchQueue.main.async { completion(message) }
        }
    }
}
