import Foundation

/// Exact app-owned locations only. Never follow user-selected folders or scan by name.
enum UninstallPlan {
    static let bundleID = "io.github.zitan-source.relyr.mac.trial"
    static func supportURL(library: URL) -> URL {
        library.appendingPathComponent("Application Support/\(bundleID)")
    }
    /// Move the complete old directory once, including backups and the locked inode.
    /// A running older version prevents migration rather than writing to two locations.
    static func acquireDataLock(library: URL) throws -> InstanceLock {
        let fm = FileManager.default
        let destination = supportURL(library:library)
        let legacy = library.appendingPathComponent("Application Support/RELYR-Mac-Trial")
        if !fm.fileExists(atPath:destination.path), fm.fileExists(atPath:legacy.path) {
            guard !(try legacy.resourceValues(forKeys:[.isSymbolicLinkKey]).isSymbolicLink ?? false) else {
                throw NSError(domain:"RELYR.Data",code:1,userInfo:[NSLocalizedDescriptionKey:"旧設定フォルダがリンクのため、自動移行を中止しました。"])
            }
            let lock = try InstanceLock(url:legacy.appendingPathComponent("instance.lock"))
            try fm.moveItem(at:legacy,to:destination)
            return lock
        }
        return try InstanceLock(url:destination.appendingPathComponent("instance.lock"))
    }
    static func dataURLs(library: URL) -> [URL] {
        ["Application Support/\(bundleID)",
         "Application Support/RELYR-Mac-Trial",
         "Caches/\(bundleID)",
         "Preferences/\(bundleID).plist",
         "Saved Application State/\(bundleID).savedState",
         "HTTPStorages/\(bundleID)",
         "HTTPStorages/\(bundleID).binarycookies",
         "WebKit/\(bundleID)"].map { library.appendingPathComponent($0) }
    }
    static func moveExisting(_ urls: [URL], exists: (URL) -> Bool,
                             move: (URL) throws -> Void) throws {
        for url in urls where exists(url) { try move(url) }
    }
}
