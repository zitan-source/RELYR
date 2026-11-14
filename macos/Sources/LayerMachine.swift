import Foundation

enum InputDecision: Equatable {
    case profile(String)
    case macro(String)
    case gesture(String)
    case pass, consume, spaceTap, spaceDownThenPass
    case remap(UInt16, Modifiers)
    case mouse(UInt16, Modifiers)
    case launch(String)
    case text(String), url(String), deck(String)
    case window(WindowCommand, Bool), file(String), system(SystemCommand, Modifiers = [])
    case deferPress, restoreTap(UInt16), pulse(Mapping, Modifiers), layerDownThenPass(UInt16)
}

/// Pure state machine: no hooks, generated input, file I/O, or application launches.
final class LayerMachine {
    private enum SpaceState { case idle, pending, layer, passthrough }
    private struct Deferred {
        var tap: Mapping?
        var hold: Mapping
        var deadline: TimeInterval
        var modifiers: Modifiers
    }
    private enum Held { case passthrough, mapped(Mapping), deferred(Deferred), completed }
    private var caps: SpaceState = .idle
    private var mouseHold: Deferred?
    private var space: SpaceState = .idle
    private var lastSpaceTap: TimeInterval?
    private var spaceRepeatDeadline: TimeInterval?
    private var spaceRepeated = false
    private(set) var mouseLayer: Layer?
    private var mouseUsed = false
    private var mouseDragging = false
    enum MouseDecision: Equatable { case ordinary, consume, replayClick, endDrag }
    func mouseSource(_ code: UInt16, down: Bool, now: TimeInterval = ProcessInfo.processInfo.systemUptime, modifiers: Modifiers = []) -> MouseDecision {
        if down { lastSpaceTap = nil }
        if mouseLayer?.mouseCode == code {
            if down { return .consume }
            let result: MouseDecision = mouseDragging ? .endDrag : (mouseUsed ? .consume : .replayClick)
            mouseLayer = nil; mouseUsed = false; mouseDragging = false; mouseHold = nil
            return result
        }
        // A mapped button is a child of the already-held keyboard layer, not
        // a new mouse-layer source. Its matching Up remains owned by held[].
        let keyboardLayer: Layer? = (caps == .pending || caps == .layer) ? .capsLock : ((space == .pending || space == .layer) ? .space : nil)
        if down, mouseLayer == nil, let layer = keyboardLayer,
           mappings.contains(where: { $0.layer == layer && $0.input == code && InputPolicy.allows(code,layer:layer,trigger:$0.trigger) }) {
            return .ordinary
        }
        if down, mappings.contains(where: { $0.layer == .normal && $0.input == code && $0.action == .gesture && $0.trigger == .tap }) { return .ordinary }
        if down, mouseLayer == nil, let layer = Layer.allCases.first(where: { $0.mouseCode == code }),
           (mappings.contains(where: { $0.layer == layer })) {
            mouseLayer = layer; mouseUsed = false; mouseDragging = false
            if let hold = mappings.first(where: { $0.layer == .normal && $0.input == code && $0.trigger == .hold }) {
                mouseHold = Deferred(tap: nil, hold: hold, deadline: now + Double(hold.holdMilliseconds) / 1000, modifiers: modifiers)
            }
            return .consume
        }
        return .ordinary
    }
    func beginMouseDrag() -> Bool {
        guard mouseLayer != nil, !mouseUsed, !mouseDragging else { return false }
        mouseDragging = true; mouseHold = nil
        return true
    }
    var isMouseDragging: Bool { mouseDragging }
    func endMouseDrag() { mouseDragging = false; mouseUsed = true }

