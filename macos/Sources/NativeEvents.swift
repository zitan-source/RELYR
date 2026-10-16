import CoreGraphics
import Foundation

/// Creates complete event pairs without posting them. Used by Deck and Unicode output.
struct NativeEvents {
    let down: CGEvent
    let up: CGEvent

    static func key(_ code: UInt16, flags: CGEventFlags, marker: Int64) -> NativeEvents? {
        guard let down = CGEvent(keyboardEventSource: nil, virtualKey: code, keyDown: true),
              let up = CGEvent(keyboardEventSource: nil, virtualKey: code, keyDown: false) else { return nil }
        for event in [down, up] {
            event.flags = flags
            event.setIntegerValueField(.eventSourceUserData, value: marker)
        }
        return NativeEvents(down: down, up: up)
    }

    static func unicode(_ units: [UInt16], marker: Int64) -> NativeEvents? {
        guard !units.isEmpty, units.count <= 20,
              Array(String(decoding: units, as: UTF16.self).utf16) == units,
              let pair = key(0, flags: [], marker: marker) else { return nil }
        for event in [pair.down, pair.up] {
            units.withUnsafeBufferPointer {
                event.keyboardSetUnicodeString(stringLength: $0.count, unicodeString: $0.baseAddress!)
            }
        }
        return pair
    }
}
