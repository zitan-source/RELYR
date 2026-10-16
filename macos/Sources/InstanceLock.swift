import Foundation
import Darwin

enum InstanceLockError: LocalizedError {
    case busy, system(String)
    var errorDescription: String? {
        switch self {
        case .busy: return "RELYR Mac Trialはすでに起動しています。"
        case .system(let message): return "起動ロックを作成できません：\(message)"
        }
    }
}

/// A kernel-owned lock avoids two simultaneous launches both activating and quitting each other.
final class InstanceLock {
    private var descriptor: Int32 = -1
    init(url: URL) throws {
        try FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
        let fd = open(url.path, O_CREAT | O_RDWR | O_CLOEXEC, S_IRUSR | S_IWUSR)
        guard fd >= 0 else { throw InstanceLockError.system(String(cString: strerror(errno))) }
        guard flock(fd, LOCK_EX | LOCK_NB) == 0 else {
            let failure = errno
            close(fd)
            if failure == EWOULDBLOCK { throw InstanceLockError.busy }
            throw InstanceLockError.system(String(cString: strerror(failure)))
        }
        descriptor = fd
    }
    func release() {
        guard descriptor >= 0 else { return }
        flock(descriptor, LOCK_UN)
        close(descriptor)
        descriptor = -1
        // Keep the inode: removing it would allow another process to lock a different file at the same path.
    }
    deinit { release() }
}
