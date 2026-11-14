import AppKit
import ApplicationServices

protocol WindowBackend: AnyObject {
    var areas: [CGRect] { get }
    func resolve(_ mode: WindowTargetMode, point: CGPoint?) -> String?
    func moveToSpace(_ id: String, command: WindowCommand, completion: @escaping (String?) -> Void)
    func windows() -> [String]
    func frame(_ id: String) -> CGRect?
    func setFrame(_ frame: CGRect, id: String) -> Bool
    func minimized(_ id: String) -> Bool?
    func minimize(_ value: Bool, id: String) -> Bool
    func closeSurface(_ id: String) -> Bool
    func close(_ id: String) -> Bool
    func restoreNative(_ id: String) -> Bool
    func fullScreen(_ id: String) -> Bool?
    func leaveFullScreen(_ id: String) -> Bool
}

final class WindowActionService {
    private let backend: WindowBackend
    private struct Saved { var original: CGRect; var applied: CGRect }
    private var saved: [String: Saved] = [:]
    private var minimizedByUs = Set<String>()
    var animationDuration: TimeInterval = 0
    private struct Animation {
        let token: UUID
        let start: CGRect
        let target: CGRect
        let original: CGRect
        let command: WindowCommand
        let started: TimeInterval
    }
    private var animations: [String:Animation] = [:]
    var isBusy: Bool { !animations.isEmpty || !pending.isEmpty || !spaceRequests.isEmpty }

    var onCompletion: ((String?) -> Void)?
    private struct Pending {
        let token: UUID
        var command: WindowCommand
        let started: TimeInterval
        var lastFrame: CGRect?
        var stable = 0
    }
    private var pending: [String:Pending] = [:]
    private var spaceRequests: [String:UUID] = [:]
    private let schedule: (TimeInterval, @escaping () -> Void) -> Void
    private let now: () -> TimeInterval
    init(backend: WindowBackend = AccessibilityWindowBackend(),
         now: @escaping () -> TimeInterval = { ProcessInfo.processInfo.systemUptime },
         schedule: @escaping (TimeInterval, @escaping () -> Void) -> Void = { delay, work in DispatchQueue.main.asyncAfter(deadline:.now()+delay,execute:work) }) {
        self.backend = backend; self.now = now; self.schedule = schedule
    }
    func cancelPending() {
        for (id, animation) in animations {
            _ = backend.setFrame(animation.target,id:id)
            if animation.command == .restoreOrMinimize { saved.removeValue(forKey:id) }
            else { saved[id] = Saved(original:animation.original,applied:animation.target) }
        }
        animations.removeAll(); pending.removeAll(); spaceRequests.removeAll()
    }
    private func waitForExit(_ id: String, token: UUID) {
        schedule(0.1) { [weak self] in
            guard let self = self, var request = self.pending[id], request.token == token else { return }
            guard let fullscreen = self.backend.fullScreen(id), let frame = self.backend.frame(id), WindowGeometry.valid(frame) else {
                self.pending.removeValue(forKey:id); self.onCompletion?("フルスクリーン解除中に対象ウィンドウを取得できなくなりました。"); return
            }
            if !fullscreen, self.now() - request.started >= 0.5 {
                request.stable = request.lastFrame.map { WindowGeometry.near($0,frame) } == true ? request.stable + 1 : 0
                request.lastFrame = frame
                if request.stable >= 3 {
                    self.pending.removeValue(forKey:id)
                    if request.command == .toggleMaximize || request.command == .restoreOrMinimize {
                        // macOS has restored its pre-fullscreen frame. Only undo our own
                        // earlier maximize if that exact applied frame is still present.
                        if let saved = self.saved[id], WindowGeometry.near(frame,saved.applied) {
                            let restored = self.resize(saved.original,id:id,previous:frame)
                            if restored { self.saved.removeValue(forKey:id) }
                            self.onCompletion?(restored ? nil : "フルスクリーンは解除しましたが、元のサイズへ戻せませんでした。")
                        } else { self.saved.removeValue(forKey:id); self.onCompletion?(nil) }
                    } else {
                        let error = self.performResolved(request.command,id:id)
                        self.onCompletion?(error)
                    }
                    return
                }
            } else { request.stable = 0; request.lastFrame = nil }
            guard self.now() - request.started < 5 else {
                self.pending.removeValue(forKey:id); self.onCompletion?("フルスクリーンの解除が完了しませんでした。アプリが解除を受け付ける状態で再度実行してください。"); return
            }
            self.pending[id] = request; self.waitForExit(id,token:token)
        }
    }

