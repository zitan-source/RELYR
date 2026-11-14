import AppKit
import ApplicationServices
import Carbon
import OSLog

final class InputEngine {
    let machine = LayerMachine()
    private var mxMaster4Observer: UUID?
    private func configureMXMaster4() {
        let needed = enabled && !applicationInputDisabled &&
            machine.configuration.mappings(for:machine.runtimeProfileID,mouseDevice:"046d:b042").contains { $0.input == 340 }
        if needed && mxMaster4Observer == nil {
            MXMaster4HID.shared.onError = { [weak self] message in self?.onStatus?(message) }
            mxMaster4Observer = MXMaster4HID.shared.observe { [weak self] down in self?.handleMXMaster4(down:down) }
        } else if !needed, let observer = mxMaster4Observer {
            mxMaster4Observer = nil; MXMaster4HID.shared.remove(observer)
        }
    }
    private func handleMXMaster4(down: Bool) {
        MouseDeviceTracker.shared.markMXMaster4()
        guard enabled, !applicationInputDisabled,
              let button = CGMouseButton(rawValue: 3),
              let event = CGEvent(mouseEventSource:nil,mouseType:down ? .otherMouseDown : .otherMouseUp,
                                  mouseCursorPosition:CGEvent(source:nil)?.location ?? .zero,mouseButton:button) else { return }
        event.setIntegerValueField(.mouseEventButtonNumber,value:32)
        event.flags = CGEventSource.flagsState(.hidSystemState)
        _ = handle(proxy:nil,type:event.type,event:event)
    }
    private var tap: CFMachPort?
    private var mousePressDevice: [Int64:String] = [:]
    private var mousePressCode: [Int64:UInt16] = [:]
    private var verificationMouseDevice: ((Int) -> String?)?

    private func mouseCode(for button: Int64) -> UInt16? {
        mousePressCode[button] ?? machine.configuration.mouseInput(button)
    }
    var onMouseDevice: ((String?) -> Void)?
    private var source: CFRunLoopSource?
    private var pendingSpace: CGEvent?
    private var pendingPressEvents: [UInt16: CGEvent] = [:]
    private var holdTimer: Timer?
    private var scheduledHoldDeadline: TimeInterval?
    private var draggedPresses: [UInt16: CGEvent] = [:]
    private var pendingMouse: CGEvent?
    private var mouseDragStarted = false
    private var generatedMousePhysical: [UInt16: Int64] = [:]
    private var generatedMouse: [UInt16: NativeEvents.MousePress] = [:]
    private var queuedReplay: [CGEvent] = []
    private var verificationSink: ((CGEvent) -> Void)?
    private var verificationFlagsState: ((CGEventSourceStateID) -> CGEventFlags)?
    private var verificationSystemQueue: [() -> Void]?
    private var verificationSystemLocations: [CGEventTapLocation] = []
    // Replay runs on the main run loop after the tap callback returns. Queued restorations
    // are never cancelled by a profile change or emergency stop.
    private func replayMouse(_ events: [CGEvent]) {
        let marker = self.marker
        for event in events { event.setIntegerValueField(.eventSourceUserData, value: marker) }
        queuedReplay.append(contentsOf: events)
        DispatchQueue.main.async { [weak self] in self?.drainReplay() }
    }
    private func drainReplay() {
        let events = queuedReplay; queuedReplay.removeAll()
        for event in events { if let sink = verificationSink { sink(event) } else { event.post(tap: .cgSessionEventTap) } }
    }
    private func mouseRelease(_ down: CGEvent) -> CGEvent? {
        guard let up = down.copy() else { return nil }
        up.type = down.type == .leftMouseDown ? .leftMouseUp : (down.type == .rightMouseDown ? .rightMouseUp : .otherMouseUp)
        if let location = CGEvent(source: nil)?.location { up.location = location }
        return up
    }

    private func beginMouse(_ input: UInt16, button: UInt16, flags: CGEventFlags, physical: CGEventFlags, point: CGPoint) -> Bool {
        guard generatedMouse[input] == nil, generatedMouse.isEmpty,
              let press = NativeEvents.mousePress(button,flags:flags,physicalFlags:physical,point:point,marker:marker) else { return false }
        generatedMouse[input] = press; replayMouse(press.begin); return true
    }
    private func endMouse(_ input: UInt16, point: CGPoint) {
        guard let press = generatedMouse.removeValue(forKey:input) else { return }
        generatedMousePhysical.removeValue(forKey:input)
        let physical = verificationSink == nil ? CGEventSource.flagsState(.hidSystemState) : []
        var flags = physical.union(press.requestedFlags)
        let modifierFlags: [Int64: CGEventFlags] = [59:.maskControl,58:.maskAlternate,56:.maskShift,55:.maskCommand]
        var events: [CGEvent] = []
        for event in press.end {
            if event.type == .flagsChanged, let flag = modifierFlags[event.getIntegerValueField(.keyboardEventKeycode)] {
                if physical.contains(flag) { continue } // The user may have pressed it during the drag.
                flags.remove(flag); event.flags = flags
            } else { event.location = point; event.flags = flags }
            events.append(event)
        }
        replayMouse(events)
    }
    private func recoverReleasedMouseSources() {
        guard verificationSink == nil else { return }
        for input in Array(generatedMouse.keys) {
            let held: Bool
            if let raw = generatedMousePhysical[input], let button = CGMouseButton(rawValue:UInt32(raw)) {
                held = CGEventSource.buttonState(.hidSystemState,button:button)
            } else if input == 340 { continue } // HID++ supplies its own release and disconnect.
            else { held = CGEventSource.keyState(.hidSystemState,key:input) }
            if !held { _ = machine.key(input,down:false); endMouse(input,point:CGEvent(source:nil)?.location ?? .zero) }
        }
    }

    private func pulseMouse(_ mapping: Mapping, flags: CGEventFlags, point: CGPoint) {
        guard generatedMouse.isEmpty,
              let press = NativeEvents.mousePress(mapping.output,flags:mapping.modifiers.flags,physicalFlags:flags,point:point,marker:marker) else { return }
        replayMouse(press.begin + press.end)
    }

    private var timer: Timer?
    private var generation = 0
    private var textGeneration = 0
    private var textIsRunning = false
    private var foregroundProcessID: pid_t?
    private let marker: Int64 = 0x52454C59524D
    private(set) var enabled = false
    private var resumeRequested = false
    private var releaseWaitStarted: TimeInterval?
    private var recoveryBackoff = RecoveryBackoff()
    private var secureInputEnabled: () -> Bool = { IsSecureEventInputEnabled() }
    private var sleeping = false
    private var lastRecoveryNotice: String?
    private func recoveryNotice(_ message: String) {
        guard lastRecoveryNotice != message else { return }; lastRecoveryNotice = message; onStatus?(message)
    }
    private var inactiveSession = false
    private var recentTimeouts: [TimeInterval] = []
    var requested: Bool { enabled || resumeRequested }
    var onStatus: ((String) -> Void)?
    var onProfile: ((String) -> Void)?
    var onKeypad: ((Bool) -> Void)?
    var onShowRelyr: (() -> Void)?
    var onToggleDeck: ((String) -> Void)?
    private let windowActions = WindowActionService()
    private var lastTrackpadMenuPulse: (time: UInt64, input: UInt16)?
    private var menuBarRegions: [CGRect] = []
    private func refreshMenuBarRegions() {
        guard let primary = NSScreen.screens.first else { menuBarRegions = []; return }
        menuBarRegions = NSScreen.screens.compactMap { screen in
            let height = screen.frame.maxY - screen.visibleFrame.maxY
            guard height > 0 && height <= 100 else { return nil }
            return CGRect(x: screen.frame.minX, y: primary.frame.maxY - screen.frame.maxY, width: screen.frame.width, height: height)
        }
    }
    // Menu-bar Actions describe device movement, not scrolling content.
    private func wheelInput(_ event: CGEvent) -> UInt16? {
        let inMenu = machine.mouseLayer == nil && menuBarRegions.contains { $0.contains(event.location) }
        let inverted = inMenu && (NSEvent(cgEvent:event)?.isDirectionInvertedFromDevice ?? false)
        return NativeEvents.scrollInput(event,invertedFromDevice:inverted)
    }
    private func menuBarLayer(_ code: UInt16, at point: CGPoint) -> Layer? {
        guard machine.mouseLayer == nil, menuBarRegions.contains(where: { $0.contains(point) }),
              machine.configuration.mappings(for: machine.runtimeProfileID).contains(where: { $0.layer == .menuBar && $0.input == code && InputPolicy.allows(code, layer: .menuBar, trigger: $0.trigger) }) else { return nil }
        return .menuBar
    }
    @discardableResult private func performWindow(_ command: WindowCommand, menuBar: Bool, point: CGPoint?) -> String? {
        windowActions.animationDuration = NSWorkspace.shared.accessibilityDisplayShouldReduceMotion ? 0 : 0.14
        let error = windowActions.perform(command, mode: menuBar ? .active : (machine.configuration.windowTarget ?? .active), point: point)
        if let error = error { onStatus?(error) }; return error
    }
    private func enqueueSystem(_ command: SystemCommand, modifiers: Modifiers = []) {
        let ticket = generation
        let action: () -> Void = { [weak self] in
            guard let self = self, self.enabled, self.generation == ticket else { return }
            self.performSystem(command,modifiers:modifiers)
        }
        if verificationSystemQueue != nil { verificationSystemQueue?.append(action) }
        else { DispatchQueue.main.async(execute: action) }
    }
    var onToggleAutoExtract: (() -> Void)?
    var onClipboardHistory: (() -> Void)?
    private func systemPhysicalFlags() -> CGEventFlags {
        // Session state includes our synthetic Command-Q (including its key-up).
        // It can remain there after the target quits until the next ordinary click.
        // Only hardware modifiers may be inherited by a subsequent OS shortcut.
        if let read = verificationFlagsState { return read(.hidSystemState) }
        return verificationSink == nil ? CGEventSource.flagsState(.hidSystemState) : []
    }
    private func performSystem(_ command: SystemCommand, modifiers: Modifiers = []) {
        if command == .finderExisting || command == .finderNewWindow {
            FinderActions.perform(newWindow:command == .finderNewWindow) { [weak self] error in
                if let error = error { self?.onStatus?(error) }
            }
            return
        }
        if command == .toggleAutoExtract { onToggleAutoExtract?(); return }
        if command == .keyboardCleaner { requestKeyboardCleaning(); return }
        if command == .clipboardHistory { onClipboardHistory?(); return }
        if command == .numpad || command == .extendedKeypad { onKeypad?(command == .extendedKeypad); return }
        if [.wifiOn,.wifiOff,.wifiToggle].contains(command) { if let error = ConnectivityActions.perform(command) { onStatus?(error) }; return }
        if command == .showRelyr { onShowRelyr?(); return }
        if let key = MacSystemEvents.mediaKey(command) {
            guard let events = MacSystemEvents.media(key,marker:marker) else { onStatus?("メディアキーを生成できません。"); return }
            // Media keys must reach the system media-key handler, which selects the
            // current Bluetooth/built-in route and presents the native level HUD.
            // This method is reached only from the deferred output queue.
            drainReplay()
            for event in events {
                if let sink = verificationSink { verificationSystemLocations.append(.cghidEventTap); sink(event) }
                else { event.post(tap:.cghidEventTap) }
            }
            return
        }
        if let event = MacSystemEvents.scroll(command,marker:marker,modifiers:modifiers.flags) { replayMouse([event]); return }

        if let (code, flags) = command.shortcut(layout:machine.configuration.keyboardLayout) {
            guard !machine.outputIsHeld(code), let events = NativeEvents.systemShortcut(code,flags:flags.flags,physicalFlags:systemPhysicalFlags(),marker:marker) else {
                onStatus?("同じキーを離してから実行してください。"); return
            }
            // Called only after the originating callback returns. Global shortcuts must
            // enter at WindowServer's HID entry, before login-session shortcut routing.
            drainReplay()
            for event in events {
                if let sink = verificationSink { verificationSystemLocations.append(.cghidEventTap); sink(event) }
                else { event.post(tap:.cghidEventTap) }
            }
            onStatus?("Action送信：\(command.title)（対象アプリ・macOS側のショートカット設定に従います）")
            return
        }
        if let error = AudioActions.perform(command,backend:SystemAudioBackend(input:command == .micMute)) { onStatus?(error) }
    }
    private func sendNativeSpaceShortcut(right: Bool) -> Bool {
        let code: UInt16 = right ? 124 : 123
        guard let events = NativeEvents.systemShortcut(code,flags:.maskControl,
                physicalFlags:systemPhysicalFlags(),marker:marker) else { return false }
        drainReplay()
        for event in events { event.post(tap:.cghidEventTap) }
        return true
    }
    var onResourceResult: ((ActionType,String,String?) -> Void)?
    private func resourceResult(_ kind: ActionType, _ value: String, _ error: String?) {
        onResourceResult?(kind,value,error)
        if let error = error { onStatus?(error) }
    }
    private func openFile(_ path: String) {
        var directory: ObjCBool = false
        guard FileManager.default.fileExists(atPath:path,isDirectory:&directory) else { resourceResult(.file,path,"ファイルが見つかりません。割り当てたファイルの場所を確認してください。"); return }
        if directory.boolValue && !NSWorkspace.shared.isFilePackage(atPath:path) {
            FinderActions.openFolder(path) { [weak self] error in self?.resourceResult(.file,path,error) }
            return
        }
        resourceResult(.file,path,NSWorkspace.shared.open(URL(fileURLWithPath:path)) ? nil : "ファイルを開けません。対応するアプリとファイルへのアクセス権を確認してください。")
    }


