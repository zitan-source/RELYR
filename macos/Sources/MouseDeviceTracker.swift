import Foundation
import IOKit.hid

/// Observes physical mice without seizing them. Quartz mouse events do not
/// carry a mouse identity, so the most recent HID movement/button is used to
/// choose a device. Unknown devices keep the common assignments.
final class MouseDeviceTracker {
    static let shared = MouseDeviceTracker()
    private let manager = IOHIDManagerCreate(kCFAllocatorDefault, IOOptionBits(kIOHIDOptionsTypeNone))
    private(set) var devices: [String:String] = [:]
    private(set) var activeID: String?
    private var recentButtons: [Int:(String,TimeInterval)] = [:]
    private var running = false
    private var refreshTimer: Timer?
    var onDevicesChanged: (() -> Void)?
    var onActivity: ((String) -> Void)?

    private init() {}
    static func id(vendor: Int, product: Int) -> String? {
        guard vendor > 0, product > 0 else { return nil }
        return String(format:"%04x:%04x",vendor,product)
    }
    static func id(vendor: Int, product: Int, unique: String?, serial: String?, location: UInt64?, builtIn: Bool) -> String? {
        guard !builtIn else { return nil }
        if let standard = id(vendor:vendor,product:product) { return standard }
        let unique = unique?.trimmingCharacters(in:.whitespacesAndNewlines)
        let serial = serial?.trimmingCharacters(in:.whitespacesAndNewlines)
        guard let detail = [unique,serial].compactMap({ $0 }).first(where:{ !$0.isEmpty })
                ?? location.map({ "port:\($0)" }) else { return nil }
        // A stable opaque identifier avoids storing a device serial or Bluetooth address in settings.
        var hash: UInt64 = 14_695_981_039_346_656_037
        for byte in "\(vendor):\(product):\(detail)".utf8 {
            hash = (hash ^ UInt64(byte)) &* 1_099_511_628_211
        }
        return String(format:"generic:%016llx",hash)
    }
    static func displayName(_ product: String?, id: String) -> String {
        let name = product?.trimmingCharacters(in:.whitespacesAndNewlines) ?? ""
        return name.isEmpty ? "汎用マウス (\(id))" : name
    }
    private func identity(_ device: IOHIDDevice) -> (String,String)? {
        let vendor = (IOHIDDeviceGetProperty(device,kIOHIDVendorIDKey as CFString) as? NSNumber)?.intValue ?? 0
        let product = (IOHIDDeviceGetProperty(device,kIOHIDProductIDKey as CFString) as? NSNumber)?.intValue ?? 0
        let unique = IOHIDDeviceGetProperty(device,kIOHIDPhysicalDeviceUniqueIDKey as CFString) as? String
        let serial = IOHIDDeviceGetProperty(device,kIOHIDSerialNumberKey as CFString) as? String
        let location = (IOHIDDeviceGetProperty(device,kIOHIDLocationIDKey as CFString) as? NSNumber)?.uint64Value
        let builtIn = (IOHIDDeviceGetProperty(device,kIOHIDBuiltInKey as CFString) as? NSNumber)?.boolValue ?? false
        guard let id = Self.id(vendor:vendor,product:product,unique:unique,serial:serial,location:location,builtIn:builtIn) else { return nil }
        let name = Self.displayName(IOHIDDeviceGetProperty(device,kIOHIDProductKey as CFString) as? String,id:id)
        return (id,name)
    }
    func start() {
        guard !running else { return }
        let match: [String:Any] = [kIOHIDDeviceUsagePageKey as String:1,kIOHIDDeviceUsageKey as String:2]
        IOHIDManagerSetDeviceMatching(manager,match as CFDictionary)
        let context = Unmanaged.passUnretained(self).toOpaque()
        IOHIDManagerRegisterDeviceMatchingCallback(manager,{ context,_,_,device in
            guard let context = context else { return }
            Unmanaged<MouseDeviceTracker>.fromOpaque(context).takeUnretainedValue().connected(device)
        },context)
        IOHIDManagerRegisterDeviceRemovalCallback(manager,{ context,_,_,device in
            guard let context = context else { return }
            Unmanaged<MouseDeviceTracker>.fromOpaque(context).takeUnretainedValue().removed(device)
        },context)
        IOHIDManagerRegisterInputValueCallback(manager,{ context,_,_,value in
            guard let context = context else { return }
            Unmanaged<MouseDeviceTracker>.fromOpaque(context).takeUnretainedValue().received(value)
        },context)
        IOHIDManagerScheduleWithRunLoop(manager,CFRunLoopGetMain(),CFRunLoopMode.commonModes.rawValue)
        guard IOHIDManagerOpen(manager,IOOptionBits(kIOHIDOptionsTypeNone)) == kIOReturnSuccess else {
            IOHIDManagerUnscheduleFromRunLoop(manager,CFRunLoopGetMain(),CFRunLoopMode.commonModes.rawValue)
            return
        }
        running = true
        refreshDevices()
        let timer = Timer(timeInterval:2,repeats:true) { [weak self] _ in self?.refreshDevices() }
        RunLoop.main.add(timer,forMode:.common)
        refreshTimer = timer
    }
    func stop() {
        guard running else { return }
        refreshTimer?.invalidate(); refreshTimer = nil
        IOHIDManagerClose(manager,IOOptionBits(kIOHIDOptionsTypeNone))
        IOHIDManagerUnscheduleFromRunLoop(manager,CFRunLoopGetMain(),CFRunLoopMode.commonModes.rawValue)
        running = false; activeID = nil; recentButtons.removeAll(); devices.removeAll()
        onDevicesChanged?()
    }
    private func refreshDevices() {
        guard running, let found = IOHIDManagerCopyDevices(manager) as? Set<IOHIDDevice> else { return }
        var current: [String:String] = [:]
        for device in found {
            if let (id,name) = identity(device) { current[id] = name }
        }
        guard current != devices else { return }
        devices = current
        if let activeID = activeID, current[activeID] == nil { self.activeID = nil }
        recentButtons = recentButtons.filter { current[$0.value.0] != nil }
        onDevicesChanged?()
    }
    private func connected(_ device: IOHIDDevice) {
        guard let (id,name) = identity(device) else { return }
        devices[id] = name; onDevicesChanged?()
    }
    private func removed(_ device: IOHIDDevice) {
        guard let (id,_) = identity(device) else { return }
        devices.removeValue(forKey:id)
        if activeID == id { activeID = nil }
        recentButtons = recentButtons.filter { $0.value.0 != id }
        onDevicesChanged?()
    }
    private func received(_ value: IOHIDValue) {
        let element = IOHIDValueGetElement(value)
        guard let (id,_) = identity(IOHIDElementGetDevice(element)) else { return }
        let page = IOHIDElementGetUsagePage(element), usage = IOHIDElementGetUsage(element)
        let reading = IOHIDValueGetIntegerValue(value)
        guard (page == 9 && (1...32).contains(usage)) || (page == 1 && [0x30,0x31,0x38,0x48].contains(usage) && reading != 0) else { return }
        if page == 9, reading != 0 { recentButtons[Int(usage)-1] = (id,ProcessInfo.processInfo.systemUptime) }
        activeID = id; onActivity?(id)
    }
    func device(forButton button: Int) -> String? {
        if let recent = recentButtons[button], ProcessInfo.processInfo.systemUptime - recent.1 < 0.075 { return recent.0 }
        return activeID
    }
    func markMXMaster4() {
        let id = "046d:b042"
        devices[id] = "MX Master 4"; activeID = id
        onDevicesChanged?(); onActivity?(id)
    }
}
