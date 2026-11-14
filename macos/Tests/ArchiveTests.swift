import AppKit

@main struct ArchiveTests {
    static func main() throws {
        let root = URL(fileURLWithPath:CommandLine.arguments[1]), fm = FileManager.default
        let output = root.appendingPathComponent("output")
        try? fm.removeItem(at:output); try fm.createDirectory(at:output,withIntermediateDirectories:true)
        let good = root.appendingPathComponent("good.zip")
        let first = try ArchiveExtractor.extract(good,into:output)
        let text = try String(contentsOf:first.appendingPathComponent("folder/hello.txt"),encoding:.utf8); precondition(text == "RELYR archive test")
        let second = try ArchiveExtractor.extract(good,into:output)
        precondition(first != second && fm.fileExists(atPath:good.path))
        for name in ["escape.zip","link.zip","broken.zip","broken.gz"] {
            do { _ = try ArchiveExtractor.extract(root.appendingPathComponent(name),into:output); fatalError("Accepted invalid archive: \(name)") }
            catch { }
        }
        do { _ = try ArchiveExtractor.extract(good,into:output,cancelled:{ true }); fatalError("Ignored cancellation") }
        catch is CancellationError { }
        precondition(!fm.fileExists(atPath:root.appendingPathComponent("escaped.txt").path))
        let remaining = try fm.contentsOfDirectory(atPath:output.path); precondition(remaining.allSatisfy { !$0.hasPrefix(".relyr-extract-") })
        let gzip = try ArchiveExtractor.extract(root.appendingPathComponent("single.gz"),into:output)
        let gzipFiles = try fm.contentsOfDirectory(at:gzip,includingPropertiesForKeys:nil)
        precondition(gzipFiles.count == 1)
        let gzipContents = try Data(contentsOf:gzipFiles[0]); precondition(gzipContents == Data("RELYR gzip test".utf8))
        let watch = root.appendingPathComponent("watch")
        try? fm.removeItem(at:watch); try fm.createDirectory(at:watch,withIntermediateDirectories:true)
        try fm.copyItem(at:good,to:watch.appendingPathComponent("old.zip"))
        let service = ArchiveAutomation()
        service.apply(ArchiveSettings(enabled:true,watchFolder:watch.path,destinationFolder:output.path,trashAfterExtraction:false))
        RunLoop.current.run(until:Date().addingTimeInterval(1))
        try fm.copyItem(at:good,to:watch.appendingPathComponent("new.zip"))
        RunLoop.current.run(until:Date().addingTimeInterval(5))
        precondition(fm.fileExists(atPath:output.appendingPathComponent("new/folder/hello.txt").path))
        precondition(!fm.fileExists(atPath:output.appendingPathComponent("old").path))
        service.apply(ArchiveSettings())
        try fm.copyItem(at:good,to:watch.appendingPathComponent("disabled.zip"))
        RunLoop.current.run(until:Date().addingTimeInterval(2))
        precondition(!fm.fileExists(atPath:output.appendingPathComponent("disabled").path))
        print("PASS: archive extraction, collision handling, traversal/link rejection, corruption, cancellation, staging cleanup, watcher baseline/new files/OFF; isolated fixtures only")
    }
}
