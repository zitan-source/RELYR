import AppKit

final class FakeWindows: WindowBackend {
    var areas = [CGRect(x: 0,y: 25,width: 1400,height: 875), CGRect(x: -1200,y: 25,width: 1200,height: 875)]
    var frames = ["a": CGRect(x: 100,y: 100,width: 600,height: 400)]
    var states = ["a": false, "b": true]
    var target: String? = "a"
    var minimumWidth: CGFloat = 0
    var partial = false
    var writes: [String] = []
    var fullscreen = false
    var acceptsExit = true
    var exits = 0
    func fullScreen(_ id: String) -> Bool? { frames[id] == nil ? nil : fullscreen }
    func leaveFullScreen(_ id: String) -> Bool { exits += 1; return acceptsExit }
    func resolve(_ mode: WindowTargetMode, point: CGPoint?) -> String? { target }
    func windows() -> [String] { Array(states.keys) }
    func frame(_ id: String) -> CGRect? { frames[id] }
    func setFrame(_ frame: CGRect, id: String) -> Bool {
        writes.append(id)
        frames[id] = partial ? CGRect(x: frame.minX,y: frame.minY,width: 500,height: 400) : frame
        if !partial { frames[id]?.size.width = max(frame.width,minimumWidth) }
        return true
    }
    func minimized(_ id: String) -> Bool? { states[id] }
    func minimize(_ value: Bool, id: String) -> Bool { states[id] = value; return true }
    var moves: [(String,WindowCommand)] = []
    var moveCompletion: ((String?) -> Void)?
    func moveToSpace(_ id: String, command: WindowCommand, completion: @escaping (String?) -> Void) {
        moves.append((id,command)); moveCompletion = completion
    }
    var acceptsClose = true
    func closeSurface(_ id: String) -> Bool { writes.append("surface:" + id); return acceptsClose }
    func close(_ id: String) -> Bool { writes.append("close:" + id); return true }
    func restoreNative(_ id: String) -> Bool { false }
}
final class WindowTestScheduler {
    var time: TimeInterval = 0
    var jobs: [(TimeInterval,() -> Void)] = []
    func schedule(_ delay: TimeInterval,_ work: @escaping () -> Void) { jobs.append((time+delay,work)) }
    func tick(_ count: Int) {
        for _ in 0..<count {
            time += 0.101
            let ready = jobs.filter { $0.0 <= time }; jobs.removeAll { $0.0 <= time }
            ready.forEach { $0.1() }
        }
    }
}
@main struct WindowActionTests {
    static func check(_ result: Bool, _ message: String) { precondition(result, message) }
    static func immediatePlacement() {
        let backend = FakeWindows(), scheduler = WindowTestScheduler()
        let original = backend.frames["a"]!
        let service = WindowActionService(backend:backend,now:{scheduler.time},schedule:scheduler.schedule)
        for _ in 0..<20 {
            check(service.perform(.snapLeft,mode:.active) == nil && backend.frames["a"] == CGRect(x:0,y:25,width:700,height:875),"Left is immediate")
            check(service.perform(.snapRight,mode:.active) == nil && backend.frames["a"] == CGRect(x:700,y:25,width:700,height:875),"Right is immediate")
            check(service.perform(.toggleMaximize,mode:.active) == nil && backend.frames["a"] == backend.areas[0],"Maximize is immediate")
            check(service.perform(.toggleMaximize,mode:.active) == nil && backend.frames["a"] == original,"Restore exact original")
        }
        check(scheduler.jobs.isEmpty && backend.moves.isEmpty,"Placement neither waits for shortcuts nor changes desktops")
        service.animationDuration = 0.14
        check(service.perform(.snapRight,mode:.active) == nil && service.isBusy,"Animation starts immediately")
        scheduler.tick(2)
        check(!service.isBusy && backend.frames["a"] == CGRect(x:700,y:25,width:700,height:875),"Animation finishes without shortcut timeout")
        _ = service.perform(.toggleMaximize,mode:.active); scheduler.tick(2)
        _ = service.perform(.toggleMaximize,mode:.active); scheduler.tick(2)
        check(backend.frames["a"] == original && backend.moves.isEmpty,"Animated maximize/restore keeps original and desktop")
        print("PASS: 20 left/right/maximize/restore cycles, no shortcut polling or desktop moves; fake backend")
    }
    static func fullscreenTransitions() {
        do {
            let backend = FakeWindows()
            let closer = WindowActionService(backend:backend)
            backend.fullscreen = true
            check(closer.perform(.closeSurface,mode:.active) == nil && backend.writes == ["surface:a"] && backend.exits == 0,"Close surface directly even in fullscreen")
            backend.frames.removeAll(); backend.fullscreen = false; backend.writes.removeAll()
            check(closer.perform(.closeSurface,mode:.underPointer) == nil && backend.writes == ["surface:a"],"Closing does not require resize geometry")
            backend.acceptsClose = false
            check(closer.perform(.closeSurface,mode:.active) != nil,"Unsupported close reports refusal")
        }

        for command: WindowCommand in [.toggleMaximize,.snapLeft,.snapRight] {
            let backend = FakeWindows(), scheduler = WindowTestScheduler()
            let original = backend.frames["a"]!
            let service = WindowActionService(backend:backend,now:{ scheduler.time },schedule:scheduler.schedule)
            var completions = 0
            service.onCompletion = { error in check(error == nil,"Fullscreen transition succeeds"); completions += 1 }
            backend.fullscreen = true
            check(service.perform(command,mode:.underPointer) == nil && backend.exits == 1,"Requests targeted fullscreen exit")
            scheduler.tick(8); check(backend.writes.isEmpty && completions == 0,"Never resize while fullscreen")
            backend.target = "b"; backend.frames["b"] = CGRect(x:300,y:200,width:400,height:300)
            backend.fullscreen = false
            scheduler.tick(2); check(backend.writes.isEmpty,"Wait for stable geometry")
            scheduler.tick(5)
            let expected = command == .toggleMaximize ? original : CGRect(x:command == .snapLeft ? 0 : 700,y:25,width:700,height:875)
            check(backend.frames["a"] == expected && completions == 1,"Exit then restore/snap original identity")
            check(backend.writes.allSatisfy { $0 == "a" },"Foreground change never redirects pending operation")
        }
        do {
            let backend = FakeWindows(), scheduler = WindowTestScheduler(), original = CGRect(x:100,y:100,width:600,height:400)
            let service = WindowActionService(backend:backend,now:{ scheduler.time },schedule:scheduler.schedule)
            _ = service.perform(.maximize,mode:.active)
            backend.fullscreen = true; _ = service.perform(.toggleMaximize,mode:.active)
            backend.fullscreen = false; scheduler.tick(12)
            check(backend.frames["a"] == original,"Restores RELYR pre-maximize frame after fullscreen exit")
        }
        do {
            let backend = FakeWindows(), scheduler = WindowTestScheduler()
            let service = WindowActionService(backend:backend,now:{ scheduler.time },schedule:scheduler.schedule)
            backend.fullscreen = true
            _ = service.perform(.snapLeft,mode:.active); _ = service.perform(.snapRight,mode:.active)
            check(backend.exits == 1,"Repeated request does not toggle fullscreen twice")
            backend.fullscreen = false; scheduler.tick(12)
            check(backend.frames["a"]?.minX == 700,"Latest requested side wins")
        }
        for scenario in ["timeout","closed","cancel","denied"] {
            let backend = FakeWindows(), scheduler = WindowTestScheduler()
            let service = WindowActionService(backend:backend,now:{ scheduler.time },schedule:scheduler.schedule)
            var errors = 0; service.onCompletion = { if $0 != nil { errors += 1 } }
            backend.fullscreen = true; backend.acceptsExit = scenario != "denied"
            let result = service.perform(.snapLeft,mode:.active)
            if scenario == "denied" { check(result != nil,"Denied exit reports error") }
            if scenario == "closed" { backend.frames.removeValue(forKey:"a") }
            if scenario == "cancel" { service.cancelPending(); backend.fullscreen = false }
            scheduler.tick(60)
            check(backend.writes.isEmpty,"Failure/cancellation never resizes another window")
            check(errors == ((scenario == "timeout" || scenario == "closed") ? 1 : 0),"Reports exactly one async failure")
        }
        print("PASS: fullscreen exit/restore/left/right, stable-frame wait, pinned target, latest request, refusal, timeout, closed target and cancellation; fake backend only")
    }
    static func spaceMoves() {
        let spaces = [WindowSpaces.Desktop(id:8,display:"A"),.init(id:1,display:"A"),.init(id:6,display:"A"),.init(id:9,display:"B")]
        check(WindowSpaces.destination(.spaceRight,current:[8],desktops:spaces) == 1,"Right follows display order, not Space ID")
        check(WindowSpaces.destination(.spaceLeft,current:[6],desktops:spaces) == 1,"Left follows display order")
        check(WindowSpaces.destination(.spaceRight,current:[6],desktops:spaces) == nil,"Relative move never crosses monitors")
        check(WindowSpaces.destination(.spaceLeft,current:[8],desktops:spaces) == nil,"No wrap at first desktop")
        check(WindowSpaces.destination(.space4,current:[1],desktops:spaces) == 9,"Numbered destination includes second monitor")
        check(WindowSpaces.destination(.space16,current:[1],desktops:spaces) == nil,"Missing destination rejected")
        check(WindowSpaces.destination(.space2,current:[8,1],desktops:spaces) == nil,"All-spaces window rejected")
        check(WindowSpaces.destination(.space2,current:[],desktops:spaces) == nil,"Missing window rejected")
        let decoded = WindowSpaces.desktops([["Display Identifier":"A","Spaces":[["type":0,"ManagedSpaceID":8],["type":4,"ManagedSpaceID":77],["type":0,"ManagedSpaceID":1]]]])
        check(decoded.map(\.id) == [8,1],"Fullscreen spaces excluded from numbering")
        let backend = FakeWindows()
        let scheduler = WindowTestScheduler()
        let mover = WindowActionService(backend:backend,now:{scheduler.time},schedule:scheduler.schedule)
        var errors: [String?] = []
        mover.onCompletion = {errors.append($0)}
        _ = mover.perform(.spaceRight,mode:.active)
        check(backend.moves.count == 1 && backend.moves[0].0 == "a" && backend.writes.isEmpty,"Space move never resizes or injects a key")
        check(mover.perform(.spaceLeft,mode:.active) != nil && backend.moves.count == 1,"Concurrent move rejected")
        backend.target = "b"; backend.moveCompletion?("refused")
        check(errors.count == 1 && errors[0] == "refused","Async failure propagated without retargeting")
        backend.target = "a"; _ = mover.perform(.space2,mode:.active)
        mover.cancelPending(); backend.moveCompletion?("stale")
        check(errors.count == 1,"Cancelled callbacks do not overwrite status")
        backend.fullscreen = true
        _ = mover.perform(.spaceLeft,mode:.active)
        check(backend.exits == 1 && backend.moves.count == 2,"Fullscreen exits before moving")
        backend.fullscreen = false; scheduler.tick(12)
        check(backend.moves.count == 3 && backend.moves.last?.1 == .spaceLeft,"Fullscreen move waits for stable restoration")
        print("PASS: desktop ordering, boundaries, fullscreen exclusion, multi-monitor numbering, pinned target, refusal, pending guard, cancellation and fullscreen exit")
    }
    static func main() {
        immediatePlacement()
        spaceMoves()
        fullscreenTransitions()
        let backend = FakeWindows(), original = CGRect(x: 100,y: 100,width: 600,height: 400)
        let service = WindowActionService(backend: backend)
        check(service.perform(.toggleMaximize,mode:.active) == nil, "maximize succeeds")
        check(backend.frames["a"] == backend.areas[0], "uses work area, excludes menu bar")
        check(service.perform(.maximize,mode:.active) == nil, "repeated maximize")
        check(service.perform(.toggleMaximize,mode:.active) == nil && backend.frames["a"] == original, "restores exact saved frame")
        _ = service.perform(.toggleMaximize,mode:.active)
        let manual = CGRect(x:50,y:80,width:700,height:500)
        backend.frames["a"] = manual
        _ = service.perform(.toggleMaximize,mode:.active)
        _ = service.perform(.toggleMaximize,mode:.active)
        check(backend.frames["a"] == manual, "manual resize becomes new original")
        _ = service.perform(.maximize,mode:.active)
        _ = service.perform(.monitorLeft,mode:.active)
        check(backend.frames["a"] == backend.areas[1], "maximized move to negative origin monitor")
        _ = service.perform(.toggleMaximize,mode:.active)
        check(backend.frames["a"] == WindowGeometry.relocated(manual,from:backend.areas[0],to:backend.areas[1]), "restore follows monitor")
        _ = service.perform(.snapLeft,mode:.active)
        check(backend.frames["a"]?.width == 600,"Half of left monitor")
        backend.minimumWidth = 800
        check(service.perform(.snapRight,mode:.active) != nil,"Constrained snap explains minimum size")
        check(backend.frames["a"]?.maxX == backend.areas[1].maxX && backend.frames["a"]?.width == 800,"Minimum width stays right aligned without rollback")
        _ = service.perform(.snapLeft,mode:.active)
        check(backend.frames["a"]?.minX == backend.areas[1].minX,"Constrained left snap")
        backend.minimumWidth = 0
        let count = backend.writes.count
        backend.target = nil
        check(service.perform(.close,mode:.underPointer) != nil && backend.writes.count == count, "invalid pointer target never falls back")
        backend.target = "a"
        _ = service.perform(.close,mode:.underPointer)
        check(backend.writes.last == "close:a", "close uses resolved identity")
        _ = service.perform(.toggleMinimizeAll,mode:.active)
        check(backend.states["a"] == true && backend.states["b"] == true, "minimize only visible windows")
        _ = service.perform(.toggleMinimizeAll,mode:.active)
        check(backend.states["a"] == false && backend.states["b"] == true, "restore only windows minimized by service")
        backend.partial = true
        let before = backend.writes.count
        check(service.perform(.maximize,mode:.active) != nil && backend.writes.count == before + 2, "partial resize reports failure and attempts rollback")
        _ = service.perform(.minimizeAll,mode:.active)
        _ = service.perform(.minimizeAll,mode:.active)
        check(backend.states["a"] == true, "Repeated minimize all never toggles back")
        _ = service.perform(.restoreAll,mode:.active)
        check(backend.states["a"] == false && backend.states["b"] == true, "Restore only windows minimized by RELYR")
        print("PASS: window maximize/restore, manual resize, monitor geometry, target identity, minimize ownership and rollback; fake backend only")
    }
}