    private var held: [UInt16: Held] = [:]
    private var lastTypingActivity: TimeInterval?
    // Physical letter, digit and punctuation keys, including JIS and keypad.
    private static let typingKeys: Set<UInt16> = Set(0...35).union(37...47).union([50,65,67,69,75,78,81,82,83,84,85,86,87,88,89,91,92,93,94,95])
    private func isTypingKey(_ code: UInt16, modifiers: Modifiers) -> Bool {
        Self.typingKeys.contains(code) && modifiers.subtracting(.shift).isEmpty
    }
    var configuration = Configuration() { didSet { cachedMappings = nil; lastTypingActivity = nil } }
    var runtimeProfileID = "default" { didSet { if oldValue != runtimeProfileID { cachedMappings = nil; lastTypingActivity = nil } } }
    var mouseDeviceID: String? { didSet { if oldValue != mouseDeviceID { cachedMappings = nil } } }
    private struct Slot: Hashable { let layer: Layer; let input: UInt16; let trigger: Trigger }
    private var cachedMappings: [Mapping]?
    private var mappingIndex: [Slot: Mapping] = [:]
    private var activeLayers: Set<Layer> = []
    private var mappings: [Mapping] {
        if let cachedMappings = cachedMappings { return cachedMappings }
        let current = configuration.mappings(for: runtimeProfileID,mouseDevice:mouseDeviceID)
        mappingIndex.removeAll(keepingCapacity:true); activeLayers.removeAll(keepingCapacity:true)
        for mapping in current {
            let slot = Slot(layer:mapping.layer,input:mapping.input,trigger:mapping.trigger)
            if mappingIndex[slot] == nil { mappingIndex[slot] = mapping }
            activeLayers.insert(mapping.layer)
        }
        cachedMappings = current
        return current
    }
    private func mapping(_ code: UInt16, layer: Layer, trigger: Trigger) -> Mapping? {
        _ = mappings
        return mappingIndex[Slot(layer:layer,input:code,trigger:trigger)]
    }
    private func hasMappings(in layer: Layer) -> Bool { _ = mappings; return activeLayers.contains(layer) }

    func usesControlLayer(_ code: UInt16, modifiers: Modifiers) -> Bool {
        if let active = held[code] {
            switch active {
            case .mapped(let mapping): return mapping.layer == .control
            case .deferred(let pending): return pending.hold.layer == .control
            default: return false
            }
        }
        return code < 256 && mouseLayer == nil && caps == .idle && space == .idle &&
            modifiers.contains(.control) && hasMappings(in:.control)
    }

    private func defaultMouseAction(_ code: UInt16, layer: Layer) -> Mapping? {
        guard layer == .normal && (code == 259 || code == 260) else { return nil }
        return Mapping(layer:.normal,input:code,action:.system,payload:code == 259 ? "navigateBack" : "navigateForward")
    }
    func key(_ code: UInt16, down: Bool, repeating: Bool = false, modifiers: Modifiers = [], now: TimeInterval = ProcessInfo.processInfo.systemUptime, allowHold: Bool = true, qualifiedLayer: Layer? = nil) -> InputDecision {
        if code != 49 && down { lastSpaceTap = nil }
        if code == 105 { return capsKey(down: down, repeating: repeating) }
        if code == 49 { return spaceKey(down: down, repeating: repeating, modifiers: modifiers, now: now) }
        if let active = held[code] {
            if !down { held.removeValue(forKey: code) }
            switch active {
            case .completed: return .consume
            case .deferred(let pending):
                guard !down else { return .consume }
                if now >= pending.deadline { return .pulse(pending.hold, pending.modifiers) }
                if pending.hold.layer == .normal, pending.tap == nil, isTypingKey(code,modifiers:pending.modifiers) { lastTypingActivity = now }
                return pending.tap.map { .pulse($0, pending.modifiers) } ?? .restoreTap(code)
            case .passthrough:
                if isTypingKey(code,modifiers:modifiers) { lastTypingActivity = now }
                // A physical destination key can overlap a remapped source.
                return !down && outputIsHeld(code) ? .consume : .pass
            case .mapped(let mapping):
                if mapping.action == .mouse { return down ? .consume : .mouse(mapping.output, mapping.modifiers) }
                if !mapping.action.isKey { return .consume }
                if !down && outputIsHeld(mapping.output) { return .consume }
                return .remap(mapping.output, mapping.modifiers)
            }
        }
        // Never capture a key already down when interception starts.
        guard down, !repeating else { return .pass }
        let controlLayer = usesControlLayer(code, modifiers: modifiers)
        let outputModifiers = controlLayer ? modifiers.subtracting(.control) : modifiers
        let layer: Layer = qualifiedLayer ?? (controlLayer ? .control : nil) ?? mouseLayer ?? ((caps == .pending || caps == .layer) ? .capsLock : ((space == .pending || space == .layer) ? .space : .normal))
        let tap = mapping(code,layer:layer,trigger:.tap)
        let ordinaryText = layer == .normal && tap == nil && isTypingKey(code,modifiers:modifiers)
        let typing = ordinaryText && configuration.typingPriority != false && lastTypingActivity.map { now >= $0 && now - $0 <= 0.300 } == true
        if ordinaryText { lastTypingActivity = now }
        if allowHold, !typing, InputPolicy.allows(code, layer: layer, trigger: .hold),
           let hold = mapping(code,layer:layer,trigger:.hold) {
            held[code] = .deferred(Deferred(tap: mapping(code,layer:layer,trigger:.tap) ?? defaultMouseAction(code,layer:layer),
                                         hold: hold, deadline: now + Double(hold.holdMilliseconds) / 1000, modifiers: outputModifiers))
            useLayer(layer)
            return .deferPress
        }
        if InputPolicy.allows(code, layer: layer), let mapping = mapping(code,layer:layer,trigger:.tap) ?? defaultMouseAction(code,layer:layer) {
            let mapping = mapping.resolvedWindowShortcut
            useLayer(layer)
            held[code] = .mapped(mapping)
            switch mapping.action {
            case .disabled: return .consume
            case .profile: return .profile(mapping.payload)
            case .macro: return .macro(mapping.payload)
            case .gesture: return .gesture(mapping.payload)
            case .mouse: return .mouse(mapping.output, mapping.modifiers)
            case .key, .shortcut: return .remap(mapping.output, mapping.modifiers)
            case .launch: return .launch(mapping.application)
            case .text: return .text(mapping.payload)
            case .url: return .url(mapping.payload)
            case .deck: return .deck(mapping.payload)
            case .file: return .file(mapping.application)
            case .system: return SystemCommand(rawValue: mapping.payload).map { .system($0, mapping.modifiers) } ?? .consume
            case .window: return WindowCommand(rawValue: mapping.payload).map { .window($0, mapping.layer == .menuBar) } ?? .consume
            }
        }
        held[code] = .passthrough
        return flushPendingSpace()
    }

