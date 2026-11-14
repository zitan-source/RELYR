import AppKit
import CArchive

struct ArchiveFailure: LocalizedError {
    let message: String
    var errorDescription: String? { message }
}

/// Writes into a private staging directory, never into paths supplied by the archive.
/// The destination becomes visible only after every entry succeeds.
enum ArchiveExtractor {
    static let suffixes = [".tar.gz", ".tar.bz2", ".tar.xz", ".tar.zst", ".tgz", ".tbz2", ".txz", ".zip", ".7z", ".rar", ".tar", ".gz", ".bz2", ".xz", ".zst"]
    static func supported(_ url: URL) -> Bool { suffixes.contains { url.lastPathComponent.lowercased().hasSuffix($0) } }
    static func extract(_ source: URL, into parent: URL, cancelled: () -> Bool = { false }) throws -> URL {
        guard supported(source), let reader = archive_read_new() else { throw ArchiveFailure(message:"対応していない圧縮ファイルです。") }
        defer { archive_read_free(reader) }
        archive_read_support_filter_all(reader); archive_read_support_format_all(reader)
        if ["gz","bz2","xz","zst"].contains(source.pathExtension.lowercased()) { archive_read_support_format_raw(reader) }
        func failure() -> ArchiveFailure { ArchiveFailure(message:archive_error_string(reader).map { String(cString:$0) } ?? "圧縮ファイルを読み込めません。") }
        guard archive_read_open_filename(reader,source.path,65536) == 0 else { throw failure() }
        if ["gz","bz2","xz","zst"].contains(source.pathExtension.lowercased()), archive_filter_code(reader,0) == 0 {
            throw ArchiveFailure(message:"拡張子と圧縮形式が一致しません。")
        }
        let fm = FileManager.default
        let suffix = suffixes.first { source.lastPathComponent.lowercased().hasSuffix($0) }!
        let name = String(source.lastPathComponent.dropLast(suffix.count))
        let base = name.isEmpty ? "展開したファイル" : name
        let staging = parent.appendingPathComponent(".relyr-extract-" + UUID().uuidString,isDirectory:true)
        try fm.createDirectory(at:staging,withIntermediateDirectories:false,attributes:[.posixPermissions:0o700])
        defer { try? fm.removeItem(at:staging) }
        var count = 0, expanded: Int64 = 0
        var buffer = [UInt8](repeating:0,count:65536)
        while true {
            if cancelled() { throw CancellationError() }
            var entry: OpaquePointer?
            let result = archive_read_next_header(reader,&entry)
            if result == 1 { break }
            guard result == 0, let entry = entry, let rawName = archive_entry_pathname(entry) else { throw failure() }
            count += 1
            guard count <= 10000 else { throw ArchiveFailure(message:"展開する項目が10,000件を超えています。") }
            let relative = String(cString:rawName)
            let parts = relative.split(separator:"/",omittingEmptySubsequences:true)
            guard !relative.hasPrefix("/"), !relative.contains("\\"), !parts.contains(".."),
                  archive_entry_symlink(entry) == nil, archive_entry_hardlink(entry) == nil else { throw ArchiveFailure(message:"保存先の外へのパス、またはリンクを含むため解凍を中止しました。") }
            let clean = parts.filter { $0 != "." }.map(String.init).joined(separator:"/")
            let kind = archive_entry_filetype(entry)
            guard kind == 0o040000 || kind == 0o100000 else { throw ArchiveFailure(message:"通常のファイル以外を含む圧縮ファイルです。") }
            if clean.isEmpty { if kind == 0o040000 { continue }; throw ArchiveFailure(message:"ファイル名が空です。") }
            let output = staging.appendingPathComponent(clean)
            if kind == 0o040000 { try fm.createDirectory(at:output,withIntermediateDirectories:true); continue }
            guard archive_entry_size(entry) <= 20 * 1024 * 1024 * 1024 else { throw ArchiveFailure(message:"展開サイズが20GBを超えています。") }
            try fm.createDirectory(at:output.deletingLastPathComponent(),withIntermediateDirectories:true)
            guard !fm.fileExists(atPath:output.path), fm.createFile(atPath:output.path,contents:nil,attributes:[.posixPermissions:0o600]) else { throw ArchiveFailure(message:"同名の項目が重複しています。") }
            let file = try FileHandle(forWritingTo:output)
            do {
                while true {
                    if cancelled() { throw CancellationError() }
                    let bytes = archive_read_data(reader,&buffer,buffer.count)
                    if bytes == 0 { break }
                    guard bytes > 0 else { throw failure() }
                    expanded += Int64(bytes)
                    guard expanded <= 20 * 1024 * 1024 * 1024 else { throw ArchiveFailure(message:"展開サイズが20GBを超えています。") }
                    try file.write(contentsOf:Data(buffer.prefix(bytes)))
                }
                try file.close()
            } catch { try? file.close(); throw error }
        }
        guard count > 0 else { throw ArchiveFailure(message:"圧縮ファイルが空です。") }
        if cancelled() { throw CancellationError() }
        var destination = parent.appendingPathComponent(base), number = 2
        while fm.fileExists(atPath:destination.path) { destination = parent.appendingPathComponent("\(base) (\(number))"); number += 1 }
        try fm.moveItem(at:staging,to:destination)
        return destination
    }
}

