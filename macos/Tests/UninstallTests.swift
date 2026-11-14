import Foundation
@main struct UninstallTests {
    static func main() throws {
        let root = URL(fileURLWithPath:CommandLine.arguments[1]).appendingPathComponent("uninstall-fixture/Library")
        let targets = UninstallPlan.dataURLs(library:root)
        precondition(targets.count == Set(targets).count)
        precondition(targets.allSatisfy { $0.path.hasPrefix(root.path + "/") })
        precondition(!targets.contains(root))
        var moved: [URL] = []
        try UninstallPlan.moveExisting(targets,exists:{ $0 == targets[0] },move:{ moved.append($0) })
        precondition(moved == [targets[0]])
        moved = []
        do {
            try UninstallPlan.moveExisting(targets,exists:{ _ in true },move:{ url in
                if url == targets[1] { throw NSError(domain:"fixture",code:1) }
                moved.append(url)
            })
            fatalError("Removal failure must propagate")
        } catch { precondition(moved == [targets[0]]) }
        // Exercise real filesystem moves only inside a fresh isolated fixture.
        let fm = FileManager.default
        let fixture = root.deletingLastPathComponent().appendingPathComponent(UUID().uuidString)
        let library = fixture.appendingPathComponent("Library")
        let trash = fixture.appendingPathComponent("SimulatedTrash")
        try fm.createDirectory(at:trash,withIntermediateDirectories:true)
        let owned = UninstallPlan.dataURLs(library:library)
        let unrelated = library.appendingPathComponent("Application Support/OtherApp/keep.txt")
        try fm.createDirectory(at:unrelated.deletingLastPathComponent(),withIntermediateDirectories:true)
        try Data("keep".utf8).write(to:unrelated)
        for url in owned {
            try fm.createDirectory(at:url.deletingLastPathComponent(),withIntermediateDirectories:true)
            if url.pathExtension == "plist" || url.pathExtension == "binarycookies" { try Data("fixture".utf8).write(to:url) }
            else { try fm.createDirectory(at:url,withIntermediateDirectories:true) }
        }
        let linked = owned[0].appendingPathComponent("external-link")
        try fm.createSymbolicLink(at:linked,withDestinationURL:unrelated.deletingLastPathComponent())
        var count = 0
        try UninstallPlan.moveExisting(owned,exists:{ fm.fileExists(atPath:$0.path) },move:{ url in
            try fm.moveItem(at:url,to:trash.appendingPathComponent(String(count))); count += 1
        })
        precondition(count == owned.count && owned.allSatisfy { !fm.fileExists(atPath:$0.path) })
        let kept = try String(contentsOf:unrelated,encoding:.utf8)
        precondition(kept == "keep")
        try UninstallPlan.moveExisting(owned,exists:{ fm.fileExists(atPath:$0.path) },move:{ _ in fatalError("Repeated cleanup must skip absent files") })
        let migrationLibrary = fixture.appendingPathComponent("MigrationLibrary")
        let old = migrationLibrary.appendingPathComponent("Application Support/RELYR-Mac-Trial")
        try fm.createDirectory(at:old,withIntermediateDirectories:true)
        let payload = Data("private settings".utf8)
        try payload.write(to:old.appendingPathComponent("configuration.json"))
        let runningOld = try InstanceLock(url:old.appendingPathComponent("instance.lock"))
        do { _ = try UninstallPlan.acquireDataLock(library:migrationLibrary); fatalError("Running old app must block migration") }
        catch InstanceLockError.busy {}
        runningOld.release()
        let migrated = try UninstallPlan.acquireDataLock(library:migrationLibrary)
        let current = UninstallPlan.supportURL(library:migrationLibrary)
        precondition(!fm.fileExists(atPath:old.path))
        let contents = try Data(contentsOf:current.appendingPathComponent("configuration.json"))
        precondition(contents == payload)
        do { _ = try UninstallPlan.acquireDataLock(library:migrationLibrary); fatalError("Migration must retain process lock") }
        catch InstanceLockError.busy {}
        migrated.release()
        var removalIndex = 100
        try UninstallPlan.moveExisting(UninstallPlan.dataURLs(library:migrationLibrary),exists:{ fm.fileExists(atPath:$0.path) },move:{ url in
            try fm.moveItem(at:url,to:trash.appendingPathComponent(String(removalIndex))); removalIndex += 1
        })
        let fresh = try UninstallPlan.acquireDataLock(library:migrationLibrary)
        precondition(!fm.fileExists(atPath:current.appendingPathComponent("configuration.json").path))
        fresh.release()
        print("PASS: legacy migration preserves settings and lock; running old app blocks migration; uninstall/reinstall cannot restore removed settings")
        print("PASS: exact uninstall allowlist, real isolated file moves, unrelated/symlink targets preserved, repeat cleanup and failure propagation; no user files or login services touched")
    }
}