    // A typing chord commits HOLD-only ordinary keys before the following key.
    // Explicit TAP actions and layer/mouse holds retain their configured semantics.
    func releaseTypingHolds(before code: UInt16) -> [UInt16] {
        guard code < 256 else { return [] }
        let pending = held.compactMap { source, state -> (UInt16, TimeInterval)? in
            guard source != code, source < 256, case .deferred(let press) = state,
                  press.hold.layer == .normal, press.tap == nil else { return nil }
            return (source, press.deadline - Double(press.hold.holdMilliseconds) / 1000)
        }.sorted { $0.1 == $1.1 ? $0.0 < $1.0 : $0.1 < $1.1 }
        for (source, _) in pending { held[source] = .passthrough }
        return pending.map { $0.0 }
    }

    func cancelDeferredForDrag(_ code: UInt16) -> Bool {
        guard case .deferred? = held[code] else { return false }
        held[code] = .passthrough; return true
    }
    func ownsPress(_ code: UInt16) -> Bool {
        guard let state = held[code] else { return false }
        if case .passthrough = state { return false }; return true
    }
    func outputIsHeld(_ output: UInt16) -> Bool {
        if output == 49 && space == .passthrough { return true }
        return held.contains { code, active in
            switch active {
            case .completed, .deferred: return false
            case .passthrough: return code == output
            case .mapped(let mapping): return mapping.action.isKey && mapping.output == output
            }
        }
    }

    func useWheelLayer() { useLayer(wheelLayer) }
    var wheelLayer: Layer {
        mouseLayer ?? ((caps == .pending || caps == .layer) ? .capsLock : ((space == .pending || space == .layer) ? .space : .normal))
    }
    private func useLayer(_ layer: Layer) {
        if layer == .space { space = .layer; spaceRepeatDeadline = nil; lastSpaceTap = nil }
        if layer == .capsLock { caps = .layer }
        if layer == mouseLayer { mouseUsed = true; mouseHold = nil }
    }
    var hasSpaceRepeatDeadline: Bool { spaceRepeatDeadline != nil }
    var nextDeadline: TimeInterval? {
        var next = spaceRepeatDeadline
        if let mouseHold = mouseHold { next = min(next ?? mouseHold.deadline, mouseHold.deadline) }
        for value in held.values {
            if case .deferred(let pending) = value { next = min(next ?? pending.deadline,pending.deadline) }
        }
        return next
    }

