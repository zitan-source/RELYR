import Foundation
import IOKit.hid

/// The wheel-mode key is a HID++ control, not a macOS OtherMouse button.
/// Divert only CID 0x00c4 while a RELYR binding/capture needs it. No persistent
/// mouse settings are written, and the firmware regains the key on release.
final class MXMaster4HID {
    static let shared = MXMaster4HID()
    private let manager = IOHIDManagerCreate(kCFAllocatorDefault, IOOptionBits(kIOHIDOptionsTypeNone))
    private let reportBuffer = UnsafeMutablePointer<UInt8>.allocate(capacity: 20)
    private var device: IOHIDDevice?
    private var featureIndex: UInt8?
    private var owners: [UUID: (Bool) -> Void] = [:]
    private var configured = false
    private var diverted = false
    private var pressed = false
    private var pending: UInt8?
    private var retry: Timer?
    var onError: ((String) -> Void)?

    private init() { reportBuffer.initialize(repeating: 0, count: 20) }
    deinit { stop(); reportBuffer.deinitialize(count: 20); reportBuffer.deallocate() }

    @discardableResult func observe(_ handler: @escaping (Bool) -> Void) -> UUID {
        let id = UUID(); owners[id] = handler
        if owners.count == 1 { start() }
        return id
    }
    func requestPermissionForCapture() {
        let matching: [String: Any] = [kIOHIDVendorIDKey as String: 0x046d, kIOHIDProductIDKey as String: 0xb042]
        IOHIDManagerSetDeviceMatching(manager, matching as CFDictionary)
        guard let devices = IOHIDManagerCopyDevices(manager) as? Set<IOHIDDevice>, !devices.isEmpty else { return }
        if IOHIDCheckAccess(kIOHIDRequestTypeListenEvent) != kIOHIDAccessTypeGranted {
            _ = IOHIDRequestAccess(kIOHIDRequestTypeListenEvent)
        }
    }
    func remove(_ id: UUID) {
        if pressed { owners[id]?(false) }
        owners.removeValue(forKey: id)
        if owners.isEmpty { stop() }
    }
    private func start() {
        guard !configured else { return }
        configured = true
        let matching: [String: Any] = [kIOHIDVendorIDKey as String: 0x046d, kIOHIDProductIDKey as String: 0xb042]
        IOHIDManagerSetDeviceMatching(manager, matching as CFDictionary)
        let context = Unmanaged.passUnretained(self).toOpaque()
        IOHIDManagerRegisterDeviceMatchingCallback(manager, { context, _, _, device in
            guard let context = context else { return }
            Unmanaged<MXMaster4HID>.fromOpaque(context).takeUnretainedValue().connect(device)
        }, context)
        IOHIDManagerRegisterDeviceRemovalCallback(manager, { context, _, _, device in
            guard let context = context else { return }
            Unmanaged<MXMaster4HID>.fromOpaque(context).takeUnretainedValue().disconnect(device)
        }, context)
        IOHIDManagerScheduleWithRunLoop(manager, CFRunLoopGetMain(), CFRunLoopMode.commonModes.rawValue)
        let result = IOHIDManagerOpen(manager, IOOptionBits(kIOHIDOptionsTypeNone))
        if result != kIOReturnSuccess {
            IOHIDManagerClose(manager, IOOptionBits(kIOHIDOptionsTypeNone))
            IOHIDManagerUnscheduleFromRunLoop(manager, CFRunLoopGetMain(), CFRunLoopMode.commonModes.rawValue)
            configured = false
            onError?("MX Master 4の専用ボタンを開けません。macOS「プライバシーとセキュリティ」→「入力監視」でRELYRを許可してください。")
            retry?.invalidate()
            retry = Timer.scheduledTimer(withTimeInterval: 3, repeats: true) { [weak self] timer in
                guard let self = self, !self.owners.isEmpty else { timer.invalidate(); return }
                if IOHIDCheckAccess(kIOHIDRequestTypeListenEvent) == kIOHIDAccessTypeGranted {
                    timer.invalidate(); self.retry = nil; self.start()
                }
            }
        }
    }
    private func connect(_ candidate: IOHIDDevice) {
        guard device == nil, !owners.isEmpty else { return }
        let result = IOHIDDeviceOpen(candidate, IOOptionBits(kIOHIDOptionsTypeNone))
        guard result == kIOReturnSuccess else {
            onError?("MX Master 4の専用ボタンに接続できません（HID: \(result)）。")
            retry?.invalidate()
            retry = Timer.scheduledTimer(withTimeInterval: 3, repeats: true) { [weak self] timer in
                guard let self = self, !self.owners.isEmpty else { timer.invalidate(); return }
                guard IOHIDCheckAccess(kIOHIDRequestTypeListenEvent) == kIOHIDAccessTypeGranted else { return }
                guard let devices = IOHIDManagerCopyDevices(self.manager) as? Set<IOHIDDevice>, let first = devices.first else { return }
                timer.invalidate(); self.retry = nil; self.connect(first)
            }
            return
        }
        retry?.invalidate(); retry = nil
        device = candidate; featureIndex = nil; pending = nil; diverted = false; pressed = false
        IOHIDDeviceRegisterInputReportCallback(candidate, reportBuffer, 20, { context, result, _, _, _, report, length in
            guard result == kIOReturnSuccess, let context = context else { return }
            Unmanaged<MXMaster4HID>.fromOpaque(context).takeUnretainedValue()
                .receive(Array(UnsafeBufferPointer(start: report, count: length)))
        }, Unmanaged.passUnretained(self).toOpaque())
        // Root.GetFeature(0x1b04), software ID 1.
        send(feature: 0, function: 0, payload: [0x1b, 0x04])
    }
    private func disconnect(_ candidate: IOHIDDevice) {
        guard let device = device, device === candidate else { return }
        if pressed { pressed = false; owners.values.forEach { $0(false) } }
        self.device = nil; featureIndex = nil; pending = nil; diverted = false
        IOHIDDeviceClose(device, IOOptionBits(kIOHIDOptionsTypeNone))
    }
    private func stop() {
        retry?.invalidate(); retry = nil
        if let device = device {
            if diverted, let featureIndex = featureIndex {
                // Update temporary-diversion bit and clear it; leave all other CID state intact.
                send(feature: featureIndex, function: 3, payload: [0x00, 0xc4, 0x02, 0x00, 0x00])
            }
            if pressed { pressed = false; owners.values.forEach { $0(false) } }
            self.device = nil
            IOHIDDeviceClose(device, IOOptionBits(kIOHIDOptionsTypeNone))
        }
        featureIndex = nil; pending = nil; diverted = false
        if configured {
            IOHIDManagerClose(manager, IOOptionBits(kIOHIDOptionsTypeNone))
            IOHIDManagerUnscheduleFromRunLoop(manager, CFRunLoopGetMain(), CFRunLoopMode.commonModes.rawValue)
            configured = false
        }
    }
    private func send(feature: UInt8, function: UInt8, payload: [UInt8]) {
        guard let device = device else { return }
        var bytes = [UInt8](repeating: 0, count: 20)
        bytes[0] = 0x11; bytes[1] = 0xff; bytes[2] = feature
        bytes[3] = (function << 4) | 1
        for (index, byte) in payload.prefix(16).enumerated() { bytes[index + 4] = byte }
        pending = bytes[3]
        let result = IOHIDDeviceSetReport(device, kIOHIDReportTypeOutput, CFIndex(0x11), &bytes, bytes.count)
        if result != kIOReturnSuccess { pending = nil; onError?("MX Master 4への設定送信に失敗しました（HID: \(result)）。") }
    }
    private func receive(_ bytes: [UInt8]) {
        guard bytes.count >= 7, bytes[0] == 0x11, bytes[1] == 0xff || bytes[1] == 0 else { return }
        if bytes[2] == 0xff { pending = nil; onError?("MX Master 4が専用ボタン設定を拒否しました（HID++: \(bytes[5])）。"); return }
        if bytes[2] == 0, bytes[3] == 0x01, pending == 0x01 {
            pending = nil
            guard bytes[4] != 0 else { onError?("MX Master 4はボタン割り当て機能を公開していません。") ; return }
            featureIndex = bytes[4]
            // GetCIDReporting(0x00c4) before changing only its temporary bit.
            send(feature: bytes[4], function: 2, payload: [0x00, 0xc4]); return
        }
        guard let featureIndex = featureIndex, bytes[2] == featureIndex else { return }
        if bytes[3] == 0x21, pending == 0x21 {
            pending = nil
            guard bytes[4] == 0, bytes[5] == 0xc4 else { return }
            // Even if a previous RELYR process crashed while diverted, reclaim the
            // button now and return it to the firmware when this session ends.
            send(feature: featureIndex, function: 3, payload: [0x00, 0xc4, 0x03, 0x00, 0x00]); return
        }
        if bytes[3] == 0x31, pending == 0x31 {
            pending = nil; diverted = true; return
        }
        guard diverted, bytes[3] >> 4 == 0, bytes[3] & 0x0f == 0 else { return }
        let controls = stride(from: 4, through: min(bytes.count - 2, 10), by: 2).map { (UInt16(bytes[$0]) << 8) | UInt16(bytes[$0 + 1]) }
        let down = controls.contains(0x00c4)
        guard down != pressed else { return }
        pressed = down
        owners.values.forEach { $0(down) }
    }
}
