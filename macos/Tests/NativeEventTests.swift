import Foundation
import CoreGraphics

@main struct NativeEventTests {
    static func main() throws {
        let marker: Int64 = 0x52454C59524D
        for continuous in [false,true] {
            for inverted in [false,true] {
                for sign in [Int32(-1),Int32(1)] {
                    let delta = inverted ? -sign : sign
                    let event = CGEvent(scrollWheelEvent2Source:nil,units:continuous ? .pixel : .line,wheelCount:1,wheel1:delta*12,wheel2:0,wheel3:0)!
                    event.setIntegerValueField(.scrollWheelEventIsContinuous,value:continuous ? 1 : 0)
                    let before = event.getIntegerValueField(.scrollWheelEventDeltaAxis1)
                    precondition(NativeEvents.scrollInput(event,invertedFromDevice:inverted) == (sign > 0 ? 270 : 271), "Natural scrolling must not reverse device-direction Action bindings")
                    precondition(event.getIntegerValueField(.scrollWheelEventDeltaAxis1) == before, "Action classification does not mutate ordinary scrolling")
                }
            }
        }

        guard let key = NativeEvents.key(8, flags: [.maskCommand, .maskShift], marker: marker) else { fatalError("event allocation") }
        precondition(key.down.type == .keyDown && key.up.type == .keyUp)
        for event in [key.down, key.up] {
            precondition(event.getIntegerValueField(.keyboardEventKeycode) == 8)
            precondition(event.flags == [.maskCommand, .maskShift])
            precondition(event.getIntegerValueField(.eventSourceUserData) == marker)
        }
        for flag: CGEventFlags in [.maskShift,.maskControl,.maskCommand,.maskAlternate] {
            let click = NativeEvents.mousePress(0,flags:flag,physicalFlags:[],point:.zero,marker:marker)!
            precondition((click.begin + click.end).map { $0.type } == [.flagsChanged,.leftMouseDown,.leftMouseUp,.flagsChanged])
            precondition(click.begin.allSatisfy { $0.flags.contains(flag) } && click.end.last?.flags == [])
            let held = NativeEvents.mousePress(0,flags:flag,physicalFlags:flag,point:.zero,marker:marker)!
            precondition((held.begin + held.end).map { $0.type } == [.leftMouseDown,.leftMouseUp], "Never release the user's physically held modifier")
        }
        for button in UInt16(5)...31 {
            let press = NativeEvents.mousePress(button,flags:[],physicalFlags:[],point:.zero,marker:marker)!
            precondition((press.begin + press.end).map { $0.type } == [.otherMouseDown,.otherMouseUp])
            precondition((press.begin + press.end).allSatisfy { $0.getIntegerValueField(.mouseEventButtonNumber) == Int64(button) })
        }
        precondition(NativeEvents.mousePress(32,flags:[],physicalFlags:[],point:.zero,marker:marker) == nil)
        let wheel = CGEvent(scrollWheelEvent2Source:nil,units:.line,wheelCount:2,wheel1:3,wheel2:-2,wheel3:0)!
        wheel.setIntegerValueField(.scrollWheelEventIsContinuous,value:0)
        wheel.setIntegerValueField(.scrollWheelEventPointDeltaAxis1,value:30)
        wheel.setDoubleValueField(.scrollWheelEventFixedPtDeltaAxis1,value:3.5)
        for output: UInt16 in [270,271,272,273] {
            let mapped = NativeEvents.mappedScroll(wheel,input:270,output:output,flags:.maskControl,marker:marker)!
            let axis: CGEventField = output >= 272 ? .scrollWheelEventDeltaAxis2 : .scrollWheelEventDeltaAxis1
            let point: CGEventField = output >= 272 ? .scrollWheelEventPointDeltaAxis2 : .scrollWheelEventPointDeltaAxis1
            let fixed: CGEventField = output >= 272 ? .scrollWheelEventFixedPtDeltaAxis2 : .scrollWheelEventFixedPtDeltaAxis1
            let sign: Double = output % 2 == 0 ? 1 : -1
            precondition(mapped.getIntegerValueField(axis) == Int64(3*sign))
            precondition(mapped.getIntegerValueField(point) == Int64(30*sign))
            precondition(mapped.getDoubleValueField(fixed) == 3.5*sign)
            precondition(mapped.flags == .maskControl && mapped.timestamp == wheel.timestamp && mapped.location == wheel.location)
            precondition(wheel.getIntegerValueField(.scrollWheelEventDeltaAxis1) == 3,"Source scroll is not mutated")
        }
        precondition(NativeEvents.reverseDiscreteScroll(wheel))
        precondition(wheel.getIntegerValueField(.scrollWheelEventDeltaAxis1) == -3)
        precondition(wheel.getIntegerValueField(.scrollWheelEventDeltaAxis2) == 2)
        precondition(wheel.getIntegerValueField(.scrollWheelEventPointDeltaAxis1) == -30)
        precondition(wheel.getDoubleValueField(.scrollWheelEventFixedPtDeltaAxis1) == -3.5)
        wheel.setIntegerValueField(.scrollWheelEventIsContinuous,value:1)
        precondition(!NativeEvents.reverseDiscreteScroll(wheel))
        precondition(wheel.getIntegerValueField(.scrollWheelEventDeltaAxis1) == -3, "Trackpad direction preserved")
        let system = NativeEvents.systemShortcut(123,flags:.maskControl,physicalFlags:[],marker:marker)!
        precondition(system.map { $0.type } == [.flagsChanged,.keyDown,.keyUp,.flagsChanged])
        precondition(system.map { $0.getIntegerValueField(.keyboardEventKeycode) } == [59,123,123,59])
        precondition(system.map { $0.flags } == [.maskControl,[.maskControl,.maskSecondaryFn,.maskNumericPad],[.maskControl,.maskSecondaryFn,.maskNumericPad],[]])
        let nativeTile = NativeEvents.systemShortcut(123,flags:[.maskControl,.maskSecondaryFn],physicalFlags:[],marker:marker)!
        precondition(nativeTile.map { $0.type } == [.flagsChanged,.flagsChanged,.keyDown,.keyUp,.flagsChanged,.flagsChanged])
        precondition(nativeTile[2].flags.contains([.maskControl,.maskSecondaryFn,.maskNumericPad]))
        precondition(nativeTile.last?.flags == [] && nativeTile.allSatisfy { $0.getIntegerValueField(.eventSourceUserData) == marker })
        let alreadyHeld = NativeEvents.systemShortcut(123,flags:.maskControl,physicalFlags:.maskControl,marker:marker)!
        precondition(alreadyHeld.count == 2 && alreadyHeld.allSatisfy { $0.flags.subtracting([.maskSecondaryFn,.maskNumericPad]) == .maskControl }, "Never release physical Control")
        let multi = NativeEvents.systemShortcut(20,flags:[.maskCommand,.maskShift],physicalFlags:[],marker:marker)!
        precondition(multi.map { $0.getIntegerValueField(.keyboardEventKeycode) } == [56,55,20,20,55,56])
        precondition(multi.last?.flags == [] && multi.allSatisfy { $0.getIntegerValueField(.eventSourceUserData) == marker })
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
        let symbols: [UInt32:(UInt16,CGEventFlags)] = [33:(18,.maskShift),63:(44,.maskShift),64:(33,[])]
        let punctuation = TextInputRouting.pairs("！？!?＠",marker:marker,punctuationKeys:symbols)!
        precondition(punctuation.map { $0.down.getIntegerValueField(.keyboardEventKeycode) } == [18,44,18,44,33])
        precondition(punctuation.allSatisfy { $0.throughIME && !$0.down.flags.contains(.maskControl) })
        precondition(punctuation.prefix(4).allSatisfy { $0.down.flags == .maskShift && $0.up.flags == .maskShift })
        let literal = "日本語👩‍💻abc！？\n"
        func decoded(_ output: [TextInputRouting.Output]) -> String {
            var units: [UInt16] = []
            for pair in output {
                var buffer = [UniChar](repeating:0,count:20), length = 0
                pair.down.keyboardGetUnicodeString(maxStringLength:20,actualStringLength:&length,unicodeString:&buffer)
                units += buffer.prefix(length)
                precondition(pair.down.type == .keyDown && pair.up.type == .keyUp)
                precondition(pair.down.getIntegerValueField(.eventSourceUserData) == marker)
            }
            return String(decoding:units,as:UTF16.self)
        }
        let unicodeOnly = TextInputRouting.pairs(literal,marker:marker,punctuationKeys:[:])!
        precondition(unicodeOnly.allSatisfy { !$0.throughIME })
        precondition(decoded(unicodeOnly) == literal)
        let mixed = TextInputRouting.pairs("日本語？👩‍💻！",marker:marker,punctuationKeys:symbols)!
        precondition(mixed.map { $0.throughIME } == [false,true,false,true])
        precondition(decoded(mixed.filter { !$0.throughIME }) == "日本語👩‍💻")
        let long = String(repeating:"a",count:19) + "😀" + String(repeating:"b",count:21)
        precondition(decoded(TextInputRouting.pairs(long,marker:marker,punctuationKeys:[:])!) == long)
        precondition(TextInputRouting.pairs("",marker:marker,punctuationKeys:symbols)!.isEmpty)
        _ = TextInputRouting.pairs("！？",marker:marker) // Read input source only; never posts events.
        print("PASS: shared text routing, full/half-width punctuation, layout-provided keys, literal fallback, mixed order, surrogate boundaries; no physical IME test")
        guard CommandLine.arguments.count == 2 else { fatalError("Pass a repository-local verification directory") }
        let lockURL = URL(fileURLWithPath: CommandLine.arguments[1]).appendingPathComponent("native-test.lock")
        let first = try InstanceLock(url: lockURL)
        do { _ = try InstanceLock(url: lockURL); fatalError("duplicate instance acquired lock") }
        catch InstanceLockError.busy { /* expected */ }
        let otherUserURL = lockURL.deletingLastPathComponent().appendingPathComponent("other-user/instance.lock")
        let otherUser = try InstanceLock(url:otherUserURL)
        otherUser.release()
        first.release(); first.release()
        let second = try InstanceLock(url: lockURL)
        precondition(FileManager.default.fileExists(atPath: lockURL.path))
        second.release()
        print("PASS: native key/Unicode pairs, flags, markers, UTF-16 and exclusive instance lock; zero events posted")
    }
}