final class ArchiveAutomation {
    var onStatus: ((String) -> Void)?
    private let queue = DispatchQueue(label:"relyr.archive",qos:.utility)
    private var timer: DispatchSourceTimer?
    private var settings: ArchiveSettings?
    private let lock = NSLock()
    private var generation = 0
    private func ticket() -> Int { lock.lock(); defer { lock.unlock() }; return generation }
    private struct Stamp: Equatable { let size: Int; let modified: Date }
    private var seen: [URL:Stamp] = [:]
    private var stable: [URL:Int] = [:]
    private var completed: [URL:Stamp] = [:]
    private func stamp(_ url: URL) -> Stamp? {
        guard let value = try? url.resourceValues(forKeys:[.fileSizeKey,.contentModificationDateKey,.isRegularFileKey,.isSymbolicLinkKey]), value.isRegularFile == true, value.isSymbolicLink != true, let size = value.fileSize, let date = value.contentModificationDate else { return nil }
        return Stamp(size:size,modified:date)
    }
    func apply(_ next: ArchiveSettings) {
        guard settings != next else { return }; settings = next
        lock.lock(); generation += 1; let token = generation; lock.unlock()
        queue.async { [weak self] in
            guard let self = self else { return }
            self.timer?.cancel(); self.timer = nil; self.seen.removeAll(); self.stable.removeAll(); self.completed.removeAll()
            guard next.enabled, self.ticket() == token else { return }
            let watch = next.watchFolder.isEmpty ? FileManager.default.urls(for:.desktopDirectory,in:.userDomainMask)[0] : URL(fileURLWithPath:next.watchFolder)
            let destination = next.destinationFolder.isEmpty ? watch : URL(fileURLWithPath:next.destinationFolder)
            do {
                let files = try FileManager.default.contentsOfDirectory(at:watch,includingPropertiesForKeys:nil)
                for file in files where ArchiveExtractor.supported(file) { self.completed[file] = self.stamp(file) }
                guard (try destination.resourceValues(forKeys:[.isDirectoryKey])).isDirectory == true else { throw ArchiveFailure(message:"保存先フォルダーがありません。") }
                let timer = DispatchSource.makeTimerSource(queue:self.queue); timer.schedule(deadline:.now()+1,repeating:1)
                timer.setEventHandler { [weak self] in self?.scan(watch,destination:destination,options:next,token:token) }
                self.timer = timer; timer.resume(); self.report("自動解凍：監視中（新しく追加されたファイルが対象）")
            } catch { self.report("自動解凍：\(error.localizedDescription)") }
        }
    }
    private func report(_ text: String) { DispatchQueue.main.async { [weak self] in self?.onStatus?(text) } }
    private func scan(_ watch: URL,destination: URL,options: ArchiveSettings,token:Int) {
        guard ticket() == token else { return }
        do {
            let files = try FileManager.default.contentsOfDirectory(at:watch,includingPropertiesForKeys:nil).filter { ArchiveExtractor.supported($0) }
            let existing = Set(files)
            seen = seen.filter { existing.contains($0.key) }; completed = completed.filter { existing.contains($0.key) }; stable = stable.filter { existing.contains($0.key) }
            for file in files {
                guard ticket() == token, let current = stamp(file), completed[file] != current else { continue }
                if seen[file] == current { stable[file,default:0] += 1 } else { stable[file] = 0; seen[file] = current }
                guard stable[file,default:0] >= 2 else { continue }
                completed[file] = current
                do {
                    report("解凍中：\(file.lastPathComponent)")
                    let output = try ArchiveExtractor.extract(file,into:destination) { self.ticket() != token }
                    guard self.ticket() == token, self.stamp(file) == current else { try? FileManager.default.removeItem(at:output); continue }
                    if options.trashAfterExtraction { try FileManager.default.trashItem(at:file,resultingItemURL:nil) }
                    report("自動解凍しました：\(output.lastPathComponent)")
                } catch is CancellationError { return }
                catch { report("解凍できません：\(file.lastPathComponent) — \(error.localizedDescription)") }
            }
        } catch { report("監視フォルダーを読み込めません：\(error.localizedDescription)") }
    }
}