    @discardableResult func perform(_ command: WindowCommand, mode: WindowTargetMode, point: CGPoint? = nil) -> String? {
        if command == .toggleMinimizeAll || command == .minimizeAll || command == .restoreAll {
            cancelPending()
            if command == .restoreAll || (command == .toggleMinimizeAll && !minimizedByUs.isEmpty) {
                var failed = Set<String>()
                for id in minimizedByUs {
                    guard backend.minimized(id) != nil else { continue }
                    if !backend.minimize(false, id: id) { failed.insert(id) }
                }
                minimizedByUs = failed
                return failed.isEmpty ? nil : "一部のウィンドウを復元できませんでした。"
            }
            for id in backend.windows() where backend.minimized(id) == false {
                if backend.minimize(true, id: id) { minimizedByUs.insert(id) }
            }
            return minimizedByUs.isEmpty ? "最小化できるウィンドウがありません。" : nil
        }
        guard let id = backend.resolve(mode, point: point) else { return "操作できる対象ウィンドウがありません。" }
        if let previous = animations.removeValue(forKey:id) {
            _ = backend.setFrame(previous.target,id:id)
            if previous.command == .restoreOrMinimize { saved.removeValue(forKey:id) }
            else { saved[id] = Saved(original:previous.original,applied:previous.target) }
        }

        if command == .closeSurface {
            pending.removeValue(forKey:id)
            if backend.closeSurface(id) { return nil }
            if backend.fullScreen(id) != true { return "この画面は閉じる操作を公開していないか、操作を受け付けていません。" }
        }
        let supportsExit = command.isSpaceMove || [.toggleMaximize,.restoreOrMinimize,.maximize,.snapLeft,.snapRight,.closeSurface].contains(command)
        if var existing = pending[id], supportsExit {
            existing.command = command; pending[id] = existing; return nil
        }
        pending.removeValue(forKey:id)
        if backend.fullScreen(id) == true {
            guard supportsExit else { return "フルスクリーンを解除してから実行してください。" }
            guard backend.leaveFullScreen(id) else { return "このアプリのフルスクリーンを解除できませんでした。" }
            let request = Pending(token:UUID(),command:command,started:now())
            pending[id] = request; waitForExit(id,token:request.token); return nil
        }
        return performResolved(command,id:id)
    }
    private func performResolved(_ command: WindowCommand, id: String, animate: Bool = true) -> String? {
        if command.isSpaceMove {
            guard spaceRequests[id] == nil else { return "このウィンドウはデスクトップへ移動中です。" }
            let token = UUID(); spaceRequests[id] = token
            backend.moveToSpace(id,command:command) { [weak self] error in
                guard let self = self, self.spaceRequests[id] == token else { return }
                self.spaceRequests.removeValue(forKey:id); self.onCompletion?(error)
            }
            return nil
        }
        if command == .closeSurface {
            return backend.closeSurface(id) ? nil : "対象画面が閉じる操作を受け付けませんでした。"
        }
        guard backend.fullScreen(id) != true, let frame = backend.frame(id), WindowGeometry.valid(frame) else {
            return "操作できる対象ウィンドウがありません。"
        }
        if command == .close { return backend.close(id) ? nil : "対象ウィンドウを閉じられませんでした。" }
        if command == .minimize { return backend.minimize(true, id: id) ? nil : "対象ウィンドウを最小化できませんでした。" }
        let areas = backend.areas.filter(WindowGeometry.valid)
        guard let screen = WindowGeometry.screen(for: frame, in: areas) else { return "画面の作業領域を取得できません。" }
        let area = areas[screen]
        // A manual resize invalidates the prior toggle state; the next maximize saves the new size.
        if let entry = saved[id], !WindowGeometry.near(frame,entry.applied) { saved.removeValue(forKey: id) }
        if animate && animationDuration > 0 && [.maximize,.toggleMaximize,.restoreOrMinimize,.snapLeft,.snapRight].contains(command) {
            var target = area
            var animationCommand = command == .toggleMaximize ? WindowCommand.maximize : command
            if (command == .toggleMaximize || command == .restoreOrMinimize) && WindowGeometry.near(frame,area) {
                if let entry = saved[id] { target = entry.original; animationCommand = .restoreOrMinimize }
                else { return performResolved(command,id:id,animate:false) }
            } else if command == .restoreOrMinimize { return performResolved(command,id:id,animate:false) }
            if command == .snapLeft { target.size.width /= 2 }
            if command == .snapRight { target.origin.x += target.width/2; target.size.width /= 2 }
            if WindowGeometry.near(frame,target) { return nil }
            let item = Animation(token:UUID(),start:frame,target:target,original:saved[id]?.original ?? frame,command:animationCommand,started:now())
            animations[id] = item
            animateFrame(id,token:item.token)
            return nil
        }
        if (command == .toggleMaximize || command == .restoreOrMinimize), WindowGeometry.near(frame,area) {
            if let entry = saved[id] {
                guard resize(entry.original, id: id, previous: frame) else { return "元の位置・サイズへの復元に失敗しました。" }
                saved.removeValue(forKey: id); return nil
            }
            return backend.restoreNative(id) ? nil : "この最大化前のサイズは取得できません。アプリの復元操作を使ってください。"
        }
        if command == .restoreOrMinimize { return backend.minimize(true,id:id) ? nil : "最小化できませんでした。" }
        var target = area
        switch command {
        case .snapLeft: target.size.width /= 2
        case .snapRight: target.origin.x += target.width/2; target.size.width /= 2
        case .monitorLeft, .monitorRight, .monitorUp, .monitorDown:
            guard let next = WindowGeometry.adjacent(screen,command:command,areas:areas) else { return "指定方向にモニターがありません。" }
            let maximized = WindowGeometry.near(frame,area)
            target = maximized ? areas[next] : WindowGeometry.relocated(frame,from:area,to:areas[next])
            guard resize(target,id:id,previous:frame) else { return "モニター移動に失敗しました。" }
            if let entry = saved[id] { saved[id] = Saved(original:WindowGeometry.relocated(entry.original,from:area,to:areas[next]),applied:target) }
            return nil
        default: break
        }
        if WindowGeometry.near(frame,target) { return nil } // repeated maximize never destroys the original rectangle
        if command == .snapLeft || command == .snapRight {
            guard backend.setFrame(target,id:id), let actual = backend.frame(id), WindowGeometry.valid(actual) else {
                _ = backend.setFrame(frame,id:id); return "アプリが位置・サイズの変更を受け付けませんでした。"
            }
            // Respect the application's minimum width instead of undoing a valid snap.
            guard actual.width >= target.width - 2, actual.width <= area.width + 2,
                  abs(actual.height - target.height) <= 2 else {
                _ = backend.setFrame(frame,id:id); return "このアプリのサイズ制約により左右に配置できませんでした。"
            }
            let aligned = CGRect(x:command == .snapRight ? area.maxX - actual.width : area.minX,
                                 y:area.minY,width:actual.width,height:actual.height)
            guard resize(aligned,id:id,previous:frame) else { return "ウィンドウを画面の端へ配置できませんでした。" }
            saved[id] = Saved(original:saved[id]?.original ?? frame,applied:aligned)
            return actual.width > target.width + 2 ? "アプリの最小幅に合わせて画面の端へ配置しました（半分の幅には縮小できません）。" : nil
        }
        guard resize(target,id:id,previous:frame) else { return "アプリが位置・サイズの変更を受け付けませんでした。" }
        let original = saved[id]?.original ?? frame
        saved[id] = Saved(original:original,applied:target)
        if saved.count > 128 { saved = [id:saved[id]!] }
        return nil
    }
    private func animateFrame(_ id: String, token: UUID) {
        guard let item = animations[id], item.token == token else { return }
        let t = min(1,max(0,(now()-item.started)/max(0.001,animationDuration)))
        if t >= 1 {
            animations.removeValue(forKey:id)
            // Reuse the constraint-aware placement path and preserve the original rectangle.
            let applied = backend.frame(id) ?? item.start
            saved[id] = Saved(original:item.original,applied:applied)
            let error: String?
            if item.command == .restoreOrMinimize {
                error = resize(item.target,id:id,previous:applied) ? nil : "元のサイズへ戻せませんでした。"
                if error == nil { saved.removeValue(forKey:id) }
            } else { error = performResolved(item.command,id:id,animate:false) }
            onCompletion?(error); return
        }
        let eased = 1-pow(1-t,3)
        let rect = CGRect(x:item.start.minX+(item.target.minX-item.start.minX)*eased,
                          y:item.start.minY+(item.target.minY-item.start.minY)*eased,
                          width:item.start.width+(item.target.width-item.start.width)*eased,
                          height:item.start.height+(item.target.height-item.start.height)*eased)
        if !backend.setFrame(rect,id:id) {
            animations.removeValue(forKey:id); _ = backend.setFrame(item.start,id:id)
            onCompletion?("アプリがサイズ変更を受け付けませんでした。"); return
        }
        schedule(1.0/60) { [weak self] in self?.animateFrame(id,token:token) }
    }
    private func resize(_ target: CGRect, id: String, previous: CGRect) -> Bool {
        guard backend.setFrame(target,id:id), let actual = backend.frame(id), WindowGeometry.near(actual,target) else {
            _ = backend.setFrame(previous,id:id) // partial AX resize must not silently leave a moved window
            return false
        }
        return true
    }


}

