import Foundation

struct TrackpadContact {
    let id: Int
    let x: Double
    let y: Double
}
/// Tracks one gesture per contact session. Assigned edge controls may emit bounded steps.
struct TrackpadRecognizer {
    private var start: Double?
    private var peak = 0
    private var baseline: [TrackpadContact] = []
    private var fired = false
    private var travelled = false
    private var edge: Int?
    private var edgeCancelled = false
    private var edgeAnchor = 0.0
    private var edgeLastPulse = -Double.infinity
    private var edgePulses = 0
    private(set) var adjustingVolume = false
    private var volumeDirection = 0.0
    private var volumeExtreme = 0.0

    static let volumeEdgeLimit = 0.085
    var ownsEdge: Bool { edgePulses > 0 || adjustingVolume }
    func mayOwnScroll(_ input: UInt16, width: Double, assigned: (UInt16) -> Bool) -> Bool {
        if ownsEdge { return true }
        guard !fired, !edgeCancelled, peak == 2, baseline.count == 2 else { return false }
        let candidates: [Int]
        switch input {
        case 270: candidates = [4,6]
        case 271: candidates = [5,7]
        case 272: candidates = [0,2]
        case 273: candidates = [1,3]
        default: return false
        }
        return candidates.contains { offset in
            guard assigned(UInt16(434 + offset)) else { return false }
            switch offset {
            case 0,1: return baseline.allSatisfy { $0.y >= 1-width }
            case 2,3: return baseline.allSatisfy { $0.y <= width }
            case 4,5: return baseline.allSatisfy { $0.x <= width }
            default: return baseline.allSatisfy { $0.x >= 1-width }
            }
        }
    }
    private var pendingCorners: [(code: UInt16, time: Double)] = []
    mutating func reset() { self = TrackpadRecognizer() }
    private mutating func clearContact() {
        start = nil; peak = 0; baseline = []; fired = false; travelled = false
        edge = nil; edgeCancelled = false; edgeAnchor = 0; edgeLastPulse = -.infinity; edgePulses = 0; adjustingVolume = false
        volumeDirection = 0; volumeExtreme = 0
    }
    var pendingCornerDeadline: Double? { pendingCorners.first.map { $0.time + 0.36 } }
    mutating func flush(time: Double) -> UInt16? {
        guard let pending = pendingCorners.first, time.isFinite, time >= pending.time + 0.36 else { return nil }
        pendingCorners.removeFirst()
        return pending.code
    }
    private func corner(_ point: TrackpadContact) -> UInt16? {
        let left = point.x <= 0.17, right = point.x >= 0.83
        let bottom = point.y <= 0.17, top = point.y >= 0.83
        if top && left { return 414 }
        if top && right { return 416 }
        if bottom && left { return 418 }
        if bottom && right { return 420 }
        return nil
    }
    mutating func update(_ points: [TrackpadContact], time: Double, doubleTapEnabled: (UInt16) -> Bool = { _ in true },
                         edgeWidth: Double = 0.12, edgeMapping: (UInt16) -> Bool? = { _ in nil },
                         isEdgeVolume: (UInt16) -> Bool = { _ in false },
                         edgeVolume: (UInt16, Double) -> Void = { _,_ in }) -> UInt16? {
        guard time.isFinite, points.count <= 10, points.allSatisfy({ $0.x.isFinite && $0.y.isFinite && (0...1).contains($0.x) && (0...1).contains($0.y) }) else { reset(); return nil }
        if points.isEmpty {
            defer { clearContact() }
            guard let start = start, time >= start, time-start < 0.35, !travelled, !fired else { return nil }
            if peak == 1, time-start >= 0.03, let first = baseline.first, let code = corner(first) {
                if let index = pendingCorners.firstIndex(where:{ $0.code == code && time >= $0.time && time-$0.time <= 0.36 }) {
                    pendingCorners.remove(at:index); return code + 1
                }
                if doubleTapEnabled(code + 1) { pendingCorners.append((code,time)); return nil }
                return code
            }
            return peak == 3 ? 400 : (peak == 4 ? 405 : nil)
        }
        if start == nil { start = time }
        guard let start = start, time >= start, time-start < 10 else { reset(); return nil }
        if adjustingVolume, points.count != peak || Set(points.map(\.id)) != Set(baseline.map(\.id)) {
            adjustingVolume = false
            fired = true
            return nil
        }
        if points.count > peak {
            // A second finger may land just after the first, before either moves.
            if travelled || ownsEdge || time-start > 0.12 { edgeCancelled = true }
            peak = points.count; baseline = points.sorted { $0.id < $1.id }
        }
        if points.count != peak { edgeCancelled = true }
        guard !fired, points.count == peak else { return nil }
        let sorted = points.sorted { $0.id < $1.id }
        guard sorted.map({$0.id}) == baseline.map({$0.id}), !baseline.isEmpty else { baseline = sorted; travelled = true; edgeCancelled = true; return nil }
        let dx = zip(sorted,baseline).map { $0.x-$1.x }.reduce(0,+)/Double(peak)
        let dy = zip(sorted,baseline).map { $0.y-$1.y }.reduce(0,+)/Double(peak)
        if abs(dx) > 0.025 || abs(dy) > 0.025 { travelled = true }
        let parallel = peak != 2 || hypot((sorted[0].x-baseline[0].x)-(sorted[1].x-baseline[1].x),
                                          (sorted[0].y-baseline[0].y)-(sorted[1].y-baseline[1].y)) < 0.055
        if peak <= 2, (parallel || adjustingVolume), !edgeCancelled, time-start >= (peak == 1 ? 0.017 : 0.045) {
            if let code = alongEdge(dx:dx,dy:dy,points:sorted,time:time,width:edgeWidth,mapping:edgeMapping,isVolume:isEdgeVolume,volume:edgeVolume) { return code }
        }
        if ownsEdge { return nil }
        if (peak == 3 || peak == 4), max(abs(dx),abs(dy)) >= 0.12 {
            fired = true
            let base: UInt16 = peak == 3 ? 401 : 406
            return abs(dx) > abs(dy) ? base + (dx < 0 ? 2 : 3) : base + (dy > 0 ? 0 : 1)
        }
        if peak == 2 {
            let a = baseline[0], b = baseline[1], c = sorted[0], d = sorted[1]
            let distance = hypot(b.x-a.x,b.y-a.y)
            guard distance >= 0.08 else { return nil }
            let ratio = hypot(d.x-c.x,d.y-c.y)/distance
            var angle = atan2(d.y-c.y,d.x-c.x)-atan2(b.y-a.y,b.x-a.x)
            while angle > .pi { angle -= 2 * .pi }; while angle < -.pi { angle += 2 * .pi }
            if ratio < 0.75 || ratio > 1.25 { fired = true; return ratio < 1 ? 410 : 411 }
            if abs(angle) > 0.4 { fired = true; return angle < 0 ? 412 : 413 }
            if time-start <= 0.65, (0.82...1.18).contains(ratio), abs(angle) < 0.28 {
                if abs(dx) >= 0.22, abs(dx) > abs(dy)*1.5 {
                    let towardCenter = dx > 0 ? max(a.x,b.x) <= 0.14 : min(a.x,b.x) >= 0.86
                    if towardCenter { fired = true; return dx > 0 ? 425 : 424 }
                }
                if abs(dy) >= 0.22, abs(dy) > abs(dx)*1.5 {
                    let towardCenter = dy > 0 ? max(a.y,b.y) <= 0.14 : min(a.y,b.y) >= 0.86
                    if towardCenter { fired = true; return dy > 0 ? 422 : 423 }
                }
            }
        }
        return nil
    }