    func advance(now: TimeInterval = ProcessInfo.processInfo.systemUptime) -> [(Mapping, Modifiers)] {
        guard let deadline = nextDeadline, now >= deadline else { return [] }
        var results: [(Mapping, Modifiers)] = []
        if let deadline = spaceRepeatDeadline, now >= deadline, space == .pending {
            spaceRepeated = true; spaceRepeatDeadline = now + 0.055
            results.append((Mapping(layer:.normal,input:49,action:.key,output:49),[]))
        }
        for code in held.keys.sorted() {
            if case .deferred(let pending)? = held[code], now >= pending.deadline {
                held[code] = .completed; results.append((pending.hold, pending.modifiers))
            }
        }
        if let pending = mouseHold, now >= pending.deadline {
            mouseHold = nil; mouseUsed = true; results.append((pending.hold, pending.modifiers))
        }
        return results
    }
    private func capsKey(down: Bool, repeating: Bool) -> InputDecision {
        if !down {
            let previous = caps; caps = .idle
            switch previous {
            case .pending: return .restoreTap(105)
            case .layer: return .consume
            default: return .pass
            }
        }
        if caps == .pending || caps == .layer { return .consume }
        if caps == .passthrough { return .pass }
        guard !repeating, hasMappings(in:.capsLock) else { caps = .passthrough; return .pass }
        caps = .pending; return .consume
    }

    private func spaceKey(down: Bool, repeating: Bool, modifiers: Modifiers, now: TimeInterval) -> InputDecision {
        if !down {
            let previous = space
            space = .idle; spaceRepeatDeadline = nil
            if spaceRepeated { spaceRepeated = false; lastSpaceTap = nil; return .consume }
            switch previous {
            case .pending:
                lastSpaceTap = now
                return outputIsHeld(49) ? .consume : .spaceTap
            case .layer: return .consume
            case .idle, .passthrough: return outputIsHeld(49) ? .consume : .pass
            }
        }
        switch space {
        case .pending, .layer: return .consume
        case .passthrough: return .pass
        case .idle:
            // Preserve Cmd-Space, Shift-Space, etc. A held Space without mappings repeats normally.
            guard !repeating, modifiers.isEmpty, configuration.spaceEnabled,
                  (hasMappings(in:.space)) else {
                lastSpaceTap = nil; spaceRepeatDeadline = nil
                space = .passthrough
                return .pass
            }
            spaceRepeatDeadline = lastSpaceTap.map { now >= $0 && now - $0 <= 0.5 ? now + 0.4 : nil } ?? nil
            lastSpaceTap = nil; spaceRepeated = false
            space = .pending
            return .consume
        }
    }

    /// Restore the original Space before an unrelated key, mouse click, or modifier.
    func flushPendingSpace() -> InputDecision {
        if caps == .pending { caps = .passthrough; return .layerDownThenPass(105) }
        lastSpaceTap = nil; spaceRepeatDeadline = nil
        guard space == .pending else { return .pass }
        if spaceRepeated { space = .layer; return .pass }
        space = .passthrough
        return .spaceDownThenPass
    }

    /// Called on disable, sleep, tap failure, and termination. Only generated keys need releases.
    func reset() -> [UInt16] {
        var outputs = Set<UInt16>()
        for active in held.values {
            if case .mapped(let mapping) = active, mapping.action.isKey {
                if case .passthrough? = held[mapping.output] { continue }
                if mapping.output == 49 && space == .passthrough { continue }
                outputs.insert(mapping.output)
            }
        }
        // Passed-through physical keys retain ownership. Their real Up reaches the OS after disabling.
        held.removeAll(); lastTypingActivity = nil
        space = .idle; caps = .idle; lastSpaceTap = nil; spaceRepeatDeadline = nil; spaceRepeated = false
        mouseLayer = nil; mouseUsed = false; mouseDragging = false; mouseHold = nil
        return outputs.sorted()
    }
}
