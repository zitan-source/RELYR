import Foundation
import CoreGraphics

@main struct NativeEventTests {
    static func main() throws {
        let marker: Int64 = 0x52454C59524D
        guard let key = NativeEvents.key(8, flags: [.maskCommand, .maskShift], marker: marker) else { fatalError("event allocation") }
        precondition(key.down.type == .keyDown && key.up.type == .keyUp)
        for event in [key.down, key.up] {
            precondition(event.getIntegerValueField(.keyboardEventKeycode) == 8)
            precondition(event.flags == [.maskCommand, .maskShift])
            precondition(event.getIntegerValueField(.eventSourceUserData) == marker)
        }
        let text = "こんにちは🌸\nABC"
        let units = Array(text.utf16)
        guard let pair = NativeEvents.unicode(units, marker: marker) else { fatalError("Unicode allocation") }
        precondition(pair.down.type == .keyDown && pair.up.type == .keyUp)
        for event in [pair.down, pair.up] {
            var length = 0
            var output = [UInt16](repeating: 0, count: 20)
            event.keyboardGetUnicodeString(maxStringLength: output.count, actualStringLength: &length, unicodeString: &output)
            precondition(Array(output.prefix(length)) == units)
            precondition(event.flags.isEmpty)
            precondition(event.getIntegerValueField(.eventSourceUserData) == marker)
        }
        precondition(NativeEvents.unicode([], marker: marker) == nil)
        precondition(NativeEvents.unicode([UInt16](repeating: 65, count: 21), marker: marker) == nil)
        precondition(NativeEvents.unicode([0xD800], marker: marker) == nil)
        precondition(NativeEvents.unicode([0xDC00], marker: marker) == nil)
        guard CommandLine.arguments.count == 2 else { fatalError("Pass a repository-local verification directory") }
        let lockURL = URL(fileURLWithPath: CommandLine.arguments[1]).appendingPathComponent("native-test.lock")
        let first = try InstanceLock(url: lockURL)
        do { _ = try InstanceLock(url: lockURL); fatalError("duplicate instance acquired lock") }
        catch InstanceLockError.busy { /* expected */ }
        first.release(); first.release()
        let second = try InstanceLock(url: lockURL)
        precondition(FileManager.default.fileExists(atPath: lockURL.path))
        second.release()
        print("PASS: native key/Unicode pairs, flags, markers, UTF-16 and exclusive instance lock; zero events posted")
    }
}
