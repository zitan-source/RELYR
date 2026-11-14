import AppKit
import ObjectiveC
import Darwin

/// Private OS capability, resolved at runtime. Never changes SIP, shortcuts or application assignments.
/// The bridge is present on recent macOS; unavailable versions fail explicitly.
enum WindowSpaces {
    struct Desktop: Equatable { let id: UInt64; let display: String }
    // The app supplies its existing HID shortcut route. A bare bridged current-space
    // write leaves windows from both desktops composited together on macOS 27.
    static var nativeSpaceShortcut: ((Bool) -> Bool)?
    static func desktops(_ displays: [[String:Any]]) -> [Desktop] {
        displays.flatMap { display -> [Desktop] in
            guard let name = display["Display Identifier"] as? String,
                  let spaces = display["Spaces"] as? [[String:Any]] else { return [] }
            return spaces.compactMap { space in
                guard (space["type"] as? NSNumber)?.intValue == 0,
                      let id = (space["ManagedSpaceID"] as? NSNumber)?.uint64Value, id != 0 else { return nil }
                return Desktop(id:id,display:name)
            }
        }
    }
    static func destination(_ command: WindowCommand, current: [UInt64], desktops: [Desktop]) -> UInt64? {
        // All-desktop windows have ambiguous ownership; never silently move one.
        guard current.count == 1, let source = desktops.first(where: { $0.id == current[0] }) else { return nil }
        if let number = command.spaceNumber {
            return desktops.indices.contains(number-1) ? desktops[number-1].id : nil
        }
        let local = desktops.filter { $0.display == source.display }
        guard let index = local.firstIndex(of:source) else { return nil }
        let next = index + (command == .spaceLeft ? -1 : 1)
        return local.indices.contains(next) ? local[next].id : nil
    }
    private static let library = dlopen("/System/Library/PrivateFrameworks/SkyLight.framework/SkyLight",RTLD_LAZY)
    private static func symbol<T>(_ name: String, as type: T.Type) -> T? {
        guard let library = library, let address = dlsym(library,name) else { return nil }
        return unsafeBitCast(address,to:type)
    }
    private typealias Connection = @convention(c) () -> Int32
    private typealias Displays = @convention(c) (Int32) -> Unmanaged<CFArray>?
    private typealias Membership = @convention(c) (Int32,Int32,CFArray) -> Unmanaged<CFArray>?
    private typealias WindowID = @convention(c) (AXUIElement,UnsafeMutablePointer<UInt32>) -> Int32
    private static var connection: Int32? { symbol("SLSMainConnectionID",as:Connection.self)?() }
    static func memberships(_ window: UInt32) -> [UInt64]? {
        guard let connection = connection, let copy = symbol("SLSCopySpacesForWindows",as:Membership.self),
              let values = copy(connection,7,[NSNumber(value:window)] as CFArray)?.takeRetainedValue() as? [NSNumber] else { return nil }
        return values.map(\.uint64Value)
    }
    static func visibleSpace(_ target: UInt64) -> Bool { currentSpaces()?.values.contains(target) == true }
    private static func currentSpaces() -> [String:UInt64]? {
        guard let connection = connection, let copy = symbol("SLSCopyManagedDisplaySpaces",as:Displays.self),
              let values = copy(connection)?.takeRetainedValue() as? [[String:Any]] else { return nil }
        var result: [String:UInt64] = [:]
        for display in values {
            if let name = display["Display Identifier"] as? String,
               let id = ((display["Current Space"] as? [String:Any])?["ManagedSpaceID"] as? NSNumber)?.uint64Value {
                result[name] = id
            }
        }
        return result
    }
    static func availableDesktops() -> [Desktop]? {
        guard let connection = connection, let copy = symbol("SLSCopyManagedDisplaySpaces",as:Displays.self),
              let values = copy(connection)?.takeRetainedValue() as? [[String:Any]] else { return nil }
        return desktops(values)
    }
    private static func windowNumber(_ element: AXUIElement) -> UInt32? {
        guard let handle = dlopen(nil,RTLD_LAZY), let address = dlsym(handle,"_AXUIElementGetWindow") else { return nil }
        defer { dlclose(handle) }
        var number: UInt32 = 0
        return unsafeBitCast(address,to:WindowID.self)(element,&number) == 0 && number != 0 ? number : nil
    }
    /// Bring an already-open Chrome profile window to its own Desktop without launching another.
    static func focusExisting(_ element: AXUIElement, completion: @escaping (String?) -> Void) {
        var pid: pid_t = 0
        guard AXUIElementGetPid(element,&pid) == .success,
              let app = NSRunningApplication(processIdentifier:pid),
              let number = windowNumber(element),
              let space = memberships(number)?.first else {
            completion("既存のChromeウィンドウの場所を確認できませんでした。"); return
        }
        follow(space) { error in
            // Chrome may live next to a full-screen Space, which the desktop
            // list intentionally excludes. Activating its exact AX window is a
            // safe fallback that never launches a second profile window.
            _ = AXUIElementSetAttributeValue(element,kAXMinimizedAttribute as CFString,kCFBooleanFalse)
            _ = AXUIElementSetAttributeValue(rootForApp(pid),kAXFocusedWindowAttribute as CFString,element)
            _ = AXUIElementSetAttributeValue(element,kAXMainAttribute as CFString,kCFBooleanTrue)
            let raised = AXUIElementPerformAction(element,kAXRaiseAction as CFString) == .success
            _ = app.activate(options:[])
            DispatchQueue.main.asyncAfter(deadline:.now()+0.2) {
                completion(raised && visibleSpace(space) ? nil : (error ?? "Chromeの既存ウィンドウを前面にできませんでした。"))
            }
        }
    }
    static func move(_ element: AXUIElement, command: WindowCommand, completion: @escaping (String?) -> Void) {
        // Resolve the exact AX target, not a title/position match or another foreground window.
        guard let window = windowNumber(element) else {
            completion("移動するウィンドウを取得できません。"); return
        }
        move(window,command:command) { error in
            guard error == nil else { completion(error); return }
            var pid: pid_t = 0
            guard AXUIElementGetPid(element,&pid) == .success,
                  let app = NSRunningApplication(processIdentifier:pid) else {
                completion("デスクトップは切り替えましたが、対象ウィンドウがなくなりました。"); return
            }
            _ = AXUIElementSetAttributeValue(element,kAXMainAttribute as CFString,kCFBooleanTrue)
            let raised = AXUIElementPerformAction(element,kAXRaiseAction as CFString) == .success
            _ = app.activate(options:[])
            DispatchQueue.main.asyncAfter(deadline:.now()+0.2) {
                guard let destination = memberships(window)?.first, visibleSpace(destination) else {
                    completion("ウィンドウは移動しましたが、前面表示が安定しませんでした。元のデスクトップに戻った場合は通常のControl＋矢印で切り替えてください。"); return
                }
                _ = AXUIElementSetAttributeValue(rootForApp(pid),kAXFocusedWindowAttribute as CFString,element)
                let focused = AXUIElementPerformAction(element,kAXRaiseAction as CFString) == .success
                completion(raised && focused ? nil : "移動先を表示しましたが、対象ウィンドウを前面にできませんでした。")
            }
        }
    }
    private static func rootForApp(_ pid: pid_t) -> AXUIElement { AXUIElementCreateApplication(pid) }
    static func move(_ window: UInt32, command: WindowCommand, completion: @escaping (String?) -> Void) {
        guard let current = memberships(window), let desktops = availableDesktops() else {
            completion("macOSからデスクトップ情報を取得できません。"); return
        }
        guard let target = destination(command,current:current,desktops:desktops) else {
            completion("移動先の通常デスクトップがありません。全デスクトップに固定したウィンドウは移動できません。"); return
        }
        guard let source = desktops.first(where: { $0.id == current.first }),
              let destination = desktops.first(where: { $0.id == target }),
              source.display == destination.display else {
            completion("異なるモニターのデスクトップへは安全に移動できません。"); return
        }
        if current == [target] { follow(target,completion:completion); return }
        guard submit(window,target:target) else {
            completion("このmacOSはウィンドウのデスクトップ移動に対応していません。"); return
        }
        verify(window,target:target,remaining:25,completion:completion)
    }
    private static func verify(_ window: UInt32, target: UInt64, remaining: Int, completion: @escaping (String?) -> Void) {
        DispatchQueue.main.asyncAfter(deadline:.now()+0.1) {
            if memberships(window) == [target] { follow(target,completion:completion); return }
            guard remaining > 0 else {
                completion("ウィンドウの移動を確認できませんでした。アプリのデスクトップ固定やmacOSの制限を確認してください。"); return
            }
            verify(window,target:target,remaining:remaining-1,completion:completion)
        }
    }
    private static func follow(_ target: UInt64, completion: @escaping (String?) -> Void) {
        guard let desktops = availableDesktops(),
              let destination = desktops.first(where: { $0.id == target }),
              let current = currentSpaces()?[destination.display] else {
            completion("デスクトップの現在位置を取得できません。"); return
        }
        if current == target { completion(nil); return }
        let local = desktops.filter { $0.display == destination.display }
        guard let from = local.firstIndex(where: { $0.id == current }),
              let to = local.firstIndex(of:destination) else {
            completion("切り替えるデスクトップを確認できません。"); return
        }
        let right = to > from, next = local[from + (right ? 1 : -1)].id
        guard nativeSpaceShortcut?(right) == true else {
            completion("macOSのデスクトップ切り替えキーを送信できません。"); return
        }
        verifyNativeStep(next,target:target,remaining:30,completion:completion)
    }
    private static func verifyNativeStep(_ next: UInt64, target: UInt64, remaining: Int, completion: @escaping (String?) -> Void) {
        DispatchQueue.main.asyncAfter(deadline:.now()+0.1) {
            if visibleSpace(next) {
                // Let Dock finish the native slide before another shortcut or focus change.
                DispatchQueue.main.asyncAfter(deadline:.now()+0.25) { follow(target,completion:completion) }
                return
            }
            guard remaining > 0 else {
                completion("デスクトップ切り替えを確認できませんでした。macOSのControl＋矢印ショートカットを確認してください。"); return
            }
            verifyNativeStep(next,target:target,remaining:remaining-1,completion:completion)
        }
    }
    private static func submit(_ window: UInt32, target: UInt64) -> Bool {
        submitOperation(className:"SLSBridgedMoveWindowsToManagedSpaceOperation",
                        initializer:"initWithWindows:spaceID:",argument:[NSNumber(value:window)] as NSArray,target:target)
    }
    private static func submitOperation(className: String, initializer: String, argument: AnyObject, target: UInt64) -> Bool {
        _ = library
        guard let cls = NSClassFromString(className) else { return false }
        let alloc = NSSelectorFromString("alloc"), initialize = NSSelectorFromString(initializer), perform = NSSelectorFromString("performWithWMBridgeDelegate")
        guard let initMethod = class_getInstanceMethod(cls,initialize), let performMethod = class_getInstanceMethod(cls,perform),
              let initEncoding = method_getTypeEncoding(initMethod), String(cString:initEncoding) == "@32@0:8@16Q24",
              let performEncoding = method_getTypeEncoding(performMethod), String(cString:performEncoding) == "v16@0:8",
              let allocIMP = class_getMethodImplementation(object_getClass(cls),alloc) else { return false }
        typealias Allocate = @convention(c) (AnyClass,Selector) -> Unmanaged<AnyObject>
        typealias Initialize = @convention(c) (AnyObject,Selector,AnyObject,UInt64) -> Unmanaged<AnyObject>?
        typealias Perform = @convention(c) (AnyObject,Selector) -> Void
        let object = unsafeBitCast(allocIMP,to:Allocate.self)(cls,alloc).takeUnretainedValue()
        guard let operation = unsafeBitCast(method_getImplementation(initMethod),to:Initialize.self)(object,initialize,argument,target)?.takeRetainedValue() else { return false }
        unsafeBitCast(method_getImplementation(performMethod),to:Perform.self)(operation,perform)
        return true
    }
}