    // Codes: top L/R, bottom L/R, left U/D, right U/D. Two fingers use the next eight.
    private mutating func alongEdge(dx: Double, dy: Double, points: [TrackpadContact], time: Double, width: Double,
                                    mapping: (UInt16) -> Bool?, isVolume: (UInt16) -> Bool, volume: (UInt16, Double) -> Void) -> UInt16? {
        let base = UInt16(426 + (peak-1)*8)
        if !adjustingVolume && edgePulses == 0 {
            // Choose a volume edge from the initial touch before generic edge
            // locking. Allow natural inward drift while the finger accelerates.
            let strip = min(width, Self.volumeEdgeLimit)
            let candidates = [
                (edge: 0, offset: dx, cross: dy, distance: { (p: TrackpadContact) in 1-p.y }),
                (edge: 2, offset: dx, cross: dy, distance: { (p: TrackpadContact) in p.y }),
                (edge: 4, offset: -dy, cross: dx, distance: { (p: TrackpadContact) in p.x }),
                (edge: 6, offset: -dy, cross: dx, distance: { (p: TrackpadContact) in 1-p.x })
            ]
            for candidate in candidates.sorted(by: { abs($0.offset) > abs($1.offset) }) {
                let offset = candidate.offset
                let code = base + UInt16(candidate.edge + (offset > 0 ? 1 : 0))
                guard abs(offset) >= 0.016, abs(offset) >= abs(candidate.cross)*1.15,
                      baseline.allSatisfy({ candidate.distance($0) <= strip }),
                      points.allSatisfy({ candidate.distance($0) <= strip + 0.045 }),
                      isVolume(code) else { continue }
                edge = candidate.edge
                adjustingVolume = true
                travelled = true
                edgeAnchor = offset
                volumeDirection = offset > 0 ? 1 : -1
                volumeExtreme = offset
                edgeLastPulse = time
                volume(code, abs(offset))
                return nil
            }
        }
        if edge == nil {
            guard max(abs(dx),abs(dy)) >= 0.022 else { return nil }
            if abs(dx) >= abs(dy)*1.5 {
                if baseline.allSatisfy({ $0.y >= 1-width }) { edge = 0 }
                else if baseline.allSatisfy({ $0.y <= width }) { edge = 2 }
            } else if abs(dy) >= abs(dx)*1.5 {
                if baseline.allSatisfy({ $0.x <= width }) { edge = 4 }
                else if baseline.allSatisfy({ $0.x >= 1-width }) { edge = 6 }
            }
            if edge == nil && max(abs(dx),abs(dy)) >= 0.075 { edgeCancelled = true }
        }
        guard let edge = edge else { return nil }
        let horizontal = edge < 4
        let offset = horizontal ? dx : -dy
        let rawStep = offset - edgeAnchor
        // Tiny reversals from touch noise should not toggle the level.
        if adjustingVolume {
            if offset * volumeDirection >= volumeExtreme * volumeDirection {
                volumeExtreme = offset
            } else if abs(offset - volumeExtreme) < 0.012 {
                return nil
            } else {
                edgeAnchor = volumeExtreme
                volumeDirection = -volumeDirection
                volumeExtreme = offset
            }
        }
        let step = adjustingVolume ? offset - edgeAnchor : rawStep
        let across = horizontal ? dy : dx
        guard adjustingVolume || abs(across) <= max(0.06,abs(offset)*0.6) else { return nil }
        let code = base + UInt16(edge + (step > 0 ? 1 : 0))
        if adjustingVolume {
            if isVolume(code) {
                if abs(step) >= 0.002, time-edgeLastPulse >= 1.0/60 {
                    edgeAnchor = offset
                    edgeLastPulse = time
                    volume(code, abs(step))
                }
            } else {
                edgeAnchor = offset
            }
            return nil
        }
        // Continuous volume never falls through to a generic edge pulse.
        if isVolume(code) { return nil }
        guard let start = start, time-start >= 0.065, abs(step) >= (ownsEdge ? 0.035 : 0.055) else { return nil }
        guard let repeats = mapping(code) else { edgeAnchor = offset; return nil }
        guard edgePulses < 24, !ownsEdge || repeats, time-edgeLastPulse >= 0.10 else { return nil }
        edgeAnchor = offset; edgeLastPulse = time; edgePulses += 1
        if !repeats { fired = true }
        return code
    }
}