    init() {
        WindowSpaces.nativeSpaceShortcut = { [weak self] right in self?.sendNativeSpaceShortcut(right:right) ?? false }
        windowActions.onCompletion = { [weak self] error in self?.windowOperationCompleted(error) }
        refreshMenuBarRegions()
        let center = NSWorkspace.shared.notificationCenter
        center.addObserver(self, selector: #selector(willSleep), name: NSWorkspace.willSleepNotification, object: nil)
        center.addObserver(self, selector: #selector(sessionResigned), name: NSWorkspace.sessionDidResignActiveNotification, object: nil)
        center.addObserver(self, selector: #selector(didWake), name: NSWorkspace.didWakeNotification, object: nil)
        center.addObserver(self, selector: #selector(sessionActive), name: NSWorkspace.sessionDidBecomeActiveNotification, object: nil)
        center.addObserver(self, selector: #selector(frontmostChanged), name: NSWorkspace.didActivateApplicationNotification, object: nil)
        let recoveryTimer = Timer(timeInterval:0.5,repeats:true) { [weak self] _ in self?.checkRecovery() }
        timer = recoveryTimer; RunLoop.main.add(recoveryTimer,forMode:.common)
    }
    private func checkRecovery() {
        guard requested, verificationSink == nil else { return }
        refreshMenuBarRegions()
        if enabled { recoverReleasedMouseSources() }
        if !AXIsProcessTrusted() {
            if enabled { pause("一時停止：アクセシビリティの許可待ち。許可が戻ると自動再開します。") }
            else { recoveryNotice("復帰待ち：設定の「入力とアクセシビリティ」から許可を確認してください。許可後は自動再開します。") }
            return
        }
        if secureInputEnabled() {
            if enabled { pause("一時停止：パスワード等の保護された入力中。終了後に自動復帰します。") }
            else { recoveryNotice("復帰待ち：パスワード等の保護された入力が終了すると自動再開します。") }
            return
        }
        guard !sleeping, !inactiveSession else { return }
        if resumeRequested && !physicalInputReleased {
            let now = ProcessInfo.processInfo.systemUptime
            if releaseWaitStarted == nil { releaseWaitStarted = now }
            if now - (releaseWaitStarted ?? now) < 2 { return }
            // HID state can remain latched across sleep. Never gate the whole
            // engine indefinitely: quarantine only those presses on reconnect.
        } else { releaseWaitStarted = nil }
        if enabled, let tap = tap, !CGEvent.tapIsEnabled(tap:tap) { pause("一時停止：入力監視を再接続しています。") }
        if resumeRequested && tap == nil && recoveryBackoff.ready(now:ProcessInfo.processInfo.systemUptime) { start() }
    }

    private var macroPlayback: MacroPlayback?
    private var macroLaunchToken: UUID?
    private var macroWindowPending = false
    private let macroNewWindow = MacroNewWindow()
    private var macroOperationPending: Bool { windowActions.isBusy || macroLaunchToken != nil || macroWindowPending || macroNewWindow.isBusy }
    private var macroTimer: Timer?
    private var macroReleases: [(String,[CGEvent])] = []
    private var macroMouseButtons: [UInt16] = []
    private var macroMouseButton: UInt16? { macroMouseButtons.first }
    private var macroFlagsByInput: [String:CGEventFlags] = [:]
    private var macroModifierUps: [UInt16:CGEvent] = [:]
    private var macroBaseFlags: CGEventFlags = []
    private var macroHeldFlags: CGEventFlags { macroFlagsByInput.values.reduce(macroBaseFlags) { $0.union($1) } }
    private func releaseMacroInput(_ identity: String) {
        guard let index = macroReleases.firstIndex(where: { $0.0 == identity }) else { return }
        let flagsBefore = macroHeldFlags
        let events = macroReleases.remove(at:index).1
        macroFlagsByInput.removeValue(forKey:identity)
        let point = CGEvent(source:nil)?.location ?? .zero
        for event in events {
            event.flags = flagsBefore.union(event.flags.intersection([.maskSecondaryFn,.maskNumericPad]))
            if [.leftMouseUp,.rightMouseUp,.otherMouseUp].contains(event.type) { event.location = point }
        }
        replayMouse(events)
        let remaining = macroHeldFlags
        // Match the reverse order of the modifier presses in NativeEvents.systemShortcut.
        let keys: [(UInt16, CGEventFlags)] = [
            (55, .maskCommand), (56, .maskShift), (58, .maskAlternate),
            (59, .maskControl), (63, .maskSecondaryFn)
        ]
        var flags = flagsBefore
        let physicalFnHeld = macroModifierUps[63] != nil && systemPhysicalFlags().contains(.maskSecondaryFn)
        if physicalFnHeld { flags.insert(.maskSecondaryFn) }
        for (key, flag) in keys where !remaining.contains(flag) {
            guard let event = macroModifierUps.removeValue(forKey: key) else { continue }
            if key == 63 && physicalFnHeld { continue }
            flags.remove(flag)
            event.flags = flags
            replayMouse([event])
        }
        if identity.hasPrefix("mouse"), let button = UInt16(identity.dropFirst(5)) { macroMouseButtons.removeAll { $0 == button } }
    }

    private var recordedMacro: String?
    private var recordedSteps: [MacroStep] = []
    private var recordMovement = false, recordRelative = true, recordMapped = true
    private var recordLimit = 10000
    private var lastRecordTime = 0.0
    private var lastRecordPoint: CGPoint?
    var onSwitchProfile: ((String) -> Bool)?
    var onRecordingProgress: ((Int) -> Void)?
    var onMacroRecorded: ((String,[MacroStep]) -> Void)?

    struct MacroProgress: Equatable {
        let id: String
        let message: String
        let running: Bool
        let failed: Bool
        let stepID: String?
    }
    var onMacroProgress: ((MacroProgress) -> Void)?
    private var playingMacroID: String?
    private var macroDisplayStepIDs: [String] = []
    private func macroDisplayStep(_ index: Int) -> String? {
        macroDisplayStepIDs.indices.contains(index) ? macroDisplayStepIDs[index] : nil
    }
    private var lastMacroProgress: MacroProgress?
    private func reportMacro(_ message: String, running: Bool = true, failed: Bool = false, stepID: String? = nil) {
        guard let id = playingMacroID else { return }
        let progress = MacroProgress(id:id,message:message,running:running,failed:failed,stepID:stepID ?? (failed ? lastMacroProgress?.stepID : nil))
        guard progress != lastMacroProgress else { return }
        lastMacroProgress = progress; onMacroProgress?(progress)
    }
    private var lastMacroTarget: NSRunningApplication?
    func playMacroFromEditor(_ id: String) {
        guard enabled else { onStatus?("マクロの再生には入力の有効化が必要です。"); return }
        if machine.configuration.macros.first(where: { $0.id == id })?.steps.first(where: { $0.action != nil })?.action?.action == .launch {
            playMacro(id); return
        }

        guard let target = lastMacroTarget, !target.isTerminated else { onStatus?("再生先のアプリを一度開いてから、マクロ画面へ戻って再生してください。"); return }
        let ticket = generation
        target.activate(options:.activateIgnoringOtherApps)
        DispatchQueue.main.asyncAfter(deadline:.now()+0.15) { [weak self] in
            guard let self = self, self.enabled, self.generation == ticket else { return }
            guard NSWorkspace.shared.frontmostApplication?.processIdentifier == target.processIdentifier else { self.onStatus?("再生先アプリへ切り替えられなかったため、マクロを開始しませんでした。"); return }
            self.playMacro(id)
        }
    }
    func playMacro(_ id: String) {
        guard enabled, !isCleaningKeyboard, recordedMacro == nil else { onStatus?("マクロを再生するには入力を有効にし、記録を停止してください。"); return }
        do {
            let plan = try MacroPlan.expand(id,definitions:machine.configuration.macros)
            stopMacro()
            playingMacroID = id
            macroDisplayStepIDs = []
            for step in machine.configuration.macros.first(where: { $0.id == id })?.steps ?? [] {
                if let action = step.action, action.action == .macro {
                    let children = try MacroPlan.expand(action.payload,definitions:machine.configuration.macros)
                    macroDisplayStepIDs += Array(repeating:step.id,count:children.count + (step.delayMilliseconds > 0 ? 1 : 0))
                } else { macroDisplayStepIDs.append(step.id) }
            }
            macroPlayback = MacroPlayback(steps:plan,now:ProcessInfo.processInfo.systemUptime)
            onStatus?("マクロ再生中 · Control＋Shift＋F12で停止")
            scheduleMacro()
        } catch { onStatus?(error.localizedDescription) }
    }
    private func windowOperationCompleted(_ error: String?) {
        let macroOwned = macroWindowPending
        macroWindowPending = false
        if let error = error {
            if macroOwned { stopMacro(reason:"停止：" + error, failed:true) }
            onStatus?(macroOwned ? "マクロを停止しました：" + error : error)
        }
    }
    func stopMacro(reason: String = "停止しました", failed: Bool = false) {
        reportMacro(reason,running:false,failed:failed)
        playingMacroID = nil; macroDisplayStepIDs.removeAll()
        macroNewWindow.cancel()
        if macroWindowPending { windowActions.cancelPending() }
        macroWindowPending = false
        macroLaunchToken = nil
        macroTimer?.invalidate(); macroTimer = nil; macroPlayback?.cancel(); macroPlayback = nil
        for identity in macroReleases.reversed().map({ $0.0 }) { releaseMacroInput(identity) }
        macroReleases.removeAll(); macroMouseButtons.removeAll(); macroFlagsByInput.removeAll(); macroModifierUps.removeAll(); macroBaseFlags = []
    }
    private func scheduleMacro() {
        guard let state = macroPlayback else { return }
        if state.finished && !macroOperationPending { stopMacro(reason:"完了しました"); onStatus?("マクロの再生が完了しました。"); return }
        if macroOperationPending {
            reportMacro("手順 \(state.index) / \(state.steps.count) · アプリの応答待ち",stepID:macroDisplayStep(state.index-1))
        } else if !state.finished {
            let step = state.steps[state.index]
            let name = step.action?.targetDescription(layout:machine.configuration.keyboardLayout) ?? (step.phase == .move ? "マウス移動" : "待機")
            let wait = max(0,state.deadline-ProcessInfo.processInfo.systemUptime)
            reportMacro("手順 \(state.index+1) / \(state.steps.count) · \(name)" + (wait > 0.1 ? "（待機 \(String(format:"%.1f",wait))秒）" : ""),stepID:macroDisplayStep(state.index))
        }
        macroTimer = Timer.scheduledTimer(withTimeInterval:max(macroOperationPending ? 0.02 : 0.001,state.deadline-ProcessInfo.processInfo.systemUptime),repeats:false) { [weak self] _ in
            guard let self = self, self.enabled, var state = self.macroPlayback else { return }
            if self.macroOperationPending { self.scheduleMacro(); return }
            guard self.verificationSink != nil || (AXIsProcessTrusted() && !secureInputEnabled()) else { self.stopMacro(reason:"停止：入力権限またはセキュア入力を確認してください",failed:true); return }
            if let step = state.next(now:ProcessInfo.processInfo.systemUptime) {
                self.macroPlayback = state
                if !self.executeMacroStep(step) { self.stopMacro(reason:"停止：手順 \(state.index) を実行できませんでした",failed:true); self.onStatus?("マクロを停止しました：手順を実行できませんでした。"); return }
            }
            self.scheduleMacro()
        }
    }
    private func executeMacroStep(_ step: MacroStep) -> Bool {
        let point = CGEvent(source:nil)?.location ?? .zero
        if step.phase == .move {
            let destination = step.relative ? CGPoint(x:point.x+step.x,y:point.y+step.y) : CGPoint(x:step.x,y:step.y)
            let button = macroMouseButton ?? 0
            let type: CGEventType = macroMouseButton == nil ? .mouseMoved : (button == 0 ? .leftMouseDragged : (button == 1 ? .rightMouseDragged : .otherMouseDragged))
            guard let event = CGEvent(mouseEventSource:nil,mouseType:type,mouseCursorPosition:destination,mouseButton:button == 0 ? .left : (button == 1 ? .right : .center)) else { return false }
            event.setIntegerValueField(.mouseEventButtonNumber,value:Int64(button)); event.flags = macroHeldFlags; replayMouse([event]); return true
        }
        guard let storedAction = step.action else { return true }
        let action = storedAction.resolvedWindowShortcut
        if verificationSink == nil, let playback = macroPlayback,
           MacroNewWindow.requiresConfirmation(step,remaining:playback.steps.dropFirst(playback.index)) {
            drainReplay()
            macroNewWindow.start { [weak self] error in
                if let error = error { self?.stopMacro(reason:"停止：" + error,failed:true); self?.onStatus?("マクロを停止しました：" + error) }
            }
            return true
        }
        let identity = (action.action == .mouse ? "mouse" : "key") + String(action.output)
        if step.phase == .up {
            releaseMacroInput(identity)
            return true
        }
        if action.action.isKey || action.action == .mouse {
            guard !macroReleases.contains(where: { $0.0 == identity }) else { return true }
            if macroReleases.isEmpty { macroBaseFlags = verificationSink == nil ? CGEventSource.flagsState(.combinedSessionState) : [] }
            let physical = macroHeldFlags
            var begin: [CGEvent] = [], end: [CGEvent] = []
            if action.action == .mouse {
                guard let pair = NativeEvents.mousePress(action.output,flags:action.modifiers.flags,physicalFlags:physical,point:point,marker:marker) else { return false }
                begin = pair.begin; end = pair.end
            } else {
                guard !machine.outputIsHeld(action.output), let events = NativeEvents.systemShortcut(action.output,flags:action.modifiers.flags,physicalFlags:physical,marker:marker), let up = events.firstIndex(where: { $0.type == .keyUp }) else { return false }
                begin = Array(events[..<up]); end = Array(events[up...])
            }
            if step.phase == .down {
                for event in end where event.type == .flagsChanged { macroModifierUps[UInt16(clamping:event.getIntegerValueField(.keyboardEventKeycode))] = event }
                macroReleases.append((identity,end.filter { $0.type != .flagsChanged })); macroFlagsByInput[identity] = action.modifiers.flags
                if action.action == .mouse { macroMouseButtons.append(action.output) }
                replayMouse(begin)
            } else { replayMouse(begin+end) }
            return true
        }
        // Execute one complete Action before scheduling the next delayed step.
        switch action.action {
        case .disabled: break
        case .profile: drainReplay(); return onSwitchProfile?(action.payload) ?? false
        case .system: guard let command = SystemCommand(rawValue:action.payload) else { return false }; drainReplay(); performSystem(command,modifiers:action.modifiers)
        case .window:
            guard let command = WindowCommand(rawValue:action.payload) else { return false }
            drainReplay()
            guard performWindow(command,menuBar:true,point:nil) == nil else { return false }
            macroWindowPending = windowActions.isBusy
        case .file: drainReplay(); openFile(action.application)
        case .launch:
            drainReplay()
            let token = UUID(); macroLaunchToken = token
            launchApplication(action.application) { [weak self] success in
                guard let self = self, self.macroLaunchToken == token else { return }
                self.macroLaunchToken = nil
                if !success { self.stopMacro(reason:"停止：アプリを起動できませんでした",failed:true); self.onStatus?("アプリを起動できずマクロを停止しました。") }
            }
            DispatchQueue.main.asyncAfter(deadline:.now()+12) { [weak self] in
                guard let self = self, self.macroLaunchToken == token else { return }
                self.stopMacro(reason:"停止：アプリの起動が時間内に完了しませんでした",failed:true); self.onStatus?("アプリの起動が完了しないためマクロを停止しました。")
            }
        case .url: drainReplay(); openURL(action.payload)
        case .deck: drainReplay(); onToggleDeck?(action.payload)
        case .text:
            var events: [CGEvent] = []
            guard let pairs = TextInputRouting.pairs(action.payload,marker:marker) else { return false }
            for pair in pairs { events += [pair.down,pair.up] }
            replayMouse(events)
        default: return false
        }
        return true
    }
    func beginMacroRecording(_ id: String, movement: Bool, relative: Bool, mapped: Bool) -> Bool {
        guard enabled, !isCleaningKeyboard, !secureInputEnabled() else { onStatus?("記録には入力の有効化が必要です。セキュア入力中は記録できません。"); return false }
        guard let definition = machine.configuration.macros.first(where: { $0.id == id }), definition.steps.count < 10000 else { onStatus?("マクロを選択し、手順を10000件未満にしてください。"); return false }
        stopMacro(); finishMacroRecording()
        recordLimit = 10000-definition.steps.count
        recordedMacro = id; recordedSteps = []; recordMovement = movement; recordRelative = relative; recordMapped = mapped
        lastRecordTime = ProcessInfo.processInfo.systemUptime; lastRecordPoint = nil
        onStatus?("マクロ記録中：他のアプリで操作してください。Control＋Shift＋F12で終了")
        return true
    }
    func finishMacroRecording() {
        guard let id = recordedMacro else { return }
        recordedMacro = nil; let steps = recordedSteps; recordedSteps = []; lastRecordPoint = nil
        DispatchQueue.main.async { [weak self] in self?.onMacroRecorded?(id,steps) }
    }
    private func recordMacroAction(_ mapping: Mapping, phase: MacroPhase = .pulse) {
        guard recordedMacro != nil, recordMapped, mapping.action != .gesture, mapping.action != .macro, foregroundProcessID != ProcessInfo.processInfo.processIdentifier, !secureInputEnabled() else { return }
        let now = ProcessInfo.processInfo.systemUptime
        var action = mapping; action.layer = .normal; action.input = 0; action.trigger = .tap
        recordedSteps.append(MacroStep(delayMilliseconds:min(600000,max(0,Int((now-lastRecordTime)*1000))),action:action,phase:phase)); lastRecordTime = now
        let count = recordedSteps.count
        DispatchQueue.main.async { [weak self] in self?.onRecordingProgress?(count) }
        if count >= recordLimit { finishMacroRecording() }
    }
    private func recordMacroEvent(_ type: CGEventType, event: CGEvent) {
        guard recordedMacro != nil, foregroundProcessID != ProcessInfo.processInfo.processIdentifier,
              !secureInputEnabled() else { return }
        let now = ProcessInfo.processInfo.systemUptime
        var step = MacroStep(delayMilliseconds:min(600000,max(0,Int((now-lastRecordTime)*1000))))
        if type == .keyDown || type == .keyUp {
            guard event.getIntegerValueField(.keyboardEventAutorepeat) == 0 else { return }
            let code = UInt16(clamping:event.getIntegerValueField(.keyboardEventKeycode))
            guard Keys.outputs.contains(where: { $0.code == code }) else { return }
            var flags = Modifiers(event.flags)
            if systemPhysicalFlags().contains(.maskSecondaryFn) { flags.insert(.fn) }
            step.action = Mapping(layer:.normal,input:0,action:flags.isEmpty ? .key : .shortcut,output:code,modifiers:flags)
            step.phase = type == .keyDown ? .down : .up
        } else if [.leftMouseDown,.leftMouseUp,.rightMouseDown,.rightMouseUp,.otherMouseDown,.otherMouseUp].contains(type) {
            let button = UInt16(clamping:event.getIntegerValueField(.mouseEventButtonNumber)); guard button <= 31 else { return }
            step.action = Mapping(layer:.normal,input:0,action:.mouse,output:button,modifiers:Modifiers(event.flags))
            step.phase = [.leftMouseDown,.rightMouseDown,.otherMouseDown].contains(type) ? .down : .up
        } else if recordMovement, [.mouseMoved,.leftMouseDragged,.rightMouseDragged,.otherMouseDragged].contains(type) {
            guard now-lastRecordTime >= 0.016 else { return }
            step.phase = .move; step.relative = recordRelative
            if recordRelative {
                guard let previous = lastRecordPoint else { lastRecordPoint = event.location; return }
                step.x = event.location.x-previous.x; step.y = event.location.y-previous.y
            } else { step.x = event.location.x; step.y = event.location.y }
            lastRecordPoint = event.location
        } else if type == .scrollWheel {
            let y = event.getIntegerValueField(.scrollWheelEventDeltaAxis1), x = event.getIntegerValueField(.scrollWheelEventDeltaAxis2)
            guard x != 0 || y != 0 else { return }
            step.action = Mapping(layer:.normal,input:0,action:.system,payload:abs(y) >= abs(x) ? (y > 0 ? "scrollUp" : "scrollDown") : (x > 0 ? "scrollLeft" : "scrollRight"))
        } else { return }
        recordedSteps.append(step); lastRecordTime = now
        let count = recordedSteps.count
        DispatchQueue.main.async { [weak self] in self?.onRecordingProgress?(count) }
        if count >= recordLimit { finishMacroRecording() }
    }

    private var gestureCursorAnchor: CGPoint?
    private var verificationCursorAssociation: ((Bool) -> CGError)?
    private var verificationCursorWarp: ((CGPoint) -> CGError)?
    private func associateGestureCursor(_ connected: Bool) -> CGError {
        if let operation = verificationCursorAssociation { return operation(connected) }
        if verificationSink != nil { return .success }
        return CGAssociateMouseAndMouseCursorPosition(connected ? 1 : 0)
    }
    private func warpGestureCursor(_ point: CGPoint) -> CGError {
        if let operation = verificationCursorWarp { return operation(point) }
        if verificationSink != nil { return .success }
        return CGWarpMouseCursorPosition(point)
    }
    private func unlockGestureCursor() {
        guard gestureCursorAnchor != nil else { return }
        gestureCursorAnchor = nil
        _ = associateGestureCursor(true)
    }
    deinit {
        // The normal stop path releases first; this also covers owner teardown.
        if (gestureCursorAnchor != nil || trackpadVolumeCursorAnchor != nil) && verificationSink == nil { _ = CGAssociateMouseAndMouseCursorPosition(1) }
    }
    private var gestureSession: GestureSession?
    private var gestureTimer: Timer?
    private func endGesture(cancel: Bool = false) {
        gestureTimer?.invalidate(); gestureTimer = nil
        unlockGestureCursor()
        guard var session = gestureSession else { return }
        gestureSession = nil
        if !cancel, let action = session.finish() { emitPulse(action,modifiers:[]) }
    }
    private func startGesture(_ id: String, source: UInt16, point: CGPoint) {
        guard gestureSession == nil, trackpadVolumeCursorAnchor == nil, generatedMouse.isEmpty,
              let definition = machine.configuration.gestures.first(where: { $0.id == id }) else { return }
        if definition.locksCursor {
            guard associateGestureCursor(false) == .success else {
                DispatchQueue.main.async { [weak self] in self?.onStatus?("ポインターを固定できないためジェスチャーを開始できません。アクセシビリティの許可を確認してください。") }
                return
            }
            gestureCursorAnchor = point
            guard warpGestureCursor(point) == .success else {
                unlockGestureCursor()
                DispatchQueue.main.async { [weak self] in self?.onStatus?("ポインターの開始位置を固定できませんでした。ジェスチャーを中止しました。") }
                return
            }
        }
        gestureSession = GestureSession(definition:definition,source:source,lastMotion:ProcessInfo.processInfo.systemUptime)
        gestureTimer = Timer.scheduledTimer(withTimeInterval:0.02,repeats:true) { [weak self] _ in
            guard let self = self, var session = self.gestureSession else { return }
            let elapsed = ProcessInfo.processInfo.systemUptime - session.lastMotion
            if elapsed >= 30 { self.endGesture(cancel:true); return }
            if elapsed >= 0.110, session.dx != 0 || session.dy != 0 {
                let action = session.commit(); self.gestureSession = session
                if let action = action { self.emitPulse(action,modifiers:[]) }
            }
        }
    }

    private var applicationInputDisabled = false
    private var capturedPhysical: Set<Int64> = []
    private var excludedReleases: Set<Int64> = []
    private var bypassedPhysical: Set<Int64> = []

    private func setApplicationInputDisabled(_ disabled: Bool) {
        guard disabled != applicationInputDisabled else { return }
        applicationInputDisabled = disabled
        if disabled {
            cancelMappedInput()
        }
    }

    private func cancelMappedInput() {
        windowActions.cancelPending()
        excludedReleases.formUnion(capturedPhysical); capturedPhysical.removeAll()
        stopMacro(); finishMacroRecording(); endGesture(cancel:true); cancelText()
        holdTimer?.invalidate(); holdTimer = nil; generation += 1
        for input in Array(generatedMouse.keys) { endMouse(input,point:CGEvent(source:nil)?.location ?? .zero) }
        for original in draggedPresses.values { if let up = mouseRelease(original) { replayMouse([up]) } }
        if machine.isMouseDragging, let down = pendingMouse, let up = mouseRelease(down) { replayMouse([up]) }
        draggedPresses.removeAll(); pendingPressEvents.removeAll(); pendingSpace = nil; pendingMouse = nil; mouseDragStarted = false
        for code in machine.reset() {
            if let up = CGEvent(keyboardEventSource:nil,virtualKey:code,keyDown:false) { replayMouse([up]) }
        }
    }

    private let trackpadVolumeHUD = TrackpadVolumeHUD()
    private var trackpadVolumeCursorAnchor: CGPoint?
    private var trackpadVolumeLastFrame = 0.0
    private var trackpadVolumeWatchdog: Timer?
    private var trackpadVolumeTarget: (device: UInt32, level: Float32)?
    private var lastTrackpadVolumeError: TimeInterval = -.infinity

    private func updateTrackpadVolumeCursor() {
        trackpadVolumeLastFrame = ProcessInfo.processInfo.systemUptime
        guard trackpadVolumeCursorAnchor == nil, gestureSession == nil, generatedMouse.isEmpty,
              let anchor = CGEvent(source: nil)?.location,
              associateGestureCursor(false) == .success else { return }
        trackpadVolumeCursorAnchor = anchor
        let timer = Timer(timeInterval: 0.25, repeats: true) { [weak self] _ in
            guard let self = self else { return }
            if ProcessInfo.processInfo.systemUptime - self.trackpadVolumeLastFrame >= 0.5 {
                self.multitouch.reset()
                self.finishTrackpadVolume()
            }
        }
        trackpadVolumeWatchdog = timer
        RunLoop.main.add(timer, forMode: .common)
    }

    private func finishTrackpadVolume() {
        trackpadVolumeTarget = nil
        trackpadVolumeWatchdog?.invalidate()
        trackpadVolumeWatchdog = nil
        if trackpadVolumeCursorAnchor != nil {
            trackpadVolumeCursorAnchor = nil
            _ = associateGestureCursor(true)
        }
    }

    private func adjustTrackpadVolume(by delta: Float32) {
        let output = SystemAudioBackend()
        guard let device = output.device,
              let level = trackpadVolumeTarget?.device == device ? trackpadVolumeTarget?.level : output.volume() else {
            reportTrackpadVolumeError("この出力機器の音量を取得できません。")
            return
        }
        let target = min(1, max(0, level + delta))
        guard target == level || output.setVolume(target) else {
            reportTrackpadVolumeError("この出力機器の音量を変更できません。")
            return
        }
        if delta > 0, output.muted() == true, !output.setMuted(false) {
            reportTrackpadVolumeError("音量は変更しましたが、消音を解除できませんでした。")
        }
        trackpadVolumeTarget = (device, target)
        trackpadVolumeHUD.show(target)
    }

    private func reportTrackpadVolumeError(_ message: String) {
        let now = ProcessInfo.processInfo.systemUptime
        guard now - lastTrackpadVolumeError >= 2 else { return }
        lastTrackpadVolumeError = now
        onStatus?(message)
    }

    private func trackpadMapping(_ code: UInt16) -> Mapping? {
        machine.configuration.mappings(for: machine.runtimeProfileID).first {
            $0.layer == .trackpad && $0.input == code && $0.trigger == .tap
        }
    }

    private func edgeVolumeDirection(_ code: UInt16) -> Float32? {
        guard let mapping = trackpadMapping(code), mapping.action == .system else { return nil }
        switch SystemCommand(rawValue: mapping.payload) {
        case .volumeUp: return 1
        case .volumeDown: return -1
        default: return nil
        }
    }

    private let multitouch = MultitouchInput()
    private func configureMultitouch() {
        let config = machine.configuration
        let mappings = config.mappings + config.profiles.flatMap { $0.mappings }
        guard enabled, mappings.contains(where:{ $0.layer == .trackpad && (400...441).contains($0.input) }) else { multitouch.stop(); return }
        multitouch.edgeWidth = config.edgeWidth
        multitouch.edgeMapping = { [weak self] code in
            guard let self = self, let mapping = self.machine.configuration.mappings(for:self.machine.runtimeProfileID)
                .first(where:{ $0.layer == .trackpad && $0.input == code && $0.trigger == .tap }) else { return nil }
            guard mapping.action == .system, let command = SystemCommand(rawValue:mapping.payload) else { return false }
            switch command {
            case .volumeUp,.volumeDown,.brightnessUp,.brightnessDown,.scrollUp,.scrollDown,.scrollLeft,.scrollRight: return true
            default: return false
            }
        }
        multitouch.isEdgeVolume = { [weak self] code in self?.edgeVolumeDirection(code) != nil }
        multitouch.edgeVolume = { [weak self] code, distance in
            guard let self = self, let direction = self.edgeVolumeDirection(code) else { return }
            self.adjustTrackpadVolume(by: direction * Float32(distance))
        }
        multitouch.onEdgeVolumeFrame = { [weak self] in self?.updateTrackpadVolumeCursor() }
        multitouch.onEdgeVolumeEnd = { [weak self] in self?.finishTrackpadVolume() }
        multitouch.isAllowed = { [weak self] in
            guard let self = self else { return false }
            return self.enabled && !self.isCleaningKeyboard && !self.applicationInputDisabled && !secureInputEnabled() && AXIsProcessTrusted()
        }
        multitouch.action = { [weak self] code in
            guard let self = self,
                  let mapping = self.machine.configuration.mappings(for:self.machine.runtimeProfileID).first(where:{ $0.layer == .trackpad && $0.input == code && $0.trigger == .tap }) else { return }
            self.performFromDeck(mapping)
        }
        multitouch.doubleTapEnabled = { [weak self] code in
            guard let self = self else { return false }
            return self.machine.configuration.mappings(for:self.machine.runtimeProfileID).contains { $0.layer == .trackpad && $0.input == code && $0.trigger == .tap }
        }
        if !multitouch.start() { onStatus?("多指ジェスチャーの入力機器を開始できません。通常のレイヤーは利用できます。") }
    }
    func apply(_ configuration: Configuration) {
        machine.configuration = configuration
        multitouch.reset()
        frontmostChanged()
        configureMultitouch()
        configureMXMaster4()
    }

    @objc private func frontmostChanged() {
        let app = NSWorkspace.shared.frontmostApplication
        if let app = app, app.processIdentifier != ProcessInfo.processInfo.processIdentifier { lastMacroTarget = app }
        if app?.processIdentifier != foregroundProcessID { cancelText(); multitouch.reset() }
        foregroundProcessID = app?.processIdentifier
        setApplicationInputDisabled(machine.configuration.disablesInput(bundleIdentifier:app?.bundleIdentifier))
        let id = machine.configuration.activeProfileID(bundleIdentifier: app?.bundleIdentifier)
        machine.runtimeProfileID = id
        onProfile?(machine.configuration.profileName(id))
        configureMXMaster4()
    }

    /// Deck is non-activating. Capture the current target and never redirect a delayed action.
    func performFromDeck(_ mapping: Mapping) {
        let mapping = mapping.resolvedWindowShortcut
        guard enabled, !isCleaningKeyboard, !applicationInputDisabled, AXIsProcessTrusted(), !secureInputEnabled() else {
            onStatus?("Deckを使うには入力変換を有効にしてください。セキュア入力中は使用できません。")
            return
        }
        guard let target = NSWorkspace.shared.frontmostApplication else { return }
        let ticket = generation
        DispatchQueue.main.async { [weak self] in
            guard let self = self, self.enabled, self.generation == ticket,
                  !secureInputEnabled(), AXIsProcessTrusted() else { return }
            // Opening a registered resource has no foreground target. A focus
            // transition must not silently discard this click.
            if mapping.action == .file { self.openFile(mapping.application); return }
            if mapping.action == .launch { self.launchApplication(mapping.application); return }
            if mapping.action == .url { self.openURL(mapping.payload); return }
            guard NSWorkspace.shared.frontmostApplication?.processIdentifier == target.processIdentifier else { return }
            if mapping.action == .disabled { return }
            if mapping.action == .profile { _ = self.onSwitchProfile?(mapping.payload); return }
            if mapping.action == .macro { self.playMacro(mapping.payload); return }
            if mapping.action == .gesture { self.onStatus?("ジェスチャーは割り当てたキー／マウスボタンを押して操作してください。"); return }
            if mapping.action == .mouse { self.pulseMouse(mapping,flags:CGEventSource.flagsState(.combinedSessionState),point:CGEvent(source:nil)?.location ?? .zero); return }
            if mapping.action == .system, let command = SystemCommand(rawValue: mapping.payload) { self.performSystem(command,modifiers:mapping.modifiers); return }
            if mapping.action == .window, let command = WindowCommand(rawValue: mapping.payload) { self.performWindow(command, menuBar: mapping.layer == .menuBar, point: nil); return }
            if mapping.action == .deck { self.onToggleDeck?(mapping.payload); return }
            if mapping.action == .text { self.typeText(mapping.payload, target: target.processIdentifier); return }
            guard !self.machine.outputIsHeld(mapping.output),
                  !CGEventSource.keyState(.combinedSessionState, key: mapping.output) else {
                self.onStatus?("同じ出力キーが押されています。キーを離してからDeckを押してください。")
                return
            }
            // Allocate both halves first, so allocation failure cannot leave a generated key held.
            let physicalFlags = CGEventSource.flagsState(.combinedSessionState)
            guard let pair = NativeEvents.key(mapping.output, flags: physicalFlags.union(mapping.modifiers.flags), marker: self.marker) else { return }
            for event in [pair.down, pair.up] {
                event.postToPid(target.processIdentifier)
            }
        }
    }

    // Automator Chrome-profile launchers otherwise send `open -na`, which creates a
    // new browser window every time. Match the launcher to the visible Chrome
    // profile control and focus its existing window before invoking the launcher.
    private func activateExistingChromeProfile(_ path: String) -> Bool {
        let name = URL(fileURLWithPath:path).deletingPathExtension().lastPathComponent
        guard Bundle(path:path)?.bundleIdentifier?.hasPrefix("com.apple.automator.") == true,
              let workflow = NSDictionary(contentsOfFile:URL(fileURLWithPath:path).appendingPathComponent("Contents/document.wflow").path),
              String(describing:workflow).contains("profile-directory"),
              let chrome = NSRunningApplication.runningApplications(withBundleIdentifier:"com.google.Chrome").first else { return false }
        var aliases = [name.lowercased()]
        if name.lowercased().hasSuffix("car") { aliases.append(String(name.dropLast(3)).lowercased()) }
        let root = AXUIElementCreateApplication(chrome.processIdentifier)
        var raw: CFTypeRef?
        guard AXUIElementCopyAttributeValue(root,kAXWindowsAttribute as CFString,&raw) == .success,
              let windows = raw as? [AXUIElement] else { return false }
        func value(_ element: AXUIElement, _ attribute: String) -> String? {
            var raw: CFTypeRef?
            guard AXUIElementCopyAttributeValue(element,attribute as CFString,&raw) == .success else { return nil }
            return raw as? String
        }
        func hasProfile(_ element: AXUIElement, depth: Int) -> Bool {
            guard depth < 7 else { return false }
            let role = value(element,kAXRoleAttribute) ?? ""
            if role == kAXButtonRole || role == kAXPopUpButtonRole {
                if let title = value(element,kAXTitleAttribute)?.lowercased(),aliases.contains(title) { return true }
                if let description = value(element,kAXDescriptionAttribute)?.lowercased(),aliases.contains(description) { return true }
            }
            var children: CFTypeRef?
            guard AXUIElementCopyAttributeValue(element,kAXChildrenAttribute as CFString,&children) == .success,
                  let items = children as? [AXUIElement] else { return false }
            return items.prefix(80).contains { hasProfile($0,depth:depth+1) }
        }
        guard let target = windows.first(where: { window in
            let title = value(window,kAXTitleAttribute)?.lowercased() ?? ""
            return aliases.contains(where: { title.hasSuffix(" - " + $0) }) || hasProfile(window,depth:0)
        }) else { return false }
        WindowSpaces.focusExisting(target) { [weak self] error in
            if let error = error { self?.onStatus?(error) }
        }
        return true
    }

    private func launchApplication(_ path: String, completion: ((Bool) -> Void)? = nil) {
        if Bundle(path:path)?.bundleIdentifier == "com.apple.finder" {
            performSystem(.finderExisting); completion?(true); return
        }
        if activateExistingChromeProfile(path) { resourceResult(.launch,path,nil); completion?(true); return }
        NSWorkspace.shared.openApplication(at: URL(fileURLWithPath: path), configuration: .init()) { [weak self] _, error in
            DispatchQueue.main.async {
                self?.resourceResult(.launch,path,error.map { "アプリを起動できません：\($0.localizedDescription)" })
                completion?(error == nil)
            }
        }
    }

    private func openURL(_ value: String) {
        guard let url = URL(string: value), ["http", "https"].contains(url.scheme?.lowercased() ?? "") else { resourceResult(.url,value,"URLを確認してください。httpまたはhttpsで始まるURLを指定します。"); return }
        resourceResult(.url,value,NSWorkspace.shared.open(url) ? nil : "URLを開けませんでした。")
    }

    private func typeText(_ value: String, target: pid_t) {
        textGeneration += 1
        let textTicket = textGeneration, ticket = generation
        guard let pairs = TextInputRouting.pairs(value,marker:marker) else { return }
        textIsRunning = true
        func step(_ index: Int) {
            guard self.enabled, self.generation == ticket, self.textGeneration == textTicket else { return }
            guard !secureInputEnabled(), AXIsProcessTrusted(),
                  NSWorkspace.shared.frontmostApplication?.processIdentifier == target else {
                self.cancelText(); return
            }
            guard index < pairs.count else { self.textIsRunning = false; return }
            let pair = pairs[index]
            for event in [pair.down, pair.up] {
                if let sink = self.verificationSink { sink(event) }
                else if pair.throughIME { event.post(tap:.cgSessionEventTap) }
                else { event.postToPid(target) }
            }
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.01) { step(index + 1) }
        }
        step(0)
    }

    private func cancelText() {
        guard textIsRunning else { return }
        textIsRunning = false; textGeneration += 1
        onStatus?("文字列入力を中断しました。入力先の変更や別の操作を検出しました。")
    }

    func restorePreviousSession() {
        guard !requested else { return }
        resumeRequested = true; recoveryBackoff.reset()
        onStatus?("一時停止：前回の有効状態を復元します。キーとボタンを離してください。")
    }

    func start() {
        guard !enabled else { return }
        resumeRequested = true
        frontmostChanged()
        guard AXIsProcessTrusted() else {
            onStatus?("一時停止：アクセシビリティの許可待ち。許可後に自動再開します。")
            return
        }
        guard !secureInputEnabled() else {
            onStatus?("一時停止：保護された入力中です。終了後に自動再開します。")
            return
        }
        let types: [CGEventType] = [Self.mediaEvent,.keyDown, .keyUp, .flagsChanged, .leftMouseDown, .leftMouseUp, .rightMouseDown, .rightMouseUp, .otherMouseDown, .otherMouseUp, .leftMouseDragged, .rightMouseDragged, .otherMouseDragged, .mouseMoved, .scrollWheel]
        let mask = types.reduce(CGEventMask(0)) { $0 | (CGEventMask(1) << $1.rawValue) }
        let context = Unmanaged.passUnretained(self).toOpaque()
        guard let port = CGEvent.tapCreate(tap: .cgSessionEventTap, place: .headInsertEventTap,
            options: .defaultTap, eventsOfInterest: mask, callback: { proxy, type, event, context in
                guard let context = context else { return Unmanaged.passUnretained(event) }
                let engine = Unmanaged<InputEngine>.fromOpaque(context).takeUnretainedValue()
                return engine.handle(proxy: proxy, type: type, event: event)
            }, userInfo: context) else {
            recoveryBackoff.failed(now:ProcessInfo.processInfo.systemUptime)
            onStatus?("一時停止：入力監視の再接続を待っています。自動で再試行します。")
            return
        }
        guard let runLoopSource = CFMachPortCreateRunLoopSource(kCFAllocatorDefault, port, 0) else {
            CFMachPortInvalidate(port)
            recoveryBackoff.failed(now:ProcessInfo.processInfo.systemUptime)
            onStatus?("一時停止：入力監視の初期化を再試行します。")
            return
        }
        tap = port
        source = runLoopSource
        CFRunLoopAddSource(CFRunLoopGetMain(), runLoopSource, .commonModes)
        bypassedPhysical = Self.heldPhysicalInputs(keyDown:{ CGEventSource.keyState(.hidSystemState,key:$0) },buttonDown:{ CGEventSource.buttonState(.hidSystemState,button:$0) })
        releaseWaitStarted = nil
        CGEvent.tapEnable(tap: port, enable: true)
        enabled = true; resumeRequested = false; recoveryBackoff.reset(); lastRecoveryNotice = nil
        onStatus?("有効：緊急停止は ⌃⌥⌘ Escape")
        configureMultitouch()
        MouseDeviceTracker.shared.onActivity = { [weak self] id in
            guard let self = self, self.machine.mouseLayer == nil else { return }
            if self.machine.mouseDeviceID != id { self.machine.mouseDeviceID = id; self.onMouseDevice?(id) }
        }
        MouseDeviceTracker.shared.start()
        configureMXMaster4()
    }

    func stop(message: String = "停止中：通常のキー入力が使えます。", preservingRequest: Bool = false) {
        finishKeyboardCleaning(discardHeld: true)
        if let observer = mxMaster4Observer { mxMaster4Observer = nil; MXMaster4HID.shared.remove(observer) }
        MouseDeviceTracker.shared.onActivity = nil; MouseDeviceTracker.shared.stop(); mousePressDevice.removeAll(); mousePressCode.removeAll()
        machine.mouseDeviceID = nil; onMouseDevice?(nil)
        multitouch.stop()
        finishTrackpadVolume()
        windowActions.cancelPending()
        let keepRequested = preservingRequest && requested
        stopMacro(); finishMacroRecording()
        endGesture(cancel:true)
        capturedPhysical.removeAll(); excludedReleases.removeAll(); bypassedPhysical.removeAll()
        enabled = false; resumeRequested = keepRequested; releaseWaitStarted = nil
        lastRecoveryNotice = nil
        if !keepRequested { recoveryBackoff.reset() }
        for input in Array(generatedMouse.keys) { endMouse(input,point:CGEvent(source:nil)?.location ?? .zero) }
        holdTimer?.invalidate(); holdTimer = nil; pendingPressEvents.removeAll()
        for original in draggedPresses.values { if let up = mouseRelease(original) { replayMouse([up]) } }
        draggedPresses.removeAll()
        generation += 1
        textGeneration += 1; textIsRunning = false
        if let tap = tap { CGEvent.tapEnable(tap: tap, enable: false) }
        if machine.isMouseDragging, let down = pendingMouse, let up = mouseRelease(down) { replayMouse([up]) }
        pendingMouse = nil
        // Release output keys before removing the tap. Our marker bypasses interception.
        for code in machine.reset() {
            if let event = CGEvent(keyboardEventSource: nil, virtualKey: code, keyDown: false) {
                event.flags = CGEventSource.flagsState(.combinedSessionState)
                event.setIntegerValueField(.eventSourceUserData, value: marker)
                replayMouse([event])
            }
        }
        pendingSpace = nil
        drainReplay() // stop runs outside the callback; finish queued Down before its cleanup Up.
        if let source = source { CFRunLoopRemoveSource(CFRunLoopGetMain(), source, .commonModes) }
        if let tap = tap { CFMachPortInvalidate(tap) }
        source = nil
        tap = nil
        onStatus?(message)
    }

    private static func heldPhysicalInputs(keyDown: (UInt16) -> Bool, buttonDown: (CGMouseButton) -> Bool) -> Set<Int64> {
        var held = Set<Int64>()
        for key in UInt16(0)..<128 where keyDown(key) { held.insert(Int64(key)) }
        for number in UInt32(0)...31 {
            if let button = CGMouseButton(rawValue:number), buttonDown(button) { held.insert(100000 + Int64(number)) }
        }
        return held
    }

    private var physicalInputReleased: Bool {
        Self.inputsReleased(flags:CGEventSource.flagsState(.hidSystemState),
                            keyDown:{ CGEventSource.keyState(.hidSystemState,key:$0) },
                            buttonDown:{ CGEventSource.buttonState(.hidSystemState,button:$0) })
    }
    private static func inputsReleased(flags: CGEventFlags, keyDown: (UInt16) -> Bool, buttonDown: (CGMouseButton) -> Bool) -> Bool {
        guard flags.intersection([.maskShift,.maskControl,.maskAlternate,.maskCommand,.maskSecondaryFn]).isEmpty else { return false }
        // Fn's virtual key can still report pressed after its modifier flag clears.
        // 0x7F is outside the assignable virtual-key range. Caps Lock is a toggle.
        for key in UInt16(0)..<127 where key != 57 && key != 63 {
            if keyDown(key) { return false }
        }
        for button in UInt32(0)...31 {
            if let button = CGMouseButton(rawValue:button), buttonDown(button) { return false }
        }
        return true
    }
    private func pause(_ reason: String) {
        stop(message:reason,preservingRequest:true)
    }
    @objc private func willSleep() { sleeping = true; if requested { pause("一時停止：スリープ中。復帰後、キーとボタンを離すと再開します。") } }
    @objc private func didWake() { sleeping = false; lastRecoveryNotice = nil; if requested { onStatus?("復帰待ち：スリープから復帰しました。入力監視を再接続します。") } }
    @objc private func sessionResigned() { inactiveSession = true; if requested { pause("一時停止：画面ロック中。ログイン後に再開します。") } }
    @objc private func sessionActive() { inactiveSession = false; lastRecoveryNotice = nil; if requested { onStatus?("復帰待ち：ログインを検出しました。入力監視を再接続します。") } }


    private func scheduleHolds() {
        let next = enabled ? machine.nextDeadline : nil
        if holdTimer?.isValid == true, scheduledHoldDeadline == next { return }
        holdTimer?.invalidate(); holdTimer = nil; scheduledHoldDeadline = next
        guard let deadline = next else { return }
        holdTimer = Timer.scheduledTimer(withTimeInterval: max(0.001, deadline - ProcessInfo.processInfo.systemUptime), repeats: false) { [weak self] _ in
            guard let self = self, self.enabled else { return }
            if self.machine.hasSpaceRepeatDeadline && self.verificationSink == nil && !CGEventSource.keyState(.hidSystemState,key:49) {
                _ = self.machine.key(49,down:false); self.pendingSpace = nil
            }
            for (mapping, modifiers) in self.machine.advance() { self.emitPulse(mapping, modifiers: modifiers) }
            self.scheduleHolds()
        }
    }
    private func emitPulse(_ mapping: Mapping, modifiers: Modifiers) {
        let mapping = mapping.resolvedWindowShortcut
        guard enabled else { return }
        recordMacroAction(mapping)
        if mapping.action == .mouse { pulseMouse(mapping,flags:modifiers.flags,point:CGEvent(source:nil)?.location ?? .zero); return }
        if mapping.action.isKey {
            guard !machine.outputIsHeld(mapping.output), let pair = NativeEvents.key(mapping.output, flags: modifiers.union(mapping.modifiers).flags, marker: marker) else { return }
            replayMouse([pair.down, pair.up]); return
        }
        if mapping.action == .system, let command = SystemCommand(rawValue: mapping.payload) {
            enqueueSystem(command, modifiers: mapping.modifiers)
            return
        }
        let ticket = generation, target = foregroundProcessID
        DispatchQueue.main.async { [weak self] in
            guard let self = self, self.enabled, self.generation == ticket else { return }
            switch mapping.action {
            case .profile: _ = self.onSwitchProfile?(mapping.payload)
            case .macro: self.playMacro(mapping.payload)
            case .window: if let command = WindowCommand(rawValue: mapping.payload) { self.performWindow(command, menuBar: mapping.layer == .menuBar, point: nil) }
            case .file: self.openFile(mapping.application)
            case .launch: self.launchApplication(mapping.application)
            case .text: if let target = target { self.typeText(mapping.payload, target: target) }
            case .url: self.openURL(mapping.payload)
            case .deck: self.onToggleDeck?(mapping.payload)
            default: break
            }
        }
    }

    private func forward(_ event: CGEvent, proxy: CGEventTapProxy?) {
        if let sink = verificationSink { sink(event) }
        else if let proxy = proxy { event.tapPostEvent(proxy) }
        else { replayMouse([event]) }
    }

    func verifyMacros() {
        verifySmoke(!enabled && tap == nil)
        let original = machine.configuration, originalForeground = foregroundProcessID
        let originalSecureInput = secureInputEnabled
        secureInputEnabled = { false }
        var events: [CGEvent] = []
        verificationSink = { events.append($0) }; enabled = true
        defer {
            secureInputEnabled = originalSecureInput
            recordedMacro = nil; recordedSteps.removeAll(); stopMacro(); drainReplay()
            enabled = false; verificationSink = nil; machine.configuration = original; foregroundProcessID = originalForeground
            onStatus?("停止中：通常のキー入力が使えます。")
        }
        let key = Mapping(layer:.normal,input:0,action:.shortcut,output:8,modifiers:.command)
        verifySmoke(executeMacroStep(MacroStep(action:key,phase:.down)))
        verifySmoke(events.isEmpty && !macroReleases.isEmpty,"Macro output deferred outside hook")
        stopMacro(); drainReplay()
        verifySmoke(events.map { $0.type } == [.flagsChanged,.keyDown,.keyUp,.flagsChanged],"Macro cancellation releases owned key and modifier")
        events.removeAll()
        let second = Mapping(layer:.normal,input:0,action:.shortcut,output:9,modifiers:.command)
        verifySmoke(executeMacroStep(MacroStep(action:key,phase:.down)))
        verifySmoke(executeMacroStep(MacroStep(action:second,phase:.down)))
        verifySmoke(executeMacroStep(MacroStep(action:key,phase:.up)))
        drainReplay(); verifySmoke(events.last?.flags.contains(.maskCommand) == true,"Overlapping key keeps Command held")
        verifySmoke(executeMacroStep(MacroStep(action:second,phase:.up)))
        drainReplay(); verifySmoke(events.last?.type == .flagsChanged && events.last?.flags.contains(.maskCommand) == false,"Last key releases shared Command")
        verifySmoke(macroModifierUps.isEmpty && macroFlagsByInput.isEmpty)
        events.removeAll()
        let fnFirst = Mapping(layer: .normal, input: 0, action: .shortcut, output: 0, modifiers: .fn)
        let fnSecond = Mapping(layer: .normal, input: 0, action: .shortcut, output: 8, modifiers: .fn)
        verifySmoke(executeMacroStep(MacroStep(action: fnFirst, phase: .down)))
        verifySmoke(executeMacroStep(MacroStep(action: fnSecond, phase: .down)))
        verifySmoke(executeMacroStep(MacroStep(action: fnFirst, phase: .up)))
        drainReplay()
        verifySmoke(events.map(\.type) == [.flagsChanged, .keyDown, .keyDown, .keyUp] &&
                    events.last?.flags.contains(.maskSecondaryFn) == true,
                    "Overlapping macro keys keep shared Fn held")
        events.removeAll()
        stop()
        verifySmoke(events.map(\.type) == [.keyUp, .flagsChanged] &&
                    events.last?.getIntegerValueField(.keyboardEventKeycode) == 63 &&
                    events.last?.flags.contains(.maskSecondaryFn) == false,
                    "Stopping the input engine releases macro-owned Fn after the last key")
        enabled = true
        events.removeAll()
        verifySmoke(executeMacroStep(MacroStep(action: fnFirst, phase: .down)))
        verificationFlagsState = { _ in .maskSecondaryFn }
        verifySmoke(executeMacroStep(MacroStep(action: fnFirst, phase: .up)))
        drainReplay()
        verificationFlagsState = nil
        verifySmoke(events.map(\.type) == [.flagsChanged, .keyDown, .keyUp] &&
                    macroModifierUps.isEmpty,
                    "Macro completion preserves a physical Fn press made during playback")
        events.removeAll()
        let mouse = Mapping(layer:.normal,input:0,action:.mouse,output:0,modifiers:.shift)
        verifySmoke(executeMacroStep(MacroStep(action:mouse,phase:.down)))
        verifySmoke(executeMacroStep(MacroStep(phase:.move,x:20,y:10)))
        verifySmoke(executeMacroStep(MacroStep(action:mouse,phase:.up)))
        drainReplay()
        verifySmoke(events.map { $0.type } == [.flagsChanged,.leftMouseDown,.leftMouseDragged,.leftMouseUp,.flagsChanged],"Macro modified drag ordering")
        verifySmoke(macroReleases.isEmpty && macroMouseButton == nil)
        events.removeAll()
        let right = Mapping(layer:.normal,input:0,action:.mouse,output:1)
        verifySmoke(executeMacroStep(MacroStep(action:mouse,phase:.down)))
        verifySmoke(executeMacroStep(MacroStep(action:right,phase:.down)))
        verifySmoke(macroMouseButtons == [0,1])
        stopMacro(); drainReplay()
        verifySmoke(events.filter { $0.type == .leftMouseUp || $0.type == .rightMouseUp }.count == 2 && macroMouseButtons.isEmpty,"Cancellation releases every held mouse button")
        events.removeAll()
        verifySmoke(executeMacroStep(MacroStep(action:Mapping(layer:.normal,input:0,action:.text,payload:"A😀B"))))
        drainReplay(); verifySmoke(events.count == 2,"Macro Unicode is a complete ordered pair")
        events.removeAll()
        var progressEvents: [MacroProgress] = []
        onMacroProgress = { progressEvents.append($0) }
        defer { onMacroProgress = nil }
        var macro = MacroDefinition(); macro.steps = [MacroStep(delayMilliseconds:600000,action:key)]
        machine.configuration.macros = [macro]; playMacro(macro.id)
        verifySmoke(macroTimer != nil && macroPlayback != nil)
        verifySmoke(progressEvents.last?.running == true && progressEvents.last?.message.contains("待機") == true,"Macro wait progress is visible before the timer fires")
        verifySmoke(progressEvents.last?.stepID == macro.steps.first?.id,"Progress identifies original editor step")
        stopMacro()
        verifySmoke(progressEvents.last?.running == false && progressEvents.last?.failed == false,"Cancellation clears running presentation")
        verifySmoke(macroTimer == nil && macroPlayback == nil && events.isEmpty,"Long wait cancels immediately")
        macro.steps = [MacroStep(delayMilliseconds:10,action:Mapping(layer:.normal,input:0,action:.key,output:8)),MacroStep(delayMilliseconds:10,action:Mapping(layer:.normal,input:0,action:.key,output:9))]
        machine.configuration.macros = [macro]; playMacro(macro.id)
        let deadline = Date().addingTimeInterval(1)
        while macroPlayback != nil && Date() < deadline { RunLoop.current.run(until:Date().addingTimeInterval(0.005)) }
        drainReplay()
        verifySmoke(macroPlayback == nil && events.filter { $0.type == .keyDown }.map { $0.getIntegerValueField(.keyboardEventKeycode) } == [8,9],"Real macro timer preserves delayed step order")
        verifySmoke(progressEvents.last?.message == "完了しました" && progressEvents.last?.running == false,"Macro completion remains available to the editor")
        events.removeAll()
        recordedMacro = macro.id; recordMapped = false; foregroundProcessID = -1; lastRecordTime = ProcessInfo.processInfo.systemUptime
        let down = CGEvent(keyboardEventSource:nil,virtualKey:0,keyDown:true)!; down.flags = []; let up = CGEvent(keyboardEventSource:nil,virtualKey:0,keyDown:false)!; up.flags = []
        recordMacroEvent(.keyDown,event:down); recordMacroEvent(.keyUp,event:up)
        verifySmoke(recordedSteps.map { $0.phase } == [.down,.up],"Physical recorder stores balanced key phases")
        recordedSteps.removeAll(); recordMapped = true
        recordMacroAction(key); verifySmoke(recordedSteps.count == 1 && recordedSteps[0].action?.output == 8,"Mapped recorder stores resolved Action")
        verifySmoke(events.isEmpty,"Recording never generates input")
        macroPlayback = MacroPlayback(steps:[],now:ProcessInfo.processInfo.systemUptime)
        playingMacroID = macro.id
        macroWindowPending = true; scheduleMacro()
        verifySmoke(macroPlayback != nil,"Last pending window action is not prematurely marked complete")
        windowOperationCompleted("検証用の配置失敗")
        verifySmoke(macroPlayback == nil && !macroWindowPending,"Failed placement stops macro and releases pending state")
        verifySmoke(progressEvents.last?.failed == true && progressEvents.last?.message.contains("検証用の配置失敗") == true,"Window failure retains its reason in macro progress")
        print("PASS: macro wait cancellation, key cleanup, modified drag, Unicode, physical and mapped recording; zero events posted")
    }

    func verifyApplicationExclusions() {
        verifySmoke(!enabled && tap == nil)
        let original = machine.configuration
        var emitted: [CGEvent] = []; verificationSink = { emitted.append($0) }
        defer {
            setApplicationInputDisabled(true); drainReplay(); applicationInputDisabled = false
            enabled = false; verificationSink = nil; capturedPhysical.removeAll(); excludedReleases.removeAll(); bypassedPhysical.removeAll()
            machine.configuration = original
        }
        machine.configuration = Configuration()
        machine.configuration.mappings = [Mapping(layer:.normal,input:0,action:.key,output:11),Mapping(layer:.space,input:0,action:.key,output:8)]
        enabled = true
        func key(_ code: UInt16,_ down: Bool) -> Bool {
            let event = CGEvent(keyboardEventSource:nil,virtualKey:code,keyDown:down)!; event.flags = []
            event.flags = []
            return handle(proxy:nil,type:event.type,event:event) == nil
        }
        verifySmoke(key(49,true))
        setApplicationInputDisabled(true)
        verifySmoke(key(49,false),"Captured Space release is consumed without inserting a space")
        verifySmoke(!key(0,true) && !key(0,false),"Excluded app passes mapped keys unchanged")
        verifySmoke(!key(102,true),"OEM key without Up remains ordinary")
        let click = CGEvent(mouseEventSource:nil,mouseType:.rightMouseDown,mouseCursorPosition:.zero,mouseButton:.right)!; click.flags = []
        click.flags = []
        verifySmoke(handle(proxy:nil,type:click.type,event:click) != nil)
        click.type = .rightMouseUp; verifySmoke(handle(proxy:nil,type:click.type,event:click) != nil)
        drainReplay(); verifySmoke(emitted.isEmpty,"Excluded app has no synthetic actions")
        setApplicationInputDisabled(false)
        verifySmoke(key(0,true) && key(0,false),"Leaving excluded app immediately restores mappings despite missing OEM Up")
        drainReplay(); verifySmoke(emitted.count == 2)
        emitted.removeAll()
        verifySmoke(key(0,true)); setApplicationInputDisabled(true); drainReplay()
        verifySmoke(emitted.map { $0.type } == [.keyDown,.keyUp],"Transition releases generated key")
        emitted.removeAll(); verifySmoke(key(0,false)); drainReplay(); verifySmoke(emitted.isEmpty)
        print("PASS: excluded applications pass keys/mouse, retain captured releases, restore immediately after missing OEM Up; zero system events")
    }

    /// Exercises the actual event routing and deferred output, with an in-memory sink.
    /// No tap is installed and no event is posted. Only invoked by isolated UI smoke mode.
    func verifyMouseRouting() {
        verifySmoke(!enabled && tap == nil && queuedReplay.isEmpty)

        let original = machine.configuration, profile = machine.runtimeProfileID
        var emitted: [CGEvent] = []
        verificationSink = { emitted.append($0) }; verificationSystemQueue = []
        defer {
            enabled = false; _ = machine.reset(); pendingMouse = nil; pendingSpace = nil
            queuedReplay.removeAll(); verificationSink = nil; verificationSystemQueue = nil; verificationFlagsState = nil
            machine.configuration = original; machine.runtimeProfileID = profile
        }
        var config = Configuration()
        config.mappings = [Mapping(layer: .mouseRight, input: 256, action: .shortcut, output: 8, modifiers: .command),
                           Mapping(layer: .mouseRight, input: 270, action: .key, output: 126)]
        machine.configuration = config; machine.runtimeProfileID = "default"; enabled = true
        func event(_ type: CGEventType, _ button: Int64, x: Double = 10) -> CGEvent {
            let e = CGEvent(mouseEventSource: nil, mouseType: type, mouseCursorPosition: CGPoint(x: x, y: 10), mouseButton: button == 0 ? .left : (button == 1 ? .right : .center))!; e.flags = []
            e.setIntegerValueField(.mouseEventButtonNumber, value: button); return e
        }
        func send(_ e: CGEvent) -> Bool { handle(proxy: nil, type: e.type, event: e) == nil }
        func cleanerKey(_ code: UInt16, _ type: CGEventType, flags: CGEventFlags = []) -> CGEvent {
            let key = CGEvent(keyboardEventSource:nil,virtualKey:code,keyDown:type != .keyUp)!
            key.type = type
            key.flags = flags
            return key
        }
        isCleaningKeyboard = true
        verifySmoke(send(cleanerKey(0,.keyDown)), "Cleaner consumes ordinary keys")
        verifySmoke(send(cleanerKey(56,.flagsChanged,flags:[.maskShift,CGEventFlags(rawValue:2)])))
        verifySmoke(send(cleanerKey(60,.flagsChanged,flags:[.maskShift,CGEventFlags(rawValue:6)])))
        verifySmoke(cleanerShiftStarted != nil, "Both Shift keys start the exit hold")
        verifySmoke(send(cleanerKey(56,.flagsChanged,flags:[.maskShift,CGEventFlags(rawValue:4)])))
        verifySmoke(cleanerShiftStarted == nil, "Releasing either Shift cancels the exit hold")
        let cleaningClick = event(.rightMouseDown,1)
        cleaningClick.flags = .maskShift
        verifySmoke(!send(cleaningClick) && !cleaningClick.flags.contains(.maskShift) && machine.mouseLayer == nil,
                    "Cleaner passes native mouse input without mappings or held modifiers")
        finishKeyboardCleaning()
        let cleaningRepeat = cleanerKey(0,.keyDown)
        cleaningRepeat.setIntegerValueField(.keyboardEventAutorepeat,value:1)
        verifySmoke(send(cleaningRepeat) && send(cleanerKey(0,.keyUp)), "Held keys remain quarantined until release")
        verifySmoke(send(cleanerKey(60,.flagsChanged)) && !send(cleanerKey(0,.keyDown)) && !send(cleanerKey(0,.keyUp)),
                    "After release, normal input resumes")
        verifySmoke(emitted.isEmpty, "Cleaner produces no synthetic keystrokes while blocking")
        for exitBeforeRelease in [false, true] {
            isCleaningKeyboard = true
            verifySmoke(send(cleanerKey(63,.flagsChanged,flags:.maskSecondaryFn)), "Cleaner consumes the Fn press")
            if exitBeforeRelease { finishKeyboardCleaning() }
            verifySmoke(!send(cleanerKey(63,.flagsChanged)), "Physical Fn release reaches the session during or after cleaning")
            verifySmoke(!cleanerHeldKeys.contains(63) && !cleanerReleaseKeys.contains(63), "Fn release clears cleaner tracking")
            finishKeyboardCleaning()
        }
        let cleanerVisibility = onKeyboardCleaning
        let cleanerFeedback = onCleanerFeedback
        var cleanerShown = false
        onKeyboardCleaning = { cleanerShown = $0 }
        onCleanerFeedback = nil
        func advanceCleanerHold() {
            for (mapping, modifiers) in machine.advance(now: ProcessInfo.processInfo.systemUptime + 2) {
                emitPulse(mapping, modifiers: modifiers)
            }
            let queued = verificationSystemQueue ?? []
            verificationSystemQueue = []
            queued.forEach { $0() }
        }
        for code: UInt16 in [111, 122] {
            machine.configuration.mappings = [
                Mapping(layer: .normal, input: code, action: .system,
                        payload: SystemCommand.keyboardCleaner.rawValue, trigger: .hold)
            ]
            verifySmoke(send(cleanerKey(code,.keyDown)) && !cleanerShown, "Cleaner honors the configured HOLD threshold")
            advanceCleanerHold()
            verifySmoke(cleanerShown && cleanerHeldKeys.contains(code), "Cleaner opens before the assigned key is released")
            if code == 122 { finishKeyboardCleaning() }
            verifySmoke(send(cleanerKey(code,.keyUp)), "Invoking key Up cannot trigger a tap or leak after exit")
            finishKeyboardCleaning()
            verifySmoke(cleanerReleaseKeys.isEmpty && machine.nextDeadline == nil)
        }
        machine.configuration.mappings = config.mappings + [
            Mapping(layer: .normal, input: 257, action: .system,
                    payload: SystemCommand.keyboardCleaner.rawValue, trigger: .hold)
        ]
        verifySmoke(send(event(.rightMouseDown,1)))
        advanceCleanerHold()
        verifySmoke(cleanerShown && machine.mouseLayer == nil, "Mouse HOLD opens the cleaner and resets its layer")
        verifySmoke(send(event(.rightMouseDragged,1)) && send(event(.rightMouseUp,1)), "Invoking button cannot leak a drag or context click")
        finishKeyboardCleaning()
        verifySmoke(excludedReleases.isEmpty && emitted.isEmpty)
        onKeyboardCleaning = cleanerVisibility
        onCleanerFeedback = cleanerFeedback
        machine.configuration = config
        let wakeHeld = Self.heldPhysicalInputs(keyDown:{ $0 == 5 || $0 == 127 },buttonDown:{ $0.rawValue == 5 })
        verifySmoke(wakeHeld == Set<Int64>([5,127,100005]),"Wake quarantines only held physical inputs including stale HID codes")
        bypassedPhysical = wakeHeld
        let heldRepeat = CGEvent(keyboardEventSource:nil,virtualKey:5,keyDown:true)!; heldRepeat.flags = []
        heldRepeat.setIntegerValueField(.keyboardEventAutorepeat,value:1)
        verifySmoke(!send(heldRepeat),"Pre-wake held key repeat passes without mapping")
        let heldUp = CGEvent(keyboardEventSource:nil,virtualKey:5,keyDown:false)!; heldUp.flags = []
        verifySmoke(!send(heldUp) && !bypassedPhysical.contains(5),"Pre-wake key Up clears its quarantine")
        bypassedPhysical.removeAll()
        let typingConfig = machine.configuration
        var typing = Configuration()
        typing.mappings = [Mapping(layer:.normal,input:5,action:.launch,application:"/Applications/Google Chrome.app",trigger:.hold,holdMilliseconds:500)]
        machine.configuration = typing
        for sequence: [(UInt16, Bool)] in [[(5,true),(0,true),(5,false),(0,false)], [(5,true),(0,true),(0,false),(5,false)], [(5,true),(5,false),(0,true),(0,false)]] {
            emitted.removeAll()
            for (code, down) in sequence {
                let e = CGEvent(keyboardEventSource:nil,virtualKey:code,keyDown:down)!; e.flags = []
                if let passed = handle(proxy:nil,type:e.type,event:e) { emitted.append(passed.takeUnretainedValue()) }
            }
            verifySmoke(emitted.map { UInt16($0.getIntegerValueField(.keyboardEventKeycode)) } == sequence.map { $0.0 },"HOLD-only G preserves native ga order, including rolling releases")
            verifySmoke(emitted.map { $0.type } == sequence.map { $0.1 ? CGEventType.keyDown : .keyUp },"Typing emits each Down/Up exactly once")
            verifySmoke(queuedReplay.isEmpty && pendingPressEvents.isEmpty && machine.nextDeadline == nil,"Typing leaves no delayed replay or launch")
            _ = machine.reset()
        }
        emitted.removeAll(); machine.configuration = typingConfig
        // Reproduce the reported physical button 5 + Space chord without posting Cmd-Q.
        let savedConfig = machine.configuration
        var chord = Configuration(); chord.forwardMouseButton = 5
        chord.mappings = [Mapping(layer:.space,input:260,action:.shortcut,output:12,modifiers:.command),Mapping(layer:.mouseForward,input:256,action:.system,payload:"desktopLeft"),Mapping(layer:.mouseForward,input:257,action:.system,payload:"desktopRight")]
        machine.configuration = chord
        for releasePrefixFirst in [false,true] {
            let spaceDown = CGEvent(keyboardEventSource:nil,virtualKey:49,keyDown:true)!; spaceDown.flags = []
            let spaceUp = CGEvent(keyboardEventSource:nil,virtualKey:49,keyDown:false)!; spaceUp.flags = []
            verifySmoke(send(spaceDown)); verifySmoke(send(event(.otherMouseDown,5)))
            if releasePrefixFirst { verifySmoke(send(spaceUp)) }
            verifySmoke(send(event(.otherMouseUp,5)))
            if !releasePrefixFirst { verifySmoke(send(spaceUp)) }
            drainReplay()
            let keys = emitted.filter { $0.type == .keyDown || $0.type == .keyUp }
            verifySmoke(keys.count == 2 && keys.map { $0.type } == [.keyDown,.keyUp])
            verifySmoke(keys.allSatisfy { $0.getIntegerValueField(.keyboardEventKeycode) == 12 && $0.flags.contains(.maskCommand) },"Space+physical Forward generates exactly one Command-Q pair")
            verifySmoke(machine.mouseLayer == nil && pendingMouse == nil)
            // No reset or unrelated click between app quit and desktop navigation.
            // Model Command retained by the session while the physical keyboard is up.
            emitted.removeAll()
            verificationFlagsState = { state in state == .combinedSessionState ? .maskCommand : [] }
            for (type, button) in [(CGEventType.rightMouseDown,Int64(1)),(.leftMouseDown,0)] {
                verifySmoke(send(event(.otherMouseDown,5)))
                verifySmoke(send(event(type,button)))
                verifySmoke(send(event(type == .rightMouseDown ? .rightMouseUp : .leftMouseUp,button)))
                verifySmoke(send(event(.otherMouseUp,5)))
                let commands = verificationSystemQueue ?? []; verificationSystemQueue = []
                commands.forEach { $0() }; drainReplay()
                let code: Int64 = type == .rightMouseDown ? 124 : 123
                verifySmoke(emitted.map { $0.getIntegerValueField(.keyboardEventKeycode) } == [59,code,code,59])
                verifySmoke(emitted.map { $0.flags.subtracting([.maskSecondaryFn,.maskNumericPad]) } == [.maskControl,.maskControl,.maskControl,[]], "Quit then desktop action must not inherit synthetic Command")
                verifySmoke(machine.mouseLayer == nil && !machine.ownsPress(257) && !machine.ownsPress(260))
                emitted.removeAll()
            }
            verificationFlagsState = { _ in .maskShift }
            performSystem(.desktopRight)
            verifySmoke(emitted.filter { $0.type == .keyDown || $0.type == .keyUp }.allSatisfy { $0.flags.contains([.maskShift,.maskControl]) }, "Preserve physically held modifiers")
            verifySmoke(emitted.last?.flags == .maskShift)
            verificationFlagsState = nil; emitted.removeAll()
        }
        machine.configuration = savedConfig
        verifySmoke(send(event(.rightMouseDown, 1)))
        verifySmoke(send(event(.rightMouseUp, 1)))
        verifySmoke(emitted.isEmpty, "replay must wait until the callback returns")
        drainReplay()
        verifySmoke(emitted.map { $0.type } == [.rightMouseDown, .rightMouseUp])
        verifySmoke(emitted.allSatisfy { $0.getIntegerValueField(.eventSourceUserData) == marker })
        emitted.removeAll()
        verifySmoke(send(event(.rightMouseDown, 1)))
        verifySmoke(send(event(.leftMouseDown, 0)))
        verifySmoke(send(event(.rightMouseUp, 1)))
        verifySmoke(send(event(.leftMouseUp, 0)))
        drainReplay()
        verifySmoke(emitted.map { $0.type } == [.keyDown, .keyUp])
        verifySmoke(emitted.allSatisfy { $0.getIntegerValueField(.keyboardEventKeycode) == 8 && $0.flags.contains(.maskCommand) })
        emitted.removeAll()
        verifySmoke(send(event(.rightMouseDown, 1)))
        verifySmoke(send(event(.rightMouseDragged, 1, x: 20)))
        verifySmoke(send(event(.rightMouseUp, 1, x: 20)))
        drainReplay()
        verifySmoke(emitted.map { $0.type } == [.rightMouseDown, .rightMouseDragged, .rightMouseUp])
        emitted.removeAll()
        verifySmoke(send(event(.rightMouseDown, 1)))
        verifySmoke(send(event(.leftMouseDown, 0)))
        stop()
        verifySmoke(emitted.map { $0.type } == [.keyDown, .keyUp], "stop drains queued Down before cleanup Up")
        verifySmoke(!enabled && queuedReplay.isEmpty)
        for (layer, button) in [(Layer.mouseBack, Int64(3)), (.mouseForward, Int64(4))] {
            emitted.removeAll(); enabled = true
            machine.configuration.mappings = [Mapping(layer: layer, input: 0, action: .key, output: 11)]
            verifySmoke(send(event(.otherMouseDown, button)))
            verifySmoke(send(event(.otherMouseUp, button)))
            drainReplay()
            let commands = verificationSystemQueue ?? []; verificationSystemQueue = []; commands.forEach { $0() }
            verifySmoke(emitted.map { $0.type } == [.flagsChanged,.keyDown,.keyUp,.flagsChanged], "Unused side layer performs navigation")
            verifySmoke(emitted[1].getIntegerValueField(.keyboardEventKeycode) == (button == 3 ? 30 : 42))
        }
        emitted.removeAll(); enabled = true; machine.configuration = config
        verifySmoke(send(event(.rightMouseDown, 1)))
        let wheel = CGEvent(scrollWheelEvent2Source: nil, units: .line, wheelCount: 1, wheel1: 1, wheel2: 0, wheel3: 0)!; wheel.flags = []
        verifySmoke(send(wheel)); verifySmoke(send(event(.rightMouseUp, 1)))
        drainReplay()
        verifySmoke(emitted.map { $0.type } == [.keyDown, .keyUp])
        verifySmoke(emitted.allSatisfy { $0.getIntegerValueField(.keyboardEventKeycode) == 126 })
        for amount: Int32 in [1,3,8,-1,-6] {
            emitted.removeAll(); _ = machine.reset()
            machine.configuration.mappings = [
                Mapping(layer:.mouseRight,input:270,action:.system,modifiers:.control,payload:"scrollUp"),
                Mapping(layer:.mouseRight,input:271,action:.system,modifiers:.control,payload:"scrollDown")]
            verifySmoke(send(event(.rightMouseDown,1)))
            let native = CGEvent(scrollWheelEvent2Source:nil,units:.line,wheelCount:1,wheel1:amount,wheel2:0,wheel3:0)!; native.flags = []
            native.flags = []
            native.setIntegerValueField(.scrollWheelEventPointDeltaAxis1,value:Int64(amount)*10)
            native.setDoubleValueField(.scrollWheelEventFixedPtDeltaAxis1,value:Double(amount)*1.25)
            verifySmoke(send(native)); verifySmoke(send(event(.rightMouseUp,1))); drainReplay()
            verifySmoke(emitted.count == 1 && emitted[0].type == .scrollWheel,"Layer wheel does not replay right click or modifier key edges")
            verifySmoke(emitted[0].flags == .maskControl,"Wheel Control flags: \(emitted[0].flags.rawValue)")
            for field: CGEventField in [.scrollWheelEventDeltaAxis1,.scrollWheelEventPointDeltaAxis1] {
                verifySmoke(emitted[0].getIntegerValueField(field) == native.getIntegerValueField(field),"Layer preserves native wheel magnitude")
            }
            verifySmoke(emitted[0].getDoubleValueField(.scrollWheelEventFixedPtDeltaAxis1) == native.getDoubleValueField(.scrollWheelEventFixedPtDeltaAxis1))
        }
        machine.configuration = config
        emitted.removeAll()
        machine.configuration.mappings.append(Mapping(layer: .normal, input: 257, action: .key, output: 11))
        verifySmoke(send(event(.rightMouseDown, 1))); verifySmoke(send(event(.rightMouseUp, 1)))
        drainReplay()
        verifySmoke(emitted.map { $0.type } == [.keyDown, .keyUp])
        verifySmoke(emitted.allSatisfy { $0.getIntegerValueField(.keyboardEventKeycode) == 11 }, "source TAP retains normal assignment")
        emitted.removeAll(); _ = machine.reset(); machine.configuration = Configuration(); enabled = true
        let hold = Mapping(layer: .normal, input: 0, action: .key, output: 11, trigger: .hold)
        machine.configuration.mappings = [hold]
        func key(_ code: UInt16, down: Bool) -> CGEvent { let event = CGEvent(keyboardEventSource:nil,virtualKey:code,keyDown:down)!; event.flags = []; return event }
        verifySmoke(send(key(0, down: true)))
        let shortUp = key(0, down: false)
        verifySmoke(!send(shortUp), "Short keyboard HOLD restores Down inline and passes physical Up")
        emitted.append(shortUp)
        drainReplay(); verifySmoke(emitted.map { $0.type } == [.keyDown,.keyUp])
        verifySmoke(emitted.allSatisfy { $0.getIntegerValueField(.keyboardEventKeycode) == 0 }, "HOLD-only short press restores original pair")
        emitted.removeAll()
        verifySmoke(!send(key(0,down:true)),"Typing-priority Down passes immediately through native handler")
        verifySmoke(!send(key(0,down:false)),"Typing-priority Up passes immediately through native handler")
        drainReplay(); verifySmoke(emitted.isEmpty && machine.nextDeadline == nil,"Typing-priority emits no replacement events or HOLD timer")
        _ = machine.reset() // Start an isolated press, outside the typing session.
        verifySmoke(send(key(0, down: true)))
        for (action, flags) in machine.advance(now: ProcessInfo.processInfo.systemUptime + 1) { emitPulse(action, modifiers: flags) }
        verifySmoke(send(key(0, down: false))); drainReplay()
        verifySmoke(emitted.count == 2 && emitted.allSatisfy { $0.getIntegerValueField(.keyboardEventKeycode) == 11 }, "HOLD generates one complete key pair")
        emitted.removeAll(); machine.configuration.mappings = [Mapping(layer: .normal, input: 257, action: .key, output: 11, trigger: .hold)]
        verifySmoke(send(event(.rightMouseDown, 1))); verifySmoke(send(event(.rightMouseDragged, 1, x: 20)))
        verifySmoke(send(event(.rightMouseUp, 1, x: 20))); drainReplay()
        verifySmoke(emitted.map { $0.type } == [.rightMouseDown,.rightMouseDragged,.rightMouseUp], "drag cancels pending HOLD without losing native button")
        emitted.removeAll(); machine.configuration.mappings = [Mapping(layer: .capsLock, input: 0, action: .key, output: 11)]
        verifySmoke(send(key(105, down: true))); verifySmoke(send(key(0, down: true)))
        verifySmoke(send(key(105, down: false))); verifySmoke(send(key(0, down: false))); drainReplay()
        verifySmoke(emitted.count == 2 && emitted.allSatisfy { $0.getIntegerValueField(.keyboardEventKeycode) == 11 }, "F13 chord does not leak source key")
        emitted.removeAll(); _ = machine.reset()
        machine.configuration.mappings = [Mapping(layer:.control,input:38,action:.shortcut,output:44,modifiers:.shift)]
        let controlJ = key(38,down:true); controlJ.flags = .maskControl
        verifySmoke(send(controlJ))
        verifySmoke(send(key(38,down:false))); drainReplay()
        verifySmoke(emitted.count == 2 && emitted.allSatisfy {
            $0.getIntegerValueField(.keyboardEventKeycode) == 44 && $0.flags.contains(.maskShift) && !$0.flags.contains(.maskControl)
        }, "Control layer strips only source modifier on both output edges")
        emitted.removeAll()
        machine.configuration.mappings = [Mapping(layer:.control,input:38,action:.shortcut,output:18,modifiers:.shift)]
        let punctuationDown = key(38,down:true); punctuationDown.flags = .maskControl
        let punctuationUp = key(38,down:false); punctuationUp.flags = .maskControl
        verifySmoke(send(punctuationDown) && send(punctuationUp)); drainReplay()
        verifySmoke(emitted.count == 2 && emitted.map { $0.type } == [.keyDown,.keyUp])
        verifySmoke(emitted.allSatisfy { $0.getIntegerValueField(.keyboardEventKeycode) == 18 && $0.flags == .maskShift },"Control-J punctuation uses Shift-1 without Control, Enter or Unicode injection")
        emitted.removeAll()
        let controlC = key(8,down:true); controlC.flags = .maskControl
        verifySmoke(!send(controlC) && controlC.flags.contains(.maskControl), "Unassigned Control shortcut preserved")
        verifySmoke(!send(key(8,down:false)))
        _ = machine.reset()
        for command in [SystemCommand.scrollUp,.scrollDown,.scrollLeft,.scrollRight] {
            emitted.removeAll(); performSystem(command,modifiers:[.command,.shift]); drainReplay()
            verifySmoke(emitted.count == 1 && emitted[0].type == .scrollWheel,"Modified wheel emits only a wheel event")
            verifySmoke(emitted[0].flags == [.maskCommand,.maskShift],"Wheel carries exact selected modifiers without held-key state")
            let axis: CGEventField = [.scrollLeft,.scrollRight].contains(command) ? .scrollWheelEventDeltaAxis2 : .scrollWheelEventDeltaAxis1
            verifySmoke(abs(emitted[0].getIntegerValueField(axis)) == 1,"Modified wheel uses a single line for fine control")
        }
        for command in SystemCommand.allCases where command.shortcut != nil {
            emitted.removeAll()
            performSystem(command); drainReplay()
            let (code, flags) = command.shortcut(layout:machine.configuration.keyboardLayout)!
            let keys = emitted.filter { $0.type == .keyDown || $0.type == .keyUp }
            verifySmoke(keys.count == 2 && keys.map { $0.type } == [.keyDown,.keyUp])
            verifySmoke(keys.allSatisfy { $0.getIntegerValueField(.keyboardEventKeycode) == code && $0.flags.subtracting([.maskSecondaryFn,.maskNumericPad]) == flags.flags }, "OS Action uses complete key pair")
            verifySmoke(emitted.first?.type == .flagsChanged && emitted.last?.type == .flagsChanged && emitted.last?.flags == [], "OS Action releases owned modifiers")
        }
        for command: SystemCommand in [.volumeUp,.volumeDown,.mute,.brightnessUp,.brightnessDown] {
            emitted.removeAll(); verificationSystemLocations.removeAll()
            enqueueSystem(command)
            verifySmoke(emitted.isEmpty,"Volume/brightness waits for hook return")
            let queued = verificationSystemQueue ?? []; verificationSystemQueue = []; queued.forEach { $0() }
            verifySmoke(emitted.count == 2 && verificationSystemLocations == [.cghidEventTap,.cghidEventTap],"Media keys use system HID route, not device master volume")
            let expected = MacSystemEvents.mediaKey(command)!
            for (index,event) in emitted.enumerated() {
                let native = NSEvent(cgEvent:event)!
                verifySmoke(native.type == .systemDefined && native.subtype.rawValue == 8)
                verifySmoke((native.data1 >> 16) == expected && ((native.data1 >> 8) & 0xff) == (index == 0 ? 0xA : 0xB),"Media code and down/up edges")
                verifySmoke(event.getIntegerValueField(.eventSourceUserData) == marker)
            }
        }
        print("PASS: volume/mute/brightness deferred native media-key routing and HUD request; no real volume or brightness changes")
        emitted.removeAll()
        verificationSystemLocations.removeAll(); verificationSystemQueue = []
        machine.configuration.mappings = [Mapping(layer:.mouseForward,input:256,action:.system,payload:"desktopLeft")]
        verifySmoke(send(event(.otherMouseDown,4)))
        verifySmoke(send(event(.leftMouseDown,0)))
        verifySmoke(emitted.isEmpty, "Desktop action waits for callback return")
        verifySmoke(send(event(.otherMouseUp,4)))
        verifySmoke(send(event(.leftMouseUp,0)))
        let queuedCommands = verificationSystemQueue ?? []; verificationSystemQueue = []; queuedCommands.forEach { $0() }
        verifySmoke(emitted.map { $0.getIntegerValueField(.keyboardEventKeycode) } == [59,123,123,59], "Control Down, Left Down, Left Up, Control Up")
        verifySmoke(emitted.map { $0.flags.subtracting([.maskSecondaryFn,.maskNumericPad]) } == [.maskControl,.maskControl,.maskControl,[]], "Control held for both arrow edges")
        verifySmoke(verificationSystemLocations == Array(repeating:.cghidEventTap,count:4), "OS shortcut uses HID entry")
        verifySmoke(machine.mouseLayer == nil && !machine.ownsPress(256), "Desktop action leaves no captured button")
        for output in emitted { verifySmoke(!send(output), "Generated OS shortcut bypasses own hook") }
        for hardwareButton in [3,7] {
            emitted.removeAll(); _ = machine.reset()
            machine.configuration.registerMouseButton(hardwareButton,forward:true)
            machine.configuration.mappings = [
                Mapping(layer:.mouseForward,input:256,action:.system,payload:"desktopLeft"),
                Mapping(layer:.mouseForward,input:257,action:.system,payload:"desktopRight"),
                Mapping(layer:.mouseRight,input:0,action:.key,output:11)]
            verifySmoke(send(event(.otherMouseDown,Int64(hardwareButton))))
            verifySmoke(send(event(.leftMouseDown,0))); verifySmoke(send(event(.leftMouseUp,0)))
            verifySmoke(send(event(.rightMouseDown,1))); verifySmoke(send(event(.rightMouseUp,1)))
            verifySmoke(send(event(.otherMouseUp,Int64(hardwareButton))))
            let commands = verificationSystemQueue ?? []; verificationSystemQueue = []; commands.forEach { $0() }
            verifySmoke(emitted.map { $0.getIntegerValueField(.keyboardEventKeycode) } == [59,123,123,59,59,124,124,59], "Registered Forward supports left and right clicks without right-layer theft")
            verifySmoke(machine.mouseLayer == nil && !machine.ownsPress(256) && !machine.ownsPress(257), "Both children released")
        }
        emitted.removeAll(); _ = machine.reset()
        machine.configuration.forwardMouseButton = 5
        machine.configuration.backMouseButton = 6
        machine.configuration.registerMouseButton(3, forward: true, deviceID: "046d:b042")
        machine.configuration.mappings = [
            Mapping(layer:.mouseForward,input:256,action:.key,output:8),
            Mapping(layer:.normal,input:303,action:.key,output:51)
        ]
        var testMouse = "046d:b042"
        verificationMouseDevice = { _ in testMouse }
        verifySmoke(send(event(.otherMouseDown,3)) && machine.mouseLayer == .mouseForward)
        testMouse = "ist"
        verifySmoke(send(event(.leftMouseDown,0))); verifySmoke(send(event(.leftMouseUp,0)))
        verifySmoke(send(event(.otherMouseUp,3)) && machine.mouseLayer == nil,
                    "Changing mice before Up keeps the invoking button's identity")
        verifySmoke(send(event(.otherMouseDown,3)) && machine.mouseLayer == nil)
        verifySmoke(send(event(.otherMouseUp,3)))
        verifySmoke(send(event(.otherMouseDown,5)) && machine.mouseLayer == .mouseForward)
        verifySmoke(send(event(.leftMouseDown,0))); verifySmoke(send(event(.leftMouseUp,0)))
        verifySmoke(send(event(.otherMouseUp,5)) && machine.mouseLayer == nil)
        drainReplay()
        verifySmoke(emitted.map { $0.getIntegerValueField(.keyboardEventKeycode) } == [8,8,51,51,8,8],
                    "Each mouse uses its own Forward button and keeps the other mouse's ordinary button action")
        verificationMouseDevice = nil
        machine.mouseDeviceID = nil
        machine.configuration.mouseLayerButtonsByDevice = nil
        machine.configuration.mappings = [Mapping(layer:.mouseForward,input:256,action:.system,payload:"desktopLeft")]
        machine.configuration.forwardMouseButton = nil; machine.configuration.backMouseButton = nil
        emitted.removeAll()
        verifySmoke(send(event(.otherMouseDown,4))); verifySmoke(send(event(.leftMouseDown,0)))
        stop()
        let cancelledCommands = verificationSystemQueue ?? []; verificationSystemQueue = []; cancelledCommands.forEach { $0() }
        verifySmoke(emitted.isEmpty, "Stop cancels desktop action before dispatch")
        enabled = true
        machine.configuration.mappings = []
        machine.configuration.reversedWheelLayers = [.normal]
        let discrete = CGEvent(scrollWheelEvent2Source:nil,units:.line,wheelCount:1,wheel1:3,wheel2:0,wheel3:0)!; discrete.flags = []
        discrete.setIntegerValueField(.scrollWheelEventIsContinuous,value:0)
        verifySmoke(!send(discrete) && discrete.getIntegerValueField(.scrollWheelEventDeltaAxis1) == -3, "Enabled ordinary wheel reverses without injection")
        let trackpad = CGEvent(scrollWheelEvent2Source:nil,units:.pixel,wheelCount:1,wheel1:30,wheel2:0,wheel3:0)!; trackpad.flags = []
        trackpad.setIntegerValueField(.scrollWheelEventIsContinuous,value:1)
        let originalScroll = trackpad.getIntegerValueField(.scrollWheelEventPointDeltaAxis1)
        verifySmoke(!send(trackpad) && trackpad.getIntegerValueField(.scrollWheelEventPointDeltaAxis1) == originalScroll, "Trackpad preserved through actual routing")
        machine.configuration.mappings = [Mapping(layer:.normal,input:272,action:.key,output:126),Mapping(layer:.normal,input:273,action:.key,output:125)]
        for horizontal in [Int32(-20),20] {
            let swipe = CGEvent(scrollWheelEvent2Source:nil,units:.pixel,wheelCount:2,wheel1:0,wheel2:horizontal,wheel3:0)!; swipe.flags = []
            swipe.setIntegerValueField(.scrollWheelEventIsContinuous,value:1)
            emitted.removeAll(); verifySmoke(!send(swipe),"Two-finger horizontal swipe bypasses assigned tilt")
            drainReplay(); verifySmoke(emitted.isEmpty,"Trackpad tilt bypass emits no assigned action")
        }
        let momentum = CGEvent(scrollWheelEvent2Source:nil,units:.line,wheelCount:2,wheel1:0,wheel2:1,wheel3:0)!; momentum.flags = []
        momentum.setIntegerValueField(.scrollWheelEventMomentumPhase,value:1)
        verifySmoke(!send(momentum),"Momentum cannot invoke tilt even without continuous flag")
        drainReplay(); verifySmoke(emitted.isEmpty,"Momentum creates no mapped output")
        let tilt = CGEvent(scrollWheelEvent2Source:nil,units:.line,wheelCount:2,wheel1:0,wheel2:1,wheel3:0)!; tilt.flags = []
        tilt.setIntegerValueField(.scrollWheelEventIsContinuous,value:0)
        emitted.removeAll(); verifySmoke(send(tilt),"Discrete mouse tilt still executes its mapping")
        drainReplay(); verifySmoke(emitted.filter { $0.type == .keyDown }.map { $0.getIntegerValueField(.keyboardEventKeycode) } == [126],"Scroll reversal never swaps assigned tilt actions")
        emitted.removeAll(); machine.configuration.mappings = []

        machine.configuration.reversedWheelLayers = nil
        verifySmoke(Self.inputsReleased(flags:[],keyDown:{ $0 == 127 },buttonDown:{ _ in false }),"Reserved key 127 cannot block wake recovery")
        verifySmoke(!Self.inputsReleased(flags:[],keyDown:{ $0 == 49 },buttonDown:{ _ in false }),"Held Space still blocks recovery")
        verifySmoke(!Self.inputsReleased(flags:.maskCommand,keyDown:{ _ in false },buttonDown:{ _ in false }),"Held modifier still blocks recovery")
        verifySmoke(Self.inputsReleased(flags:[],keyDown:{ $0 == 63 },buttonDown:{ _ in false }),"Stale Fn key state cannot block recovery when its flag has cleared")
        verifySmoke(!Self.inputsReleased(flags:.maskSecondaryFn,keyDown:{ _ in false },buttonDown:{ _ in false }),"Actively held Fn still blocks recovery")
        verifySmoke(!Self.inputsReleased(flags:[],keyDown:{ _ in false },buttonDown:{ $0.rawValue == 5 }),"Held extra mouse button still blocks recovery")
        let savedRegions = menuBarRegions
        menuBarRegions = [CGRect(x:0,y:0,width:1000,height:24)]
        machine.configuration.mappings = [Mapping(layer:.menuBar,input:270,action:.key,output:126)]
        let menuSwipe = CGEvent(scrollWheelEvent2Source:nil,units:.pixel,wheelCount:1,wheel1:12,wheel2:0,wheel3:0)!; menuSwipe.flags = []
        menuSwipe.location = CGPoint(x:50,y:10); menuSwipe.timestamp = 1_000_000_000
        menuSwipe.setIntegerValueField(.scrollWheelEventIsContinuous,value:1)
        verifySmoke(!send(menuSwipe),"Menu trackpad remains opt-in")
        machine.configuration.menuBarTrackpadEnabled = true
        verifySmoke(send(menuSwipe),"Enabled menu trackpad invokes menu assignment")
        drainReplay(); verifySmoke(emitted.filter { $0.type == .keyDown }.count == 1)
        emitted.removeAll(); menuSwipe.timestamp += 1_000_000
        verifySmoke(send(menuSwipe)); drainReplay(); verifySmoke(emitted.isEmpty,"Trackpad burst is rate limited")
        menuSwipe.location = CGPoint(x:50,y:100)
        verifySmoke(!send(menuSwipe),"Menu opt-in never enables trackpad outside menu bar")
        menuSwipe.location = CGPoint(x:50,y:10)
        menuSwipe.setIntegerValueField(.scrollWheelEventMomentumPhase,value:1)
        verifySmoke(send(menuSwipe)); drainReplay(); verifySmoke(emitted.isEmpty,"Menu momentum does not keep changing volume")
        // Real trackpad events can have no whole-line motion. The old route discarded these.
        menuSwipe.setIntegerValueField(.scrollWheelEventMomentumPhase,value:0)
        machine.configuration.mappings = [Mapping(layer:.menuBar,input:270,action:.key,output:126), Mapping(layer:.menuBar,input:271,action:.key,output:125)]
        for direction in [1.0,-1.0] { for fractionalOnly in [false,true] {
            menuSwipe.timestamp += 100_000_000
            menuSwipe.setIntegerValueField(.scrollWheelEventDeltaAxis1,value:0)
            menuSwipe.setIntegerValueField(.scrollWheelEventPointDeltaAxis1,value:fractionalOnly ? 0 : Int64(direction*3))
            menuSwipe.setDoubleValueField(.scrollWheelEventFixedPtDeltaAxis1,value:direction*0.25)
            verifySmoke(menuSwipe.getIntegerValueField(.scrollWheelEventDeltaAxis1) == 0,"Regression fixture has zero whole-line delta")
            emitted.removeAll(); verifySmoke(send(menuSwipe),"Pixel/fractional menu scroll invokes action")
            drainReplay()
            verifySmoke(emitted.filter { $0.type == .keyDown && $0.getIntegerValueField(.keyboardEventKeycode) == (direction > 0 ? 126 : 125) }.count == 1,"Precise menu scroll emits exactly one action in correct direction")
        } }
        emitted.removeAll()
        machine.configuration.menuBarTrackpadEnabled = nil; lastTrackpadMenuPulse = nil
        machine.configuration.mappings = [Mapping(layer:.trackpad,input:270,action:.key,output:126)]
        menuSwipe.location = CGPoint(x:50,y:100)
        menuSwipe.setIntegerValueField(.scrollWheelEventPointDeltaAxis1,value:20)
        menuSwipe.setDoubleValueField(.scrollWheelEventFixedPtDeltaAxis1,value:1)
        menuSwipe.timestamp += 100_000_000
        emitted.removeAll(); verifySmoke(send(menuSwipe),"Dedicated trackpad mapping outside menu bar")
        drainReplay(); verifySmoke(emitted.filter { $0.type == .keyDown }.count == 1,"Trackpad emits one action")
        menuSwipe.setIntegerValueField(.scrollWheelEventMomentumPhase,value:1)
        emitted.removeAll(); verifySmoke(send(menuSwipe),"Mapped trackpad inertia consumed")
        drainReplay(); verifySmoke(emitted.isEmpty,"Trackpad inertia emits no action")
        menuSwipe.setIntegerValueField(.scrollWheelEventMomentumPhase,value:0)
        menuSwipe.setIntegerValueField(.scrollWheelEventIsContinuous,value:0)
        verifySmoke(!send(menuSwipe),"Discrete mouse wheel cannot invoke trackpad mapping")
        lastTrackpadMenuPulse = nil


        machine.configuration.mappings = [
            Mapping(layer: .menuBar,input:257,action:.key,output:11,trigger:.hold),
            Mapping(layer: .mouseRight,input:0,action:.key,output:8)]
        verifySmoke(!send(event(.leftMouseDown,0)) && !send(event(.leftMouseUp,0)), "menu bar left click untouched")
        verifySmoke(send(event(.rightMouseDown,1)))
        verifySmoke(machine.mouseLayer == nil, "menu bar HOLD precedes ordinary mouse layer")
        verifySmoke(send(event(.rightMouseUp,1)))
        verifySmoke(emitted.isEmpty, "short click waits for callback return")
        stop()
        verifySmoke(emitted.map { $0.type } == [.rightMouseDown,.rightMouseUp], "stop preserves queued menu bar click restoration")
        emitted.removeAll(); enabled = true
        verifySmoke(send(event(.rightMouseDown,1)))
        for (action, flags) in machine.advance(now: ProcessInfo.processInfo.systemUptime + 1) { emitPulse(action,modifiers:flags) }
        verifySmoke(send(event(.rightMouseUp,1))); drainReplay()
        verifySmoke(emitted.count == 2 && emitted.allSatisfy { $0.getIntegerValueField(.keyboardEventKeycode) == 11 }, "menu bar HOLD executes once")
        // Exact reported Space + left click route, including release order variations.
        for sourceFirst in [true,false] {
            emitted.removeAll(); _ = machine.reset(); pendingSpace = nil
            machine.configuration = Configuration()
            machine.configuration.mappings = [Mapping(layer:.space,input:256,action:.system,payload:"desktopLeft")]
            enabled = true; verificationSystemQueue = []
            verifySmoke(send(key(49,down:true))); verifySmoke(send(event(.leftMouseDown,0)))
            verifySmoke(emitted.isEmpty)
            if sourceFirst { verifySmoke(send(key(49,down:false))) }
            verifySmoke(send(event(.leftMouseUp,0)))
            if !sourceFirst { verifySmoke(send(key(49,down:false))) }
            let commands = verificationSystemQueue ?? []; verificationSystemQueue = []; commands.forEach { $0() }; drainReplay()
            verifySmoke(emitted.count == 4 && emitted[1].getIntegerValueField(.keyboardEventKeycode) == 123)
            verifySmoke(emitted[1].flags.contains([.maskControl,.maskSecondaryFn,.maskNumericPad]), "Arrow identity retained for WindowServer")
        }
        // Modifier clicks: short pairs, drag movement, release after configuration changes,
        // and stop before the asynchronous output queue runs.
        for flags in [Modifiers.shift,.control,.command,.option] {
            for stopEarly in [false,true] {
                emitted.removeAll(); _ = machine.reset(); machine.configuration = Configuration(); enabled = true
                machine.configuration.mappings = [Mapping(layer:.space,input:256,action:.mouse,output:0,modifiers:flags)]
                verifySmoke(send(key(49,down:true))); verifySmoke(send(event(.leftMouseDown,0)))
                verifySmoke(emitted.isEmpty, "Modifier Down must wait until the input callback returns")
                if stopEarly { stop() }
                else {
                    verifySmoke(send(event(.leftMouseDragged,0,x:40)))
                    machine.configuration.mappings = []
                    verifySmoke(send(key(49,down:false))); verifySmoke(send(event(.leftMouseUp,0,x:50)))
                    drainReplay()
                }
                let expected: [CGEventType] = stopEarly ? [.flagsChanged,.leftMouseDown,.leftMouseUp,.flagsChanged] : [.flagsChanged,.leftMouseDown,.leftMouseDragged,.leftMouseUp,.flagsChanged]
                verifySmoke(emitted.map { $0.type } == expected, "Modifier-click and drag release order")
                verifySmoke(generatedMouse.isEmpty && emitted.last?.flags.intersection(flags.flags).isEmpty == true)
            }
        }
        emitted.removeAll(); _ = machine.reset(); machine.configuration = Configuration(); enabled = true
        machine.configuration.mappings = [Mapping(layer:.normal,input:305,action:.mouse,output:0,modifiers:.shift)]
        verifySmoke(send(event(.otherMouseDown,5))); verifySmoke(send(event(.otherMouseUp,5)))
        verifySmoke(emitted.isEmpty, "A very short modified click remains queued as a full pair")
        drainReplay()
        verifySmoke(emitted.map { $0.type } == [.flagsChanged,.leftMouseDown,.leftMouseUp,.flagsChanged])
        verifySmoke(generatedMouse.isEmpty && generatedMousePhysical.isEmpty)
        emitted.removeAll(); _ = machine.reset(); machine.configuration.mappings = [Mapping(layer:.normal,input:11,action:.mouse,output:0,modifiers:.command)]
        verifySmoke(send(key(11,down:true))); verifySmoke(send(event(.mouseMoved,0,x:30))); verifySmoke(send(key(11,down:false)))
        drainReplay()
        verifySmoke(emitted.map { $0.type } == [.flagsChanged,.leftMouseDown,.leftMouseDragged,.leftMouseUp,.flagsChanged], "Keyboard-triggered modifier drag retains movement ordering")
        // Pause is resumable only if input was enabled; explicit stop cancels that intent.
        enabled = false; resumeRequested = false
        willSleep(); verifySmoke(!requested); didWake()
        emitted.removeAll(); _ = machine.reset(); machine.configuration = Configuration(); enabled = true
        var gesture = GestureDefinition()
        gesture.actions["center"] = Mapping(layer:.normal,input:0,action:.key,output:8)
        gesture.actions["right"] = Mapping(layer:.normal,input:0,action:.key,output:9)
        machine.configuration.gestures = [gesture]
        machine.configuration.mappings = [Mapping(layer:.normal,input:257,action:.gesture,payload:gesture.id)]
        var cursorConnections: [Bool] = []; var cursorPoints: [CGPoint] = []
        verificationCursorAssociation = { cursorConnections.append($0); return .success }
        verificationCursorWarp = { cursorPoints.append($0); return .success }
        verifySmoke(send(event(.rightMouseDown,1)))
        verifySmoke(cursorConnections == [false] && gestureCursorAnchor != nil,"Locked gesture disconnects cursor before motion")
        verifySmoke(gestureSession != nil)
        verifySmoke(send(event(.rightMouseUp,1))); drainReplay()
        verifySmoke(gestureSession == nil && emitted.map { $0.type } == [.keyDown,.keyUp])
        verifySmoke(cursorConnections == [false,true] && gestureCursorAnchor == nil,"Release reconnects cursor before final action")
        verifySmoke(emitted.first?.getIntegerValueField(.keyboardEventKeycode) == 8)
        emitted.removeAll()
        verifySmoke(send(event(.rightMouseDown,1)))
        let motion = event(.rightMouseDragged,1,x:30); motion.setIntegerValueField(.mouseEventDeltaX,value:20)
        let anchor = gestureCursorAnchor!
        verifySmoke(send(motion),"Locked gesture consumes movement")
        verifySmoke(cursorPoints.last == anchor,"Locked movement restores starting position")
        machine.configuration.gestures[0].threshold = 90
        verifySmoke(send(event(.rightMouseUp,1))); drainReplay()
        verifySmoke(emitted.count == 2 && emitted[0].getIntegerValueField(.keyboardEventKeycode) == 9,"Release commits captured gesture definition")
        emitted.removeAll(); machine.configuration.gestures[0].locksCursor = false
        cursorConnections.removeAll(); cursorPoints.removeAll()
        verifySmoke(send(event(.rightMouseDown,1)))
        verifySmoke(!send(motion) && motion.type == .mouseMoved,"Unlocked gesture allows cursor without native drag")
        verifySmoke(cursorConnections.isEmpty && cursorPoints.isEmpty,"Unlocked gesture never changes cursor association or position")
        stop(); verifySmoke(gestureSession == nil && gestureTimer == nil)
        machine.configuration.gestures[0].locksCursor = true
        startGesture(gesture.id,source:257,point:CGPoint(x:100,y:100))
        stop(); verifySmoke(gestureCursorAnchor == nil && cursorConnections == [false,true],"Stop releases locked cursor")
        cursorConnections.removeAll()
        verificationCursorWarp = { _ in .failure }
        startGesture(gesture.id,source:257,point:.zero)
        verifySmoke(gestureSession == nil && gestureCursorAnchor == nil && cursorConnections == [false,true],"Failed anchor rolls back association")
        cursorConnections.removeAll()
        verificationCursorWarp = { cursorPoints.append($0); return .success }
        enabled = true
        updateTrackpadVolumeCursor()
        updateTrackpadVolumeCursor()
        verifySmoke(cursorConnections == [false] && trackpadVolumeCursorAnchor != nil, "Volume gesture locks the cursor once")
        verifySmoke(send(event(.mouseMoved,0,x:40)) && cursorPoints.last == trackpadVolumeCursorAnchor)
        finishTrackpadVolume()
        verifySmoke(cursorConnections == [false,true] && trackpadVolumeWatchdog == nil, "Volume gesture release restores pointer movement")
        updateTrackpadVolumeCursor()
        stop()
        verifySmoke(cursorConnections == [false,true,false,true] && trackpadVolumeCursorAnchor == nil, "Engine stop also releases the volume gesture lock")
        verificationCursorAssociation = nil; verificationCursorWarp = nil
        drainReplay(); verifySmoke(emitted.isEmpty,"Stop cancels gesture without center output")
        enabled = true; willSleep(); verifySmoke(!enabled && resumeRequested)
        didWake(); verifySmoke(resumeRequested && !sleeping)
        stop(); verifySmoke(!requested)
        enabled = true; sessionResigned(); verifySmoke(resumeRequested && inactiveSession)
        sessionActive(); verifySmoke(resumeRequested && !inactiveSession)
        stop(); recentTimeouts.removeAll()
        let savedStatus = onStatus
        var requests: [Bool] = []
        onStatus = { [weak self] _ in requests.append(self?.requested ?? false) }
        enabled = true; pause("一時停止：検証")
        verifySmoke(requested && !enabled && !requests.contains(false),"Pause must never persist manual-stop intent")
        for _ in 0..<5 {
            let event = CGEvent(source:nil)!; event.flags = []
            _ = handle(proxy:nil,type:.tapDisabledByTimeout,event:event)
            recoverTapInterruption(timeout:true,ticket:generation)
            verifySmoke(requested,"Repeated tap timeout remains eligible for reconnect")
        }
        verifySmoke(recoveryBackoff.failures > 0)
        let staleTicket = generation
        stop(); verifySmoke(!requested,"Manual stop cancels reconnect")
        _ = handle(proxy:nil,type:.tapDisabledByTimeout,event:CGEvent(source:nil)!)
        recoverTapInterruption(timeout:true,ticket:staleTicket)
        verifySmoke(!requested,"Stale timeout cannot override manual stop")
        var notices: [String] = []
        onStatus = { notices.append($0) }; lastRecoveryNotice = nil
        recoveryNotice("fixture"); recoveryNotice("fixture")
        verifySmoke(notices == ["fixture"],"Recovery notice is deduplicated")
        stop(); notices.removeAll(); didWake(); sessionActive()
        verifySmoke(notices.isEmpty && !requested,"Wake/login cannot override an explicit manual stop")
        openFile("/RELYR-verification-missing-file-55E25")
        verifySmoke(notices.last?.contains("ファイルが見つかりません") == true,"Missing file reports an actionable error without opening an app")
        onStatus = savedStatus; recentTimeouts.removeAll()
        menuBarRegions = savedRegions
        holdTimer?.invalidate(); holdTimer = nil
        print("PASS: native mouse replay, drag ordering, layer child release, gesture center/final direction/snapshot/lock/stop; zero events posted")
    }

    private func recoverTapInterruption(timeout: Bool, ticket: Int) {
        guard resumeRequested, generation == ticket else { return }
        if timeout {
            let now = ProcessInfo.processInfo.systemUptime
            recentTimeouts = recentTimeouts.filter { now - $0 < 10 } + [now]
            recoveryBackoff.failed(now:now)
        }
        pause(timeout ? "一時停止：入力監視の遅延から復旧中。キーとボタンを離すと再開します。" : "一時停止：macOSによる入力監視の中断。キーとボタンを離すと再開します。")
    }

    private static let mediaEvent = CGEventType(rawValue: 14)!
    private static let cleanerLog = Logger(subsystem: "io.github.zitan-source.relyr.mac.trial", category: "KeyboardCleaner")
    private var cleanerTimer: Timer?
    private var isCleaningKeyboard = false
    private var cleanerHeldKeys: Set<UInt16> = []
    private var cleanerHeldMedia: Set<Int> = []
    private var cleanerReleaseKeys: Set<UInt16> = []
    private var cleanerReleaseMedia: Set<Int> = []
    private var cleanerShiftStarted: TimeInterval?
    var onKeyboardCleaning: ((Bool) -> Void)?
    var onCleanerFeedback: ((Set<UInt16>, Double) -> Void)?

    private var keyboardTapAvailable: Bool {
        guard enabled, !secureInputEnabled() else { return false }
        if verificationSink != nil { return true }
        return tap.map { CGEvent.tapIsEnabled(tap: $0) } == true && AXIsProcessTrusted()
    }

    private func requestKeyboardCleaning() {
        guard !isCleaningKeyboard, keyboardTapAvailable else { return }

        // Capture ownership before resetting the layers. The invoking key can
        // still be down, as can Space, a modifier, or the mouse layer button.
        var physical = verificationSink == nil
            ? Set((UInt16(0)..<128).filter { $0 != 57 && $0 != 63 && CGEventSource.keyState(.hidSystemState, key: $0) })
            : []
        if systemPhysicalFlags().contains(.maskSecondaryFn) { physical.insert(63) }
        let captured = Set(capturedPhysical.union(excludedReleases).compactMap {
            (0..<128).contains($0) ? UInt16($0) : nil
        })
        let alreadyBlocked = captured.union(cleanerReleaseKeys)
        cleanerHeldKeys = physical.union(captured)
        cancelMappedInput()
        excludedReleases = excludedReleases.filter { $0 >= 100000 }
        bypassedPhysical = bypassedPhysical.filter { $0 >= 100000 }

        // Complete pre-existing native presses before suppressing their real Up.
        // Synthetic outputs owned by RELYR were released by cancelMappedInput().
        let capsFlag = systemPhysicalFlags().intersection(.maskAlphaShift)
        let releases = physical.subtracting(alreadyBlocked).sorted().compactMap { code -> CGEvent? in
            guard let event = CGEvent(keyboardEventSource: nil, virtualKey: code, keyDown: false) else { return nil }
            if [54,55,56,58,59,60,61,62,63].contains(code) { event.type = .flagsChanged }
            event.flags = capsFlag
            return event
        }
        replayMouse(releases)
        drainReplay()
        multitouch.reset()
        isCleaningKeyboard = true
        if verificationSink == nil {
            Self.cleanerLog.notice("Cleaning started. Fn held at entry: \(physical.contains(63), privacy: .public)")
        }
        onKeyboardCleaning?(true)
        let timer = Timer(timeInterval: 1.0/30, repeats: true) { [weak self] _ in self?.updateKeyboardCleaner() }
        cleanerTimer = timer
        RunLoop.main.add(timer, forMode: .common)
        updateKeyboardCleaner()
    }

    private func updateKeyboardCleaner() {
        guard keyboardTapAvailable else {
            finishKeyboardCleaning()
            return
        }
        let now = ProcessInfo.processInfo.systemUptime
        let progress = cleanerShiftStarted.map { min(1, (now - $0)/3) } ?? 0
        if progress >= 1 { finishKeyboardCleaning(); return }
        onCleanerFeedback?(cleanerHeldKeys, progress)
    }

    func finishKeyboardCleaning(discardHeld: Bool = false) {
        cleanerTimer?.invalidate()
        cleanerTimer = nil
        if isCleaningKeyboard, verificationSink == nil {
            Self.cleanerLog.notice("Cleaning ended. Fn held at exit: \(self.cleanerHeldKeys.contains(63), privacy: .public)")
        }
        if discardHeld {
            cleanerReleaseKeys.removeAll()
            cleanerReleaseMedia.removeAll()
        } else {
            cleanerReleaseKeys.formUnion(cleanerHeldKeys)
            cleanerReleaseMedia.formUnion(cleanerHeldMedia)
        }
        cleanerHeldKeys.removeAll()
        cleanerHeldMedia.removeAll()
        cleanerShiftStarted = nil
        guard isCleaningKeyboard else { return }
        isCleaningKeyboard = false
        multitouch.reset()
        onKeyboardCleaning?(false)
    }

    private func filterCleanerInput(_ type: CGEventType, _ event: CGEvent) -> Bool {
        if type == Self.mediaEvent {
            guard isCleaningKeyboard || !cleanerReleaseMedia.isEmpty,
                  let native = NSEvent(cgEvent: event), native.subtype.rawValue == 8 else { return false }
            let code = (native.data1 >> 16) & 0xffff
            let down = (native.data1 >> 8) & 0xff == 0x0a
            if isCleaningKeyboard {
                if down { cleanerHeldMedia.insert(code) } else { cleanerHeldMedia.remove(code); cleanerReleaseMedia.remove(code) }
                return true
            }
            if down, native.data1 & 1 == 0 { cleanerReleaseMedia.remove(code); return false }
            return down ? cleanerReleaseMedia.contains(code) : cleanerReleaseMedia.remove(code) != nil
        }
        guard type == .keyDown || type == .keyUp || type == .flagsChanged else { return false }
        let key = UInt16(clamping: event.getIntegerValueField(.keyboardEventKeycode))
        let down: Bool
        if type == .flagsChanged {
            // Side-specific device bits distinguish a released Shift from the other held Shift.
            let mask: UInt64
            switch key {
            case 56: mask = 0x02
            case 60: mask = 0x04
            case 59: mask = 0x01
            case 62: mask = 0x2000
            case 58: mask = 0x20
            case 61: mask = 0x40
            case 55: mask = 0x08
            case 54: mask = 0x10
            case 63: mask = CGEventFlags.maskSecondaryFn.rawValue
            default: return isCleaningKeyboard // Caps Lock is a toggle.
            }
            down = event.flags.rawValue & mask != 0
        } else {
            down = type == .keyDown
        }
        if isCleaningKeyboard {
            if down { cleanerHeldKeys.insert(key) } else { cleanerHeldKeys.remove(key); cleanerReleaseKeys.remove(key) }
            if type == .flagsChanged && (key == 56 || key == 60) {
                if cleanerHeldKeys.contains(56) && cleanerHeldKeys.contains(60) {
                    if cleanerShiftStarted == nil { cleanerShiftStarted = ProcessInfo.processInfo.systemUptime }
                } else {
                    cleanerShiftStarted = nil
                }
            }
            return true
        }
        guard cleanerReleaseKeys.contains(key) else { return false }
        if !down { cleanerReleaseKeys.remove(key); return true }
        if type == .keyDown && event.getIntegerValueField(.keyboardEventAutorepeat) != 0 { return true }
        cleanerReleaseKeys.remove(key) // A fresh Down can recover a missing Up.
        return false
    }

    private func handle(proxy: CGEventTapProxy?, type: CGEventType, event: CGEvent) -> Unmanaged<CGEvent>? {
        guard enabled, event.getIntegerValueField(.eventSourceUserData) != marker,
              type != .tapDisabledByTimeout, type != .tapDisabledByUserInput else { return processEvent(proxy:proxy,type:type,event:event) }
        if isCleaningKeyboard || !cleanerReleaseKeys.isEmpty || !cleanerReleaseMedia.isEmpty {
            // WindowServer needs the physical Globe/Fn release even when the
            // cleaner consumed the press or a synthetic release was queued.
            if (type == .keyUp || (type == .flagsChanged && !event.flags.contains(.maskSecondaryFn))),
               event.getIntegerValueField(.keyboardEventKeycode) == 63 {
                cleanerHeldKeys.remove(63)
                cleanerReleaseKeys.remove(63)
                return Unmanaged.passUnretained(event)
            }
            if filterCleanerInput(type, event) { return nil }
            if !cleanerReleaseKeys.isEmpty {
                // Held modifiers must not modify the first click or key after exit.
                let groups: [(CGEventFlags, UInt64, UInt16, UInt16)] = [
                    (.maskShift, 0x06, 56, 60), (.maskControl, 0x2001, 59, 62),
                    (.maskAlternate, 0x60, 58, 61), (.maskCommand, 0x18, 55, 54),
                    (.maskSecondaryFn, 0, 63, 63)
                ]
                for (flag, deviceBits, left, right) in groups where cleanerReleaseKeys.contains(left) || cleanerReleaseKeys.contains(right) {
                    event.flags.subtract(CGEventFlags(rawValue: flag.rawValue | deviceBits))
                }
            }
            if isCleaningKeyboard {
                let heldModifiers: CGEventFlags = [.maskShift,.maskControl,.maskAlternate,.maskCommand,.maskSecondaryFn,.maskAlphaShift]
                event.flags.subtract(heldModifiers.union(CGEventFlags(rawValue: 0x207f)))
                let button = event.getIntegerValueField(.mouseEventButtonNumber)
                let identity = 100000 + button
                switch type {
                case .leftMouseDown,.rightMouseDown,.otherMouseDown:
                    excludedReleases.remove(identity)
                    bypassedPhysical.insert(identity)
                case .leftMouseUp,.rightMouseUp,.otherMouseUp:
                    mousePressDevice.removeValue(forKey: button)
                    mousePressCode.removeValue(forKey: button)
                    bypassedPhysical.remove(identity)
                    if excludedReleases.remove(identity) != nil { return nil }
                case .leftMouseDragged,.rightMouseDragged,.otherMouseDragged:
                    if excludedReleases.contains(identity) { return nil }
                default: break
                }
                return Unmanaged.passUnretained(event)
            }
        }
        if type == Self.mediaEvent { return Unmanaged.passUnretained(event) }
        var identity: Int64?
        var releasedMouseButton: Int64?
        defer {
            if let button = releasedMouseButton {
                mousePressDevice.removeValue(forKey: button)
                mousePressCode.removeValue(forKey: button)
            }
        }
        var down = false
        if type == .keyDown || type == .keyUp {
            identity = event.getIntegerValueField(.keyboardEventKeycode); down = type == .keyDown
        } else if [.leftMouseDown,.leftMouseUp,.rightMouseDown,.rightMouseUp,.otherMouseDown,.otherMouseUp].contains(type) {
            identity = 100000 + event.getIntegerValueField(.mouseEventButtonNumber)
            down = [.leftMouseDown,.rightMouseDown,.otherMouseDown].contains(type)
            let button = event.getIntegerValueField(.mouseEventButtonNumber)
            let deviceID: String?
            if !down {
                deviceID = mousePressDevice[button]
            } else if button == 32 {
                deviceID = "046d:b042"
            } else if let identify = verificationMouseDevice {
                deviceID = identify(Int(button))
            } else {
                deviceID = MouseDeviceTracker.shared.device(forButton: Int(button))
            }
            if down {
                mousePressDevice[button] = deviceID
                mousePressCode[button] = machine.configuration.mouseInput(button, deviceID: deviceID)
            }
            if machine.mouseLayer == nil, let deviceID = deviceID, machine.mouseDeviceID != deviceID {
                machine.mouseDeviceID = deviceID; onMouseDevice?(deviceID)
            }
            if !down {
                // Keep the press identity available through the release handler.
                releasedMouseButton = button
            }
        } else if type == .flagsChanged {
            let key = event.getIntegerValueField(.keyboardEventKeycode)
            let masks: [Int64:CGEventFlags] = [59:.maskControl,62:.maskControl,56:.maskShift,60:.maskShift,58:.maskAlternate,61:.maskAlternate,55:.maskCommand,54:.maskCommand]
            if let mask = masks[key] { identity = key; down = event.flags.contains(mask) }
        }
        if let identity = identity {
            let freshDown = down && (type != .keyDown || event.getIntegerValueField(.keyboardEventAutorepeat) == 0)
            if freshDown { excludedReleases.remove(identity); bypassedPhysical.remove(identity) }
            if excludedReleases.contains(identity) {
                if !down { excludedReleases.remove(identity) }
                return nil
            }
            if bypassedPhysical.contains(identity) {
                if !down { bypassedPhysical.remove(identity) }
                return Unmanaged.passUnretained(event)
            }
            if applicationInputDisabled {
                if down { bypassedPhysical.insert(identity) }
                return Unmanaged.passUnretained(event)
            }
        } else if applicationInputDisabled { return Unmanaged.passUnretained(event) }
        let result = processEvent(proxy:proxy,type:type,event:event)
        if let identity = identity {
            if down && result == nil { capturedPhysical.insert(identity) }
            if !down { capturedPhysical.remove(identity) }
        }
        return result
    }

    private func processEvent(proxy: CGEventTapProxy?, type: CGEventType, event: CGEvent) -> Unmanaged<CGEvent>? {
        if type == .tapDisabledByTimeout || type == .tapDisabledByUserInput {
            let timeout = type == .tapDisabledByTimeout
            // Defer cleanup until the callback returns. Recovery starts with empty state
            // and waits until every physical key/button is released.
            let ticket = generation
            resumeRequested = requested; enabled = false
            DispatchQueue.main.async { [weak self] in
                self?.recoverTapInterruption(timeout:timeout,ticket:ticket)
            }
            return Unmanaged.passUnretained(event)
        }
        guard enabled, event.getIntegerValueField(.eventSourceUserData) != marker else { return Unmanaged.passUnretained(event) }
        if (macroPlayback != nil || recordedMacro != nil), type == .keyDown, event.getIntegerValueField(.keyboardEventKeycode) == 111, Modifiers(event.flags).isSuperset(of:[.control,.shift]) {
            DispatchQueue.main.async { [weak self] in self?.stopMacro(); self?.finishMacroRecording(); self?.onStatus?("マクロを停止しました。") }
            return nil
        }
        if !recordMapped { recordMacroEvent(type,event:event) }
        else if recordMovement && [.mouseMoved,.leftMouseDragged,.rightMouseDragged,.otherMouseDragged].contains(type) { recordMacroEvent(type,event:event) }
        if type == .flagsChanged || type == .leftMouseDown || type == .rightMouseDown || type == .otherMouseDown ||
            (type == .keyDown && event.getIntegerValueField(.keyboardEventAutorepeat) == 0) {
            // Defer UI notification until the event callback returns.
            if textIsRunning {
                textIsRunning = false; textGeneration += 1
                DispatchQueue.main.async { [weak self] in self?.onStatus?("別の操作を検出したため文字列入力を中断しました。") }
            }
        }
        if let anchor = trackpadVolumeCursorAnchor {
            if type == .mouseMoved {
                if warpGestureCursor(anchor) != .success {
                    multitouch.reset()
                    finishTrackpadVolume()
                }
                return nil
            }
            if type == .leftMouseDown || type == .rightMouseDown || type == .otherMouseDown {
                multitouch.reset()
                finishTrackpadVolume()
            }
        }
        if var session = gestureSession {
            let releaseCode: UInt16? = type == .keyUp ? UInt16(clamping:event.getIntegerValueField(.keyboardEventKeycode)) :
                ([.leftMouseUp,.rightMouseUp,.otherMouseUp].contains(type) ? mouseCode(for: event.getIntegerValueField(.mouseEventButtonNumber)) : nil)
            if releaseCode == session.source { endGesture() }
            else if [.mouseMoved,.leftMouseDragged,.rightMouseDragged,.otherMouseDragged].contains(type) {
                session.move(x:Double(event.getIntegerValueField(.mouseEventDeltaX)),y:Double(event.getIntegerValueField(.mouseEventDeltaY)),now:ProcessInfo.processInfo.systemUptime)
                gestureSession = session
                if let anchor = gestureCursorAnchor {
                    // Session-tap suppression alone does not stop WindowServer's cursor.
                    // Preserve relative deltas above, and keep the visible pointer anchored.
                    if warpGestureCursor(anchor) != .success { endGesture(cancel:true) }
                    return nil
                }
                event.type = .mouseMoved
                return Unmanaged.passUnretained(event)
            }
        }
        if [.mouseMoved,.leftMouseDragged,.rightMouseDragged,.otherMouseDragged].contains(type), let press = generatedMouse.values.first {
            if let movement = event.copy() {
                movement.type = press.button == 0 ? .leftMouseDragged : (press.button == 1 ? .rightMouseDragged : .otherMouseDragged)
                movement.setIntegerValueField(.mouseEventButtonNumber,value:Int64(press.button)); movement.flags = event.flags.union(press.flags)
                replayMouse([movement])
            }
            return nil
        }
        var code = UInt16(clamping: event.getIntegerValueField(.keyboardEventKeycode))
        let modifiers = Modifiers(event.flags)
        if type == .keyDown, code == 53, modifiers.isSuperset(of: [.control, .option, .command]) {
            enabled = false
            DispatchQueue.main.async { [weak self] in self?.stop(message: "緊急停止しました。") }
            return nil
        }
        if type == .mouseMoved { return Unmanaged.passUnretained(event) }
        for (mapping, flags) in machine.advance() { emitPulse(mapping, modifiers: flags) }
        defer { scheduleHolds() }
        var isDown = type == .keyDown
        let mouseDown = type == .leftMouseDown || type == .rightMouseDown || type == .otherMouseDown
        let mouseUp = type == .leftMouseUp || type == .rightMouseUp || type == .otherMouseUp
        if type == .leftMouseDragged || type == .rightMouseDragged || type == .otherMouseDragged {
            guard let dragCode = mouseCode(for: event.getIntegerValueField(.mouseEventButtonNumber)) else { return Unmanaged.passUnretained(event) }
            if draggedPresses[dragCode] != nil {
                if let movement = event.copy() { replayMouse([movement]) }; return nil
            }
            if let original = pendingPressEvents[dragCode] {
                let dx = event.location.x - original.location.x, dy = event.location.y - original.location.y
                if dx * dx + dy * dy >= 16, machine.cancelDeferredForDrag(dragCode) {
                    pendingPressEvents.removeValue(forKey: dragCode); draggedPresses[dragCode] = original
                    if let movement = event.copy() { replayMouse([original, movement]) }; return nil
                }
            }
            if machine.ownsPress(dragCode) { event.type = .mouseMoved; return Unmanaged.passUnretained(event) }
        }
        if type == .rightMouseDragged || type == .otherMouseDragged, let original = pendingMouse,
           event.getIntegerValueField(.mouseEventButtonNumber) == original.getIntegerValueField(.mouseEventButtonNumber) {
            let dx = event.location.x - original.location.x, dy = event.location.y - original.location.y
            if machine.isMouseDragging || (dx * dx + dy * dy >= 16 && machine.beginMouseDrag()) {
                guard let movement = event.copy() else { return nil }
                if !mouseDragStarted { mouseDragStarted = true; replayMouse([original, movement]) }
                else { replayMouse([movement]) }
                return nil
            }
            // A layer action owns the button, but pointer motion remains ordinary motion.
            event.type = .mouseMoved
            return Unmanaged.passUnretained(event)
        }
        var sourceTap = false
        var qualifiedLayer: Layer? = type == .keyDown ? menuBarLayer(code,at:event.location) : nil
        if mouseDown || mouseUp {
            let button = type == .leftMouseDown || type == .leftMouseUp ? 0 : (type == .rightMouseDown || type == .rightMouseUp ? 1 : event.getIntegerValueField(.mouseEventButtonNumber))
            guard let mappedCode = mouseCode(for: button) else { return Unmanaged.passUnretained(event) }
            code = mappedCode; isDown = mouseDown
            if mouseUp, draggedPresses.removeValue(forKey: code) != nil {
                _ = machine.key(code, down: false)
                if let up = event.copy() { replayMouse([up]) }; return nil
            }
            guard let retainedEvent = event.copy() else { return Unmanaged.passUnretained(event) }
            if mouseDown { qualifiedLayer = menuBarLayer(code, at: event.location) }
            let sourceDecision: LayerMachine.MouseDecision = qualifiedLayer != nil || machine.ownsPress(code) ? .ordinary : machine.mouseSource(code, down: mouseDown, modifiers: modifiers)
            switch sourceDecision {
            case .ordinary: break
            case .consume:
                if mouseDown, pendingMouse == nil {
                    pendingMouse = retainedEvent; mouseDragStarted = false
                    switch machine.flushPendingSpace() {
                    case .spaceDownThenPass: if let down = pendingSpace { replayMouse([down]); pendingSpace = nil }
                    case .layerDownThenPass(let code): if let down = pendingPressEvents.removeValue(forKey: code) { replayMouse([down]) }
                    default: break
                    }
                }
                if mouseUp { pendingMouse = nil; mouseDragStarted = false }
                return nil
            case .replayClick:
                if code == 259 || code == 260 || machine.configuration.mappings(for: machine.runtimeProfileID).contains(where: { $0.layer == .normal && $0.input == code && $0.trigger == .tap }) {
                    sourceTap = true; isDown = true
                } else if let original = pendingMouse {
                    recordMacroAction(Mapping(layer:.normal,input:0,action:.mouse,output:UInt16(clamping:original.getIntegerValueField(.mouseEventButtonNumber)),modifiers:modifiers))
                    replayMouse([original, retainedEvent])
                }
                pendingMouse = nil; mouseDragStarted = false
                if !sourceTap { return nil }
            case .endDrag:
                if let up = event.copy() { replayMouse([up]) }
                pendingMouse = nil; mouseDragStarted = false
                return nil
            }
        }
        let wheel = type == .scrollWheel
        if wheel {
            // Trackpad gestures and inertia are not physical wheel/tilt bindings.
            // Keep their original event, phase and deltas; do not consume a layer.
            if NativeEvents.isContinuousScroll(event) {
                guard let input = NativeEvents.scrollInput(event,invertedFromDevice:NSEvent(cgEvent:event)?.isDirectionInvertedFromDevice ?? false) else { return Unmanaged.passUnretained(event) }
                if multitouch.reservesScroll(input) { return Unmanaged.passUnretained(event) }
                if machine.configuration.menuBarTrackpadEnabled == true, menuBarLayer(input,at:event.location) == .menuBar {
                    qualifiedLayer = .menuBar
                } else if machine.configuration.mappings(for:machine.runtimeProfileID).contains(where:{ $0.layer == .trackpad && $0.input == input && $0.trigger == .tap }) {
                    qualifiedLayer = .trackpad
                } else { return Unmanaged.passUnretained(event) }
                // Momentum must not continue changing volume after the fingers leave.
                if event.getIntegerValueField(.scrollWheelEventMomentumPhase) != 0 { return nil }
                let time = event.timestamp
                if let last = lastTrackpadMenuPulse, last.input == input, time >= last.time, time-last.time < 80_000_000 { return nil }
                lastTrackpadMenuPulse = (time,input)
            }
            guard let input = qualifiedLayer == .trackpad ? NativeEvents.scrollInput(event,invertedFromDevice:NSEvent(cgEvent:event)?.isDirectionInvertedFromDevice ?? false) : wheelInput(event) else { return Unmanaged.passUnretained(event) }
            code = input
            isDown = true
            qualifiedLayer = qualifiedLayer ?? menuBarLayer(code, at: event.location)
        }
        if type == .keyDown, event.getIntegerValueField(.keyboardEventAutorepeat) == 0 {
            for source in machine.releaseTypingHolds(before: code) {
                if let original = pendingPressEvents.removeValue(forKey: source) {
                    forward(original, proxy: proxy)
                }
            }
        }
        let stripLayerControl = machine.usesControlLayer(code, modifiers: modifiers)
        let decision: InputDecision
        if type == .keyDown || type == .keyUp || mouseDown || mouseUp || wheel {
            if code == 105, type == .keyDown, event.getIntegerValueField(.keyboardEventAutorepeat) == 0 { pendingPressEvents[code] = event.copy() }
            if code == 49, type == .keyDown, event.getIntegerValueField(.keyboardEventAutorepeat) == 0 {
                pendingSpace = event.copy()
            }
            decision = machine.key(code, down: isDown,
                repeating: event.getIntegerValueField(.keyboardEventAutorepeat) != 0, modifiers: modifiers, allowHold: !sourceTap, qualifiedLayer: qualifiedLayer)
        } else {
            decision = machine.flushPendingSpace()
        }
        if recordMapped {
            let phase: MacroPhase = wheel || sourceTap ? .pulse : (isDown ? .down : .up)
            var recorded: Mapping?
            switch decision {
            case .remap(let output,let flags):
                let combined = flags.union(stripLayerControl ? modifiers.subtracting(.control) : modifiers)
                recorded = Mapping(layer:.normal,input:0,action:combined.isEmpty ? .key : .shortcut,output:output,modifiers:combined)
            case .mouse(let button,let flags): recorded = Mapping(layer:.normal,input:0,action:.mouse,output:button,modifiers:flags)
            case .system(let command,let flags): recorded = Mapping(layer:.normal,input:0,action:.system,modifiers:flags,payload:command.rawValue)
            case .window(let command,_): recorded = Mapping(layer:.normal,input:0,action:.window,payload:command.rawValue)
            case .text(let text): recorded = Mapping(layer:.normal,input:0,action:.text,payload:text)
            case .launch(let path): recorded = Mapping(layer:.normal,input:0,action:.launch,application:path)
            case .file(let path): recorded = Mapping(layer:.normal,input:0,action:.file,application:path)
            case .url(let url): recorded = Mapping(layer:.normal,input:0,action:.url,payload:url)
            case .deck(let id): recorded = Mapping(layer:.normal,input:0,action:.deck,payload:id)
            case .profile(let id): recorded = Mapping(layer:.normal,input:0,action:.profile,payload:id)
            case .macro(let id): recorded = Mapping(layer:.normal,input:0,action:.macro,payload:id)
            case .spaceTap: recorded = Mapping(layer:.normal,input:0,action:.key,output:49)
            case .spaceDownThenPass:
                recordMacroAction(Mapping(layer:.normal,input:0,action:.key,output:49),phase:.down); recordMacroEvent(type,event:event)
            case .pass: recordMacroEvent(type,event:event)
            default: break
            }
            if let recorded = recorded { recordMacroAction(recorded,phase:decision == .spaceTap ? .pulse : (recorded.action.isKey || recorded.action == .mouse ? phase : .pulse)) }
        }
        let wheelRelease = (wheel || sourceTap) ? machine.key(code, down: false) : .pass
        if isDown && decision != .pass && decision != .consume && decision != .spaceDownThenPass && machine.isMouseDragging {
            if let original = pendingMouse, let up = mouseRelease(original) { replayMouse([up]) }
            machine.endMouseDrag(); mouseDragStarted = false
        }
        switch decision {
        case .profile(let id):
            let ticket = generation
            DispatchQueue.main.async { [weak self] in guard let self = self, self.enabled, self.generation == ticket else { return }; _ = self.onSwitchProfile?(id) }; return nil
        case .macro(let id):
            let ticket = generation
            DispatchQueue.main.async { [weak self] in guard let self = self, self.enabled, self.generation == ticket else { return }; self.playMacro(id) }; return nil
        case .gesture(let id): startGesture(id,source:code,point:event.location); return nil
        case .deferPress:
            pendingPressEvents[code] = event.copy()
            return nil
        case .pulse(let mapping, let flags):
            pendingPressEvents.removeValue(forKey: code)
            emitPulse(mapping, modifiers: flags)
            return nil
        case .restoreTap(let sourceCode):
            if let original = pendingPressEvents.removeValue(forKey: sourceCode) {
                if type == .keyUp {
                    // Preserve keyboard order in this tap, before later physical keys.
                    forward(original, proxy: proxy)
                    return Unmanaged.passUnretained(event)
                }
                if let up = event.copy() { replayMouse([original, up]) }
            }
            return nil
        case .layerDownThenPass(let sourceCode):
            if let original = pendingPressEvents.removeValue(forKey: sourceCode) { forward(original, proxy: proxy) }
            return Unmanaged.passUnretained(event)
        case .pass:
            if wheel && machine.configuration.reversesMouseScroll { NativeEvents.reverseDiscreteScroll(event) }
            if !isDown { pendingPressEvents.removeValue(forKey: code) }
            if code == 49, type == .keyUp { pendingSpace = nil }
            return Unmanaged.passUnretained(event)
        case .consume:
            if !isDown { pendingPressEvents.removeValue(forKey: code) }
            if code == 49, type == .keyUp { pendingSpace = nil }
            return nil
        case .spaceDownThenPass:
            if let pendingSpace = pendingSpace { forward(pendingSpace, proxy: proxy) }
            pendingSpace = nil
            return Unmanaged.passUnretained(event)
        case .spaceTap:
            if let pendingSpace = pendingSpace { forward(pendingSpace, proxy: proxy) }
            pendingSpace = nil
            return Unmanaged.passUnretained(event) // physical Space Up follows the restored Down
        case .mouse(let button, let flags):
            if isDown {
                let physical = stripLayerControl ? event.flags.subtracting(.maskControl) : event.flags
                if beginMouse(code,button:button,flags:flags.flags,physical:physical,point:event.location), mouseDown, code != 340 { generatedMousePhysical[code] = event.getIntegerValueField(.mouseEventButtonNumber) }
                if wheel || sourceTap { endMouse(code,point:event.location) }
            } else { endMouse(code,point:event.location) }
            return nil
        case .remap(let output, let addedModifiers):
            guard let replacement = CGEvent(keyboardEventSource: nil, virtualKey: output, keyDown: isDown) else {
                enabled = false
                DispatchQueue.main.async { [weak self] in self?.pause("一時停止：入力イベントを生成できませんでした。自動で再接続します。") }
                return Unmanaged.passUnretained(event)
            }
            replacement.flags = (stripLayerControl ? event.flags.subtracting(.maskControl) : event.flags).union(addedModifiers.flags)
            replacement.timestamp = event.timestamp
            replacement.setIntegerValueField(.keyboardEventAutorepeat, value: event.getIntegerValueField(.keyboardEventAutorepeat))
            replacement.setIntegerValueField(.eventSourceUserData, value: marker)
            if mouseDown || mouseUp || wheel {
                var events = [replacement]
                if case .remap(let releaseCode, let releaseModifiers) = wheelRelease,
                   let up = CGEvent(keyboardEventSource: nil, virtualKey: releaseCode, keyDown: false) {
                    up.flags = event.flags.union(releaseModifiers.flags); events.append(up)
                }
                replayMouse(events)
            } else { forward(replacement, proxy: proxy) }
            return nil
        case .system(let command,let flags):
            let scrollTargets: [SystemCommand:UInt16] = [.scrollUp:270,.scrollDown:271,.scrollLeft:272,.scrollRight:273]
            if wheel, let output = scrollTargets[command],
               let replacement = NativeEvents.mappedScroll(event,input:code,output:output,
                   flags:(stripLayerControl ? event.flags.subtracting(.maskControl) : event.flags).union(flags.flags),marker:marker) {
                replayMouse([replacement])
                return nil
            }
            enqueueSystem(command,modifiers:flags)
            return nil
        case .window(let command, let menuBar):
            let ticket = generation, point = event.location
            DispatchQueue.main.async { [weak self] in
                guard let self = self, self.enabled, self.generation == ticket else { return }
                self.performWindow(command, menuBar: menuBar, point: point)
            }
            return nil
        case .file(let path):
            let ticket = generation
            DispatchQueue.main.async { [weak self] in
                guard let self = self, self.enabled, self.generation == ticket else { return }
                self.openFile(path)
            }
            return nil
        case .launch(let path):
            let ticket = generation
            DispatchQueue.main.async { [weak self] in
                guard let self = self, self.enabled, self.generation == ticket else { return }
                self.launchApplication(path)
            }
            return nil
        case .text(let value):
            let ticket = generation
            // Session taps precede event target annotation. Use the workspace observer's cached PID,
            // then verify it outside the callback before sending any text.
            let target = foregroundProcessID
            DispatchQueue.main.async { [weak self] in
                guard let self = self, self.enabled, self.generation == ticket,
                      let target = target, NSWorkspace.shared.frontmostApplication?.processIdentifier == target else { return }
                self.typeText(value, target: target)
            }
            return nil
        case .url(let value):
            let ticket = generation
            DispatchQueue.main.async { [weak self] in
                guard let self = self, self.enabled, self.generation == ticket else { return }
                self.openURL(value)
            }
            return nil
        case .deck(let id):
            let ticket = generation
            DispatchQueue.main.async { [weak self] in
                guard let self = self, self.enabled, self.generation == ticket else { return }
                self.onToggleDeck?(id)
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
        if contains(.fn) { value.insert(.maskSecondaryFn) }
        return value
    }
}
