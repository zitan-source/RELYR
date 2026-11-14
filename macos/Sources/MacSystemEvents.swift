import AppKit

/// Event creation only. Posting remains in InputEngine's deferred queue.
enum MacSystemEvents {
    static func mediaKey(_ command: SystemCommand) -> Int? {
        // IOKit/hidsystem/ev_keymap.h NX_KEYTYPE_* constants.
        switch command {
        case .volumeUp: return 0
        case .volumeDown: return 1
        case .mute: return 7
        case .mediaPlayPause: return 16
        case .mediaNext: return 19
        case .mediaPrevious: return 20
        case .brightnessUp: return 2
        case .brightnessDown: return 3
        default: return nil
        }
    }
    static func media(_ key: Int, marker: Int64) -> [CGEvent]? {
        var events: [CGEvent] = []
        for down in [true,false] {
            let state = down ? 0xA : 0xB
            guard let event = NSEvent.otherEvent(with:.systemDefined,location:.zero,modifierFlags:NSEvent.ModifierFlags(rawValue:UInt(state << 8)),timestamp:0,windowNumber:0,context:nil,subtype:8,data1:(key << 16) | (state << 8),data2:-1)?.cgEvent else { return nil }
            event.setIntegerValueField(.eventSourceUserData,value:marker); events.append(event)
        }
        return events
    }
    static func scroll(_ command: SystemCommand, marker: Int64, modifiers: CGEventFlags = []) -> CGEvent? {
        let step: Int32 = modifiers.isEmpty ? 3 : 1
        let delta: (Int32,Int32)
        switch command {
        case .scrollUp: delta = (step,0)
        case .scrollDown: delta = (-step,0)
        case .scrollLeft: delta = (0,step)
        case .scrollRight: delta = (0,-step)
        default: return nil
        }
        guard let event = CGEvent(scrollWheelEvent2Source:nil,units:.line,wheelCount:2,wheel1:delta.0,wheel2:delta.1,wheel3:0) else { return nil }
        if !modifiers.isEmpty { event.flags = modifiers }
        event.setIntegerValueField(.eventSourceUserData,value:marker); return event
    }
}
