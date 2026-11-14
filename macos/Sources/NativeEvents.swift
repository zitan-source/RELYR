import CoreGraphics
import Foundation

/// Creates complete event pairs without posting them. Used by Deck and Unicode output.
struct NativeEvents {
    static func isContinuousScroll(_ event: CGEvent) -> Bool {
        event.type == .scrollWheel && (event.getIntegerValueField(.scrollWheelEventIsContinuous) != 0 ||
            event.getIntegerValueField(.scrollWheelEventScrollPhase) != 0 ||
            event.getIntegerValueField(.scrollWheelEventMomentumPhase) != 0)
    }

    /// Trackpads report pixel/fractional motion even when the whole-line delta is zero.
    /// Preserve native wheel acceleration and fractional resolution when adding modifiers.
    static func mappedScroll(_ source: CGEvent, input: UInt16, output: UInt16, flags: CGEventFlags, marker: Int64) -> CGEvent? {
        guard source.type == .scrollWheel, (270...273).contains(input), (270...273).contains(output), let copy = source.copy() else { return nil }
        let from = input >= 272 ? 1 : 0, to = output >= 272 ? 1 : 0
        let sign: Double = (input % 2 == output % 2) ? 1 : -1
        let integerAxes: [[CGEventField]] = [
            [.scrollWheelEventDeltaAxis1,.scrollWheelEventDeltaAxis2],
            [.scrollWheelEventPointDeltaAxis1,.scrollWheelEventPointDeltaAxis2]]
        let fractional: [CGEventField] = [.scrollWheelEventFixedPtDeltaAxis1,.scrollWheelEventFixedPtDeltaAxis2]
        // Identical direction keeps all axes and native metadata intact.
        if input != output {
            for axes in integerAxes {
                let delta = source.getIntegerValueField(axes[from])
                copy.setIntegerValueField(axes[0],value:0); copy.setIntegerValueField(axes[1],value:0)
                copy.setIntegerValueField(axes[to],value:Int64(Double(delta)*sign))
            }
            let delta = source.getDoubleValueField(fractional[from])
            copy.setDoubleValueField(fractional[0],value:0); copy.setDoubleValueField(fractional[1],value:0)
            copy.setDoubleValueField(fractional[to],value:delta*sign)
        }
        copy.flags = flags
        copy.setIntegerValueField(.eventSourceUserData,value:marker)
        return copy
    }

    static func scrollInput(_ event: CGEvent, invertedFromDevice: Bool = false) -> UInt16? {
        func delta(_ point: CGEventField, _ fixed: CGEventField, _ line: CGEventField) -> Double {
            if isContinuousScroll(event) {
                let pixels = event.getDoubleValueField(point)
                if pixels.isFinite && pixels != 0 { return pixels }
                let fractional = event.getDoubleValueField(fixed)
                if fractional.isFinite && fractional != 0 { return fractional }
            }
            return Double(event.getIntegerValueField(line))
        }
        let direction = invertedFromDevice ? -1.0 : 1.0
        let y = direction * delta(.scrollWheelEventPointDeltaAxis1,.scrollWheelEventFixedPtDeltaAxis1,.scrollWheelEventDeltaAxis1)
        let x = direction * delta(.scrollWheelEventPointDeltaAxis2,.scrollWheelEventFixedPtDeltaAxis2,.scrollWheelEventDeltaAxis2)
        guard x != 0 || y != 0 else { return nil }
        // Ignore minor cross-axis drift in two-finger gestures.
        if isContinuousScroll(event), abs(x) > abs(y) { return x > 0 ? 272 : 273 }
        return y != 0 ? (y > 0 ? 270 : 271) : (x > 0 ? 272 : 273)
    }

    let down: CGEvent
    let up: CGEvent

    /// Discrete wheel only. Pixel/continuous devices (including trackpads) retain OS direction.
    @discardableResult static func reverseDiscreteScroll(_ event: CGEvent) -> Bool {
        guard event.type == .scrollWheel, event.getIntegerValueField(.scrollWheelEventIsContinuous) == 0 else { return false }
        let integerFields: [CGEventField] = [.scrollWheelEventDeltaAxis1,.scrollWheelEventDeltaAxis2,.scrollWheelEventDeltaAxis3,
                                    .scrollWheelEventPointDeltaAxis1,.scrollWheelEventPointDeltaAxis2,.scrollWheelEventPointDeltaAxis3]
        let fixedFields: [CGEventField] = [.scrollWheelEventFixedPtDeltaAxis1,.scrollWheelEventFixedPtDeltaAxis2,.scrollWheelEventFixedPtDeltaAxis3]
        // Setting a line delta also changes its point/fixed-point fields. Snapshot every
        // original value first, then restore the inverted point/fixed values last.
        let integers = integerFields.map { event.getIntegerValueField($0) }
        let fixed = fixedFields.map { event.getDoubleValueField($0) }
        for (field,value) in zip(integerFields,integers) {
            event.setIntegerValueField(field,value:value == Int64.min ? Int64.max : -value)
        }
        for (field,value) in zip(fixedFields,fixed) {
            event.setDoubleValueField(field,value:-value)
        }
        return true
    }
    static func key(_ code: UInt16, flags: CGEventFlags, marker: Int64) -> NativeEvents? {
        guard let down = CGEvent(keyboardEventSource: nil, virtualKey: code, keyDown: true),
              let up = CGEvent(keyboardEventSource: nil, virtualKey: code, keyDown: false) else { return nil }
        for event in [down, up] {
            event.flags = flags
            event.setIntegerValueField(.eventSourceUserData, value: marker)
        }
        return NativeEvents(down: down, up: up)
    }