final class AccessibilityWindowBackend: WindowBackend {
    private var elements: [String: AXUIElement] = [:]
    private var serial = 0
    var areas: [CGRect] {
        guard let primary = NSScreen.screens.first else { return [] }
        return NSScreen.screens.map { screen in
            let rect = screen.visibleFrame
            return CGRect(x:rect.minX,y:primary.frame.maxY-rect.maxY,width:rect.width,height:rect.height)
        }
    }
    private func attribute(_ element: AXUIElement, _ name: String) -> CFTypeRef? {
        var value: CFTypeRef?
        guard AXUIElementCopyAttributeValue(element,name as CFString,&value) == .success else { return nil }; return value
    }
    private func element(_ value: CFTypeRef?) -> AXUIElement? {
        guard let value = value, CFGetTypeID(value) == AXUIElementGetTypeID() else { return nil }; return (value as! AXUIElement)
    }
    private func register(_ element: AXUIElement) -> String? {
        var pid: pid_t = 0
        guard AXUIElementGetPid(element,&pid) == .success, let app = NSRunningApplication(processIdentifier:pid),
              !["com.apple.dock","com.apple.systemuiserver","com.apple.controlcenter"].contains(app.bundleIdentifier?.lowercased() ?? ""),
              attribute(element,kAXRoleAttribute) as? String == kAXWindowRole,
              let subrole = attribute(element,kAXSubroleAttribute) as? String,
              [kAXStandardWindowSubrole,kAXDialogSubrole].contains(subrole) else { return nil }
        if let existing = elements.first(where: { CFEqual($0.value,element) }) { return existing.key }
        if elements.count >= 256 { elements = elements.filter { attribute($0.value,kAXRoleAttribute) != nil } }
        guard elements.count < 512 else { return nil }
        serial += 1; let id = "\(pid):\(serial)"; elements[id] = element; return id
    }
    func resolve(_ mode: WindowTargetMode, point: CGPoint?) -> String? {
        guard AXIsProcessTrusted() else { return nil }
        if mode == .active {
            guard let app = NSWorkspace.shared.frontmostApplication else { return nil }
            let root = AXUIElementCreateApplication(app.processIdentifier)
            guard let window = element(attribute(root,kAXFocusedWindowAttribute)) ?? element(attribute(root,kAXMainWindowAttribute)) else { return nil }
            return register(window)
        }
        guard let point = point ?? CGEvent(source:nil)?.location else { return nil }
        var hit: AXUIElement?
        guard AXUIElementCopyElementAtPosition(AXUIElementCreateSystemWide(),Float(point.x),Float(point.y),&hit) == .success else { return nil }
        for _ in 0..<12 {
            guard let current = hit else { return nil }
            if attribute(current,kAXRoleAttribute) as? String == kAXWindowRole { return register(current) }
            if let window = element(attribute(current,kAXWindowAttribute)) { return register(window) }
            hit = element(attribute(current,kAXParentAttribute))
        }
        return nil // never fall back to the foreground app for an invalid pointer target
    }
    func moveToSpace(_ id: String, command: WindowCommand, completion: @escaping (String?) -> Void) {
        guard let window = elements[id] else { completion("対象ウィンドウがなくなりました。"); return }
        WindowSpaces.move(window,command:command,completion:completion)
    }
    func windows() -> [String] {
        guard AXIsProcessTrusted() else { return [] }
        return NSWorkspace.shared.runningApplications.filter { $0.activationPolicy == .regular }.flatMap { app -> [String] in
            let root = AXUIElementCreateApplication(app.processIdentifier)
            guard let windows = attribute(root,kAXWindowsAttribute) as? [AXUIElement] else { return [] }
            return windows.filter { attribute($0,"AXFullScreen") as? Bool != true }.compactMap(register)
        }
    }
    func frame(_ id: String) -> CGRect? {
        guard let window = elements[id], let position = attribute(window,kAXPositionAttribute), let size = attribute(window,kAXSizeAttribute),
              CFGetTypeID(position) == AXValueGetTypeID(), CFGetTypeID(size) == AXValueGetTypeID() else { return nil }
        var point = CGPoint.zero, dimensions = CGSize.zero
        guard AXValueGetValue(position as! AXValue,.cgPoint,&point), AXValueGetValue(size as! AXValue,.cgSize,&dimensions) else { return nil }
        return CGRect(origin:point,size:dimensions)
    }
    func setFrame(_ frame: CGRect, id: String) -> Bool {
        guard WindowGeometry.valid(frame), let window = elements[id] else { return false }
        var positionSettable = DarwinBoolean(false), sizeSettable = DarwinBoolean(false)
        guard AXUIElementIsAttributeSettable(window,kAXPositionAttribute as CFString,&positionSettable) == .success, positionSettable.boolValue,
              AXUIElementIsAttributeSettable(window,kAXSizeAttribute as CFString,&sizeSettable) == .success, sizeSettable.boolValue else { return false }
        var origin = frame.origin, size = frame.size
        guard let p = AXValueCreate(.cgPoint,&origin), let s = AXValueCreate(.cgSize,&size) else { return false }
        let moved = AXUIElementSetAttributeValue(window,kAXPositionAttribute as CFString,p)
        let resized = AXUIElementSetAttributeValue(window,kAXSizeAttribute as CFString,s)
        // Some applications constrain position against their previous size. Apply the final origin again.
        let positioned = AXUIElementSetAttributeValue(window,kAXPositionAttribute as CFString,p)
        return moved == .success && resized == .success && positioned == .success
    }
    func minimized(_ id: String) -> Bool? { elements[id].flatMap { attribute($0,kAXMinimizedAttribute) as? Bool } }
    func minimize(_ value: Bool, id: String) -> Bool {
        guard let window = elements[id] else { return false }
        return AXUIElementSetAttributeValue(window,kAXMinimizedAttribute as CFString,value ? kCFBooleanTrue : kCFBooleanFalse) == .success
    }
    func closeSurface(_ id: String) -> Bool {
        guard let window = elements[id] else { return false }
        // Press the resolved surface's close control, including Finder. Do not
        // terminate the process or redirect a failed close to a different window.
        if let button = element(attribute(window,kAXCloseButtonAttribute)),
           attribute(button,kAXEnabledAttribute) as? Bool != false,
           AXUIElementPerformAction(button,kAXPressAction as CFString) == .success { return true }
        // Sheets/dialogs may expose Cancel instead of a red close button.
        var names: CFArray?
        if AXUIElementCopyActionNames(window,&names) == .success,
           let actions = names as? [String], actions.contains(kAXCancelAction) {
            return AXUIElementPerformAction(window,kAXCancelAction as CFString) == .success
        }
        return false
    }
    func close(_ id: String) -> Bool {
        guard let window = elements[id], let button = element(attribute(window,kAXCloseButtonAttribute)) else { return false }
        var pid: pid_t = 0
        guard AXUIElementGetPid(window,&pid) == .success, let app = NSRunningApplication(processIdentifier:pid), app.activate(options:[]) else { return false }
        return AXUIElementPerformAction(button,kAXPressAction as CFString) == .success
    }
    func fullScreen(_ id: String) -> Bool? {
        guard let window = elements[id], attribute(window,kAXRoleAttribute) != nil else { return nil }
        return attribute(window,"AXFullScreen") as? Bool ?? false
    }
    func leaveFullScreen(_ id: String) -> Bool {
        guard let window = elements[id], fullScreen(id) == true else { return false }
        var settable = DarwinBoolean(false)
        guard AXUIElementIsAttributeSettable(window,"AXFullScreen" as CFString,&settable) == .success, settable.boolValue else { return false }
        return AXUIElementSetAttributeValue(window,"AXFullScreen" as CFString,kCFBooleanFalse) == .success
    }
    func restoreNative(_ id: String) -> Bool {
        // Pressing the green AX zoom button can ENTER fullscreen on modern apps.
        // Without a saved frame, never guess a restoration by pressing that button.
        false
    }
}
