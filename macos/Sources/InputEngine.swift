import AppKit
import ApplicationServices
import Carbon

final class InputEngine {
    let machine = LayerMachine()
    private var tap: CFMachPort?
    private var source: CFRunLoopSource?
    private var pendingSpace: CGEvent?
    private var timer: Timer?
    private var generation = 0
    private let marker: Int64 = 0x52454C59524D
    private(set) var enabled = false
    var onStatus: ((String) -> Void)?

    init() {
        let center = NSWorkspace.shared.notificationCenter
        center.addObserver(self, selector: #selector(sessionEnded), name: NSWorkspace.willSleepNotification, object: nil)
        center.addObserver(self, selector: #selector(sessionEnded), name: NSWorkspace.sessionDidResignActiveNotification, object: nil)
        timer = Timer.scheduledTimer(withTimeInterval: 0.5, repeats: true) { [weak self] _ in
            guard let self = self, self.enabled else { return }
            if !AXIsProcessTrusted() || IsSecureEventInputEnabled() {
                self.stop(message: "停止中：権限またはセキュア入力の状態が変わりました。再度有効にしてください。")
            }
        }
    }

    func start() {
        guard !enabled else { return }
        guard AXIsProcessTrusted() else {
            onStatus?("アクセシビリティの許可後、再度「有効にする」を押してください。")
            return
        }
        guard !IsSecureEventInputEnabled() else {
            onStatus?("セキュア入力が有効です。パスワード入力などを終了してから有効にしてください。")
            return
        }
        let types: [CGEventType] = [.keyDown, .keyUp, .flagsChanged, .leftMouseDown, .rightMouseDown, .otherMouseDown, .scrollWheel]
        let mask = types.reduce(CGEventMask(0)) { $0 | (CGEventMask(1) << $1.rawValue) }
        let context = Unmanaged.passUnretained(self).toOpaque()
        guard let port = CGEvent.tapCreate(tap: .cgSessionEventTap, place: .headInsertEventTap,
            options: .defaultTap, eventsOfInterest: mask, callback: { proxy, type, event, context in
                guard let context = context else { return Unmanaged.passUnretained(event) }
                let engine = Unmanaged<InputEngine>.fromOpaque(context).takeUnretainedValue()
                return engine.handle(proxy: proxy, type: type, event: event)
            }, userInfo: context) else {
            onStatus?("入力監視を開始できません。アクセシビリティの許可とアプリの再起動を確認してください。")
            return
        }
        guard let runLoopSource = CFMachPortCreateRunLoopSource(kCFAllocatorDefault, port, 0) else {
            CFMachPortInvalidate(port)
            onStatus?("入力監視の初期化に失敗しました。")
            return
        }
        tap = port
        source = runLoopSource
        CFRunLoopAddSource(CFRunLoopGetMain(), runLoopSource, .commonModes)
        CGEvent.tapEnable(tap: port, enable: true)
        enabled = true
        onStatus?("有効：緊急停止は ⌃⌥⌘ Escape")
    }

    func stop(message: String = "停止中：通常のキー入力が使えます。") {
        enabled = false
        generation += 1
        if let tap = tap { CGEvent.tapEnable(tap: tap, enable: false) }
        // Release output keys before removing the tap. Our marker bypasses interception.
        for code in machine.reset() {
            if let event = CGEvent(keyboardEventSource: nil, virtualKey: code, keyDown: false) {
                event.flags = CGEventSource.flagsState(.combinedSessionState)
                event.setIntegerValueField(.eventSourceUserData, value: marker)
                event.post(tap: .cgSessionEventTap)
            }
        }
        pendingSpace = nil
        if let source = source { CFRunLoopRemoveSource(CFRunLoopGetMain(), source, .commonModes) }
        if let tap = tap { CFMachPortInvalidate(tap) }
        source = nil
        tap = nil
        onStatus?(message)
    }

    @objc private func sessionEnded() { stop(message: "停止中：スリープまたはセッション切り替え。再度有効にしてください。") }

    private func handle(proxy: CGEventTapProxy, type: CGEventType, event: CGEvent) -> Unmanaged<CGEvent>? {
        if type == .tapDisabledByTimeout || type == .tapDisabledByUserInput {
            // Do not automatically resume interception with stale physical key state.
            enabled = false
            DispatchQueue.main.async { [weak self] in self?.stop(message: "停止中：入力監視が中断されました。再度有効にしてください。") }
            return Unmanaged.passUnretained(event)
        }
        guard enabled, event.getIntegerValueField(.eventSourceUserData) != marker else { return Unmanaged.passUnretained(event) }
        let code = UInt16(clamping: event.getIntegerValueField(.keyboardEventKeycode))
        let modifiers = Modifiers(event.flags)
        if type == .keyDown, code == 53, modifiers.isSuperset(of: [.control, .option, .command]) {
            enabled = false
            DispatchQueue.main.async { [weak self] in self?.stop(message: "緊急停止しました。") }
            return nil
        }
        let decision: InputDecision
        if type == .keyDown || type == .keyUp {
            if code == 49, type == .keyDown, event.getIntegerValueField(.keyboardEventAutorepeat) == 0 {
                pendingSpace = event.copy()
            }
            decision = machine.key(code, down: type == .keyDown,
                repeating: event.getIntegerValueField(.keyboardEventAutorepeat) != 0, modifiers: modifiers)
        } else {
            decision = machine.flushPendingSpace()
        }
        switch decision {
        case .pass:
            if code == 49, type == .keyUp { pendingSpace = nil }
            return Unmanaged.passUnretained(event)
        case .consume:
            if code == 49, type == .keyUp { pendingSpace = nil }
            return nil
        case .spaceDownThenPass:
            pendingSpace?.tapPostEvent(proxy)
            pendingSpace = nil
            return Unmanaged.passUnretained(event)
        case .spaceTap:
            pendingSpace?.tapPostEvent(proxy)
            pendingSpace = nil
            return Unmanaged.passUnretained(event) // physical Space Up follows the restored Down
        case .remap(let output, let addedModifiers):
            guard let replacement = CGEvent(keyboardEventSource: nil, virtualKey: output, keyDown: type == .keyDown) else {
                enabled = false
                DispatchQueue.main.async { [weak self] in self?.stop(message: "停止中：入力イベントを生成できませんでした。") }
                return Unmanaged.passUnretained(event)
            }
            replacement.flags = event.flags.union(addedModifiers.flags)
            replacement.timestamp = event.timestamp
            replacement.setIntegerValueField(.keyboardEventAutorepeat, value: event.getIntegerValueField(.keyboardEventAutorepeat))
            replacement.setIntegerValueField(.eventSourceUserData, value: marker)
            replacement.tapPostEvent(proxy)
            return nil
        case .launch(let path):
            let ticket = generation
            DispatchQueue.main.async { [weak self] in
                guard let self = self, self.enabled, self.generation == ticket else { return }
                NSWorkspace.shared.openApplication(at: URL(fileURLWithPath: path), configuration: .init()) { _, error in
                    if let error = error {
                        DispatchQueue.main.async { self.onStatus?("アプリを起動できません：\(error.localizedDescription)") }
                    }
                }
            }
            return nil
        }
    }
}

extension Modifiers {
    init(_ flags: CGEventFlags) {
        var value: Modifiers = []
        if flags.contains(.maskCommand) { value.insert(.command) }
        if flags.contains(.maskControl) { value.insert(.control) }
        if flags.contains(.maskAlternate) { value.insert(.option) }
        if flags.contains(.maskShift) { value.insert(.shift) }
        self = value
    }
    var flags: CGEventFlags {
        var value: CGEventFlags = []
        if contains(.command) { value.insert(.maskCommand) }
        if contains(.control) { value.insert(.maskControl) }
        if contains(.option) { value.insert(.maskAlternate) }
        if contains(.shift) { value.insert(.maskShift) }
        return value
    }
}
