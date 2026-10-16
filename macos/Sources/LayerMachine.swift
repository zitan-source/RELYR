import Foundation

enum InputDecision: Equatable {
    case pass, consume, spaceTap, spaceDownThenPass
    case remap(UInt16, Modifiers)
    case launch(String)
    case text(String), url(String), deck
}

/// Pure state machine: no hooks, generated input, file I/O, or application launches.
final class LayerMachine {
    private enum SpaceState { case idle, pending, layer, passthrough }
    private enum Held { case passthrough, mapped(Mapping) }
    private var space: SpaceState = .idle
    private var held: [UInt16: Held] = [:]
    var configuration = Configuration()
    var runtimeProfileID = "default"
    private var mappings: [Mapping] { configuration.mappings(for: runtimeProfileID) }

    func key(_ code: UInt16, down: Bool, repeating: Bool = false, modifiers: Modifiers = []) -> InputDecision {
        if code == 49 { return spaceKey(down: down, repeating: repeating, modifiers: modifiers) }
        if let active = held[code] {
            if !down { held.removeValue(forKey: code) }
            switch active {
            case .passthrough:
                // A physical destination key can overlap a remapped source.
                return !down && outputIsHeld(code) ? .consume : .pass
            case .mapped(let mapping):
                if !mapping.action.isKey { return .consume }
                if !down && outputIsHeld(mapping.output) { return .consume }
                return .remap(mapping.output, mapping.modifiers)
            }
        }
        // Never capture a key already down when interception starts.
        guard down, !repeating else { return .pass }
        let layer: Layer = (space == .pending || space == .layer) ? .space : .normal
        if let mapping = mappings.first(where: { $0.layer == layer && $0.input == code }) {
            if layer == .space { space = .layer }
            held[code] = .mapped(mapping)
            switch mapping.action {
            case .key, .shortcut: return .remap(mapping.output, mapping.modifiers)
            case .launch: return .launch(mapping.application)
            case .text: return .text(mapping.payload)
            case .url: return .url(mapping.payload)
            case .deck: return .deck
            }
        }
        held[code] = .passthrough
        return flushPendingSpace()
    }

    func outputIsHeld(_ output: UInt16) -> Bool {
        if output == 49 && space == .passthrough { return true }
        return held.contains { code, active in
            switch active {
            case .passthrough: return code == output
            case .mapped(let mapping): return mapping.action.isKey && mapping.output == output
            }
        }
    }

    private func spaceKey(down: Bool, repeating: Bool, modifiers: Modifiers) -> InputDecision {
        if !down {
            let previous = space
            space = .idle
            switch previous {
            case .pending: return outputIsHeld(49) ? .consume : .spaceTap
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
                  mappings.contains(where: { $0.layer == .space }) else {
                space = .passthrough
                return .pass
            }
            space = .pending
            return .consume
        }
    }

    /// Restore the original Space before an unrelated key, mouse click, or modifier.
    func flushPendingSpace() -> InputDecision {
        guard space == .pending else { return .pass }
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
        held.removeAll()
        space = .idle
        return outputs.sorted()
    }
}