    /// Own only the modifiers we press; never release a physically held modifier.
    static func systemShortcut(_ code: UInt16, flags: CGEventFlags, physicalFlags: CGEventFlags, marker: Int64) -> [CGEvent]? {
        guard let source = CGEventSource(stateID:.hidSystemState) else { return nil }
        let modifiers: [(CGEventFlags, UInt16)] = [(.maskSecondaryFn,63),(.maskControl,59),(.maskAlternate,58),(.maskShift,56),(.maskCommand,55)]
        let owned = modifiers.filter { flags.contains($0.0) && !physicalFlags.contains($0.0) }
        var result: [CGEvent] = [], current = physicalFlags
        func event(_ key: UInt16, down: Bool, modifier: Bool, state: CGEventFlags) -> CGEvent? {
            guard let e = CGEvent(keyboardEventSource:source,virtualKey:key,keyDown:down) else { return nil }
            if modifier { e.type = .flagsChanged }
            // CGEventCreateKeyboardEvent classifies arrows with Fn/numeric-pad bits.
            // Replacing all flags loses this native identity used by system shortcuts.
            let keyIdentity = modifier ? CGEventFlags() : e.flags.intersection([.maskSecondaryFn,.maskNumericPad])
            e.flags = state.union(keyIdentity); e.setIntegerValueField(.eventSourceUserData,value:marker)
            return e
        }
        for (flag,key) in owned {
            current.insert(flag)
            guard let e = event(key,down:true,modifier:true,state:current) else { return nil }; result.append(e)
        }
        for down in [true,false] {
            guard let e = event(code,down:down,modifier:false,state:physicalFlags.union(flags)) else { return nil }; result.append(e)
        }
        for (flag,key) in owned.reversed() {
            current.remove(flag)
            guard let e = event(key,down:false,modifier:true,state:current) else { return nil }; result.append(e)
        }
        return result
    }

    struct MousePress {
        let button: UInt16
        let flags: CGEventFlags
        let requestedFlags: CGEventFlags
        let begin: [CGEvent]
        let end: [CGEvent]
    }
    /// Preallocate a complete transaction before suppressing the physical press.
    static func mousePress(_ button: UInt16, flags: CGEventFlags, physicalFlags: CGEventFlags, point: CGPoint, marker: Int64) -> MousePress? {
        guard button <= 31 else { return nil }
        let modifiers: [(CGEventFlags, UInt16)] = [(.maskControl,59),(.maskAlternate,58),(.maskShift,56),(.maskCommand,55)]
        let owned = modifiers.filter { flags.contains($0.0) && !physicalFlags.contains($0.0) }
        var begin: [CGEvent] = [], end: [CGEvent] = [], state = physicalFlags
        func modifier(_ key: UInt16, down: Bool, flags: CGEventFlags) -> CGEvent? {
            guard let event = CGEvent(keyboardEventSource:nil,virtualKey:key,keyDown:down) else { return nil }
            event.type = .flagsChanged; event.flags = flags
            event.setIntegerValueField(.eventSourceUserData,value:marker); return event
        }
        for (flag,key) in owned {
            state.insert(flag)
            guard let event = modifier(key,down:true,flags:state) else { return nil }; begin.append(event)
        }
        let downType: CGEventType = button == 0 ? .leftMouseDown : (button == 1 ? .rightMouseDown : .otherMouseDown)
        let upType: CGEventType = button == 0 ? .leftMouseUp : (button == 1 ? .rightMouseUp : .otherMouseUp)
        guard let down = CGEvent(mouseEventSource:nil,mouseType:downType,mouseCursorPosition:point,mouseButton:button == 0 ? .left : (button == 1 ? .right : .center)),
              let up = CGEvent(mouseEventSource:nil,mouseType:upType,mouseCursorPosition:point,mouseButton:button == 0 ? .left : (button == 1 ? .right : .center)) else { return nil }
        for event in [down,up] { event.setIntegerValueField(.mouseEventButtonNumber,value:Int64(button)); event.flags = physicalFlags.union(flags); event.setIntegerValueField(.mouseEventClickState,value:1); event.setIntegerValueField(.eventSourceUserData,value:marker) }
        begin.append(down); end.append(up)
        for (flag,key) in owned.reversed() {
            state.remove(flag)
            guard let event = modifier(key,down:false,flags:state) else { return nil }; end.append(event)
        }
        return MousePress(button:button,flags:physicalFlags.union(flags),requestedFlags:flags,begin:begin,end:end)
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
