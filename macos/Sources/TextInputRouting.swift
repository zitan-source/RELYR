import Carbon
import CoreGraphics
import Foundation

/// Build once per text action, outside the event tap. No input-source changes or
/// Enter/paste fallback: the active IME retains ownership of its composition.
enum TextInputRouting {
    struct Output {
        let events: NativeEvents
        let throughIME: Bool
        var down: CGEvent { events.down }
        var up: CGEvent { events.up }
    }
    static func pairs(_ text: String, marker: Int64) -> [Output]? {
        guard let source = TISCopyCurrentKeyboardInputSource()?.takeRetainedValue() else {
            return pairs(text, marker:marker, punctuationKeys:[:])
        }
        let languages = TISGetInputSourceProperty(source,kTISPropertyInputSourceLanguages).map {
            Unmanaged<CFArray>.fromOpaque($0).takeUnretainedValue() as NSArray
        }
        let ascii = TISGetInputSourceProperty(source,kTISPropertyInputSourceIsASCIICapable).map {
            CFBooleanGetValue(Unmanaged<CFBoolean>.fromOpaque($0).takeUnretainedValue())
        } ?? true
        guard !ascii, languages?.contains("ja") == true,
              let layoutSource = TISCopyCurrentASCIICapableKeyboardLayoutInputSource()?.takeRetainedValue(),
              let raw = TISGetInputSourceProperty(layoutSource,kTISPropertyUnicodeKeyLayoutData) else {
            return pairs(text,marker:marker,punctuationKeys:[:])
        }
        let data = Unmanaged<CFData>.fromOpaque(raw).takeUnretainedValue()
        guard let bytes = CFDataGetBytePtr(data) else { return pairs(text,marker:marker,punctuationKeys:[:]) }
        let layout = UnsafeRawPointer(bytes).assumingMemoryBound(to:UCKeyboardLayout.self)
        var keys: [UInt32:(UInt16,CGEventFlags)] = [:]
        for shift in [false,true] {
            for code in UInt16(0)..<UInt16(128) {
                var dead: UInt32 = 0, length = 0
                var chars = [UniChar](repeating:0,count:4)
                let status = UCKeyTranslate(layout,code,UInt16(kUCKeyActionDown),shift ? UInt32(shiftKey >> 8) : 0,
                                            UInt32(LMGetKbdType()),OptionBits(kUCKeyTranslateNoDeadKeysMask),&dead,4,&length,&chars)
                if status == noErr, length == 1, isPunctuation(UInt32(chars[0])), keys[UInt32(chars[0])] == nil {
                    keys[UInt32(chars[0])] = (code,shift ? .maskShift : [])
                }
            }
        }
        return pairs(text,marker:marker,punctuationKeys:keys)
    }
    private static func isPunctuation(_ value: UInt32) -> Bool {
        (33...47).contains(value) || (58...64).contains(value) || (91...96).contains(value) || (123...126).contains(value)
    }
    /// Literal Unicode fallback preserves arbitrary strings and surrogate pairs.
    /// Only keyboard punctuation is routed through the Japanese IME; letters are
    /// never romanized and user-authored payloads are never rewritten.
    static func pairs(_ text: String, marker: Int64, punctuationKeys: [UInt32:(UInt16,CGEventFlags)]) -> [Output]? {
        var result: [Output] = [], pending: [UInt16] = []
        func flush() -> Bool {
            guard !pending.isEmpty else { return true }
            guard let pair = NativeEvents.unicode(pending,marker:marker) else { return false }
            result.append(Output(events:pair,throughIME:false)); pending.removeAll(keepingCapacity:true); return true
        }
        for scalar in text.unicodeScalars {
            let normalized = (0xFF01...0xFF5E).contains(scalar.value) ? scalar.value - 0xFEE0 : scalar.value
            if isPunctuation(normalized), let (code,flags) = punctuationKeys[normalized] {
                guard flush(), let pair = NativeEvents.key(code,flags:flags,marker:marker) else { return nil }
                result.append(Output(events:pair,throughIME:true))
            } else {
                let units = Array(String(scalar).utf16)
                if pending.count + units.count > 20, !flush() { return nil }
                pending += units
            }
        }
        guard flush() else { return nil }; return result
    }
}
