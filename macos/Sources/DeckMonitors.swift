import AppKit
import Darwin
import IOKit.ps
import IOBluetooth

/// Optional read-only system adapter. Unsupported displays report no value, never a guessed level.
private enum DeckBrightness {
    typealias Reader = @convention(c) (UInt32, UnsafeMutablePointer<Float>) -> Int32
    static let handle = dlopen("/System/Library/PrivateFrameworks/DisplayServices.framework/DisplayServices", RTLD_LAZY)
    static let read: Reader? = handle.flatMap { dlsym($0,"DisplayServicesGetBrightness") }.map { unsafeBitCast($0,to:Reader.self) }
    static func level() -> Float? {
        guard let read = read else { return nil }
        var count: UInt32 = 0
        var displays = [CGDirectDisplayID](repeating:0,count:16)
        guard CGGetActiveDisplayList(16,&displays,&count) == .success else { return nil }
        // A stable target: built-in display first, otherwise the main display.
        let display = displays.prefix(Int(count)).first { CGDisplayIsBuiltin($0) != 0 } ?? CGMainDisplayID()
        var value: Float = 0
        guard read(display,&value) == 0,value.isFinite,(0...1).contains(value) else { return nil }
        return value
    }
}

/// Samples only requested monitor kinds; the panel owns the visible-only timer.
final class DeckMonitors {
    private var previousNetwork: (UInt64,UInt64,TimeInterval)?
    private(set) var histories: [DeckMonitor:[Double]] = [:]
    private var previousCPU: (UInt64,UInt64)?
    func sample(_ kinds: Set<DeckMonitor>) -> [DeckMonitor:String] {
        var result: [DeckMonitor:String] = [:]
        if kinds.contains(.clock) { let format = DateFormatter(); format.dateFormat = "HH:mm:ss"; result[.clock] = format.string(from:Date()) }
        if kinds.contains(.date) { let format = DateFormatter(); format.dateFormat = "MM/dd E"; result[.date] = format.string(from:Date()) }
        if kinds.contains(.cpu) {
            var stats = host_cpu_load_info(); var count = mach_msg_type_number_t(MemoryLayout<host_cpu_load_info>.size/MemoryLayout<integer_t>.size)
            let status = withUnsafeMutablePointer(to:&stats) { pointer in pointer.withMemoryRebound(to:integer_t.self,capacity:Int(count)) { host_statistics(mach_host_self(),HOST_CPU_LOAD_INFO,$0,&count) } }
            if status == KERN_SUCCESS {
                let ticks = stats.cpu_ticks; let active = UInt64(ticks.0)+UInt64(ticks.1)+UInt64(ticks.3); let total = active+UInt64(ticks.2)
                if let old = previousCPU,total > old.1,active >= old.0 { result[.cpu] = String(format:"%.0f%%",100*Double(active-old.0)/Double(total-old.1)) }
                previousCPU = (active,total)
            }
        }
        if kinds.contains(.memory) {
            var stats = vm_statistics64(); var count = mach_msg_type_number_t(MemoryLayout<vm_statistics64>.size/MemoryLayout<integer_t>.size)
            let status = withUnsafeMutablePointer(to:&stats) { pointer in pointer.withMemoryRebound(to:integer_t.self,capacity:Int(count)) { host_statistics64(mach_host_self(),HOST_VM_INFO64,$0,&count) } }
            if status == KERN_SUCCESS {
                let used = (UInt64(stats.active_count)+UInt64(stats.wire_count)+UInt64(stats.compressor_page_count))*UInt64(vm_kernel_page_size)
                result[.memory] = String(format:"%.1f GB",Double(used)/1073741824)
            }
        }
        if kinds.contains(.disk),let values = try? URL(fileURLWithPath:"/").resourceValues(forKeys:[.volumeAvailableCapacityKey]),let bytes = values.volumeAvailableCapacity { result[.disk] = String(format:"%.0f GB",Double(bytes)/1073741824) }
        if kinds.contains(.uptime) { let minutes = Int(ProcessInfo.processInfo.systemUptime/60); result[.uptime] = "\(minutes/60)h \(minutes%60)m" }
        if kinds.contains(.timer) { result[.timer] = DeckTimerService.shared.label }
        if kinds.contains(.brightness) { result[.brightness] = DeckBrightness.level().map { String(format:"%.0f%%",$0*100) } ?? "—" }
        if kinds.contains(.volume) { let audio = SystemAudioBackend(); result[.volume] = audio.muted() == true ? "MUTE" : audio.volume().map { String(format:"%.0f%%",$0*100) } }
        if kinds.contains(.microphone) { result[.microphone] = SystemAudioBackend(input:true).muted().map { $0 ? "MUTE" : "ON" } }
        if kinds.contains(.wifi) { result[.wifi] = NativeWiFiBackend().powered().map { $0 ? "ON" : "OFF" } }
        if kinds.contains(.bluetooth),let controller = IOBluetoothHostController.default() { result[.bluetooth] = controller.powerState == kBluetoothHCIPowerStateON ? "ON" : "OFF" }
        if kinds.contains(.battery),let info = IOPSCopyPowerSourcesInfo()?.takeRetainedValue(),let sources = IOPSCopyPowerSourcesList(info)?.takeRetainedValue() as? [CFTypeRef] {
            for source in sources {
                guard let values = IOPSGetPowerSourceDescription(info,source)?.takeUnretainedValue() as? [String:Any],let current = values[kIOPSCurrentCapacityKey] as? Int,let maximum = values[kIOPSMaxCapacityKey] as? Int,maximum > 0 else { continue }
                result[.battery] = "\(min(100,max(0,current*100/maximum)))%"; break
            }
        }
        if !kinds.isDisjoint(with:[.networkUp,.networkDown,.networkStatus]) {
            var head: UnsafeMutablePointer<ifaddrs>?
            if getifaddrs(&head) == 0,let first = head {
                defer { freeifaddrs(first) }
                var current: UnsafeMutablePointer<ifaddrs>? = first, sent: UInt64 = 0, received: UInt64 = 0, online = false
                while let pointer = current {
                    let entry = pointer.pointee; current = entry.ifa_next
                    guard entry.ifa_flags & UInt32(IFF_LOOPBACK) == 0,entry.ifa_flags & UInt32(IFF_UP) != 0,let address = entry.ifa_addr else { continue }
                    if address.pointee.sa_family == UInt8(AF_INET) || address.pointee.sa_family == UInt8(AF_INET6) { online = true }
                    if address.pointee.sa_family == UInt8(AF_LINK),let data = entry.ifa_data,String(cString:entry.ifa_name).hasPrefix("en") {
                        let counters = data.assumingMemoryBound(to:if_data.self).pointee
                        sent += UInt64(counters.ifi_obytes); received += UInt64(counters.ifi_ibytes)
                    }
                }
                let now = ProcessInfo.processInfo.systemUptime
                if let old = previousNetwork,now > old.2,sent >= old.0,received >= old.1 {
                    if kinds.contains(.networkUp) { result[.networkUp] = String(format:"%.0f KB/s",Double(sent-old.0)/(now-old.2)/1024) }
                    if kinds.contains(.networkDown) { result[.networkDown] = String(format:"%.0f KB/s",Double(received-old.1)/(now-old.2)/1024) }
                }
                previousNetwork = (sent,received,now)
                if kinds.contains(.networkStatus) { result[.networkStatus] = online ? "ONLINE" : "OFFLINE" }
            }
        } else { previousNetwork = nil }
        for kind in kinds {
            if [.cpu,.memory,.disk,.battery,.volume,.networkUp,.networkDown].contains(kind),let text = result[kind],let value = Double(text.prefix { $0.isNumber || $0 == "." }),value.isFinite {
                histories[kind,default:[]].append(value); histories[kind] = Array(histories[kind]!.suffix(32))
            } else { histories[kind] = nil }
        }
        histories = histories.filter { kinds.contains($0.key) }
        return result
    }
}

/// All visible Decks share one sample per interval. Hidden Decks retain no request.
final class DeckMonitorPool {
    static let shared = DeckMonitorPool()
    private var source = DeckMonitors()
    private let queue = DispatchQueue(label:"relyr.deck-monitor-sampling",qos:.utility)
    private var pending: UUID?
    private var requests: [String:Set<DeckMonitor>] = [:]
    private var callbacks: [String:() -> Void] = [:]
    private var sampledKinds = Set<DeckMonitor>()
    private var lastSample = -Double.infinity
    private var values: [DeckMonitor:String] = [:]
    private(set) var histories: [DeckMonitor:[Double]] = [:]
    var subscriberCount: Int { requests.count }
    private(set) var sampleCount = 0
    func sample(owner: String, kinds: Set<DeckMonitor>, updated: (() -> Void)? = nil) -> [DeckMonitor:String] {
        guard !kinds.isEmpty else { release(owner:owner); return [:] }
        requests[owner] = kinds; callbacks[owner] = updated
        let requested = requests.values.reduce(into:Set<DeckMonitor>()) { $0.formUnion($1) }
        let now = ProcessInfo.processInfo.systemUptime
        let immediate: Set<DeckMonitor> = [.clock,.date,.timer,.uptime,.autoExtract]
        // Clock/timer labels are cheap and stay synchronous. Audio, disk,
        // network and radio queries must never stall the UI/input run loop.
        let local = DeckMonitors().sample(requested.intersection(immediate))
        values.merge(local) { _, new in new }
        if pending == nil && (now-lastSample >= 0.9 || !requested.isSubset(of:sampledKinds)) {
            let hardware = requested.subtracting(immediate)
            sampledKinds = requested; lastSample = now; sampleCount += 1
            if !hardware.isEmpty {
                let token = UUID(), sampler = source
                pending = token
                queue.async { [weak self] in
                    let result = sampler.sample(hardware), history = sampler.histories
                    DispatchQueue.main.async {
                        guard let self = self, self.pending == token else { return }
                        self.pending = nil
                        let active = self.requests.values.reduce(into:Set<DeckMonitor>()) { $0.formUnion($1) }
                        self.values.merge(result.filter { active.contains($0.key) }) { _, new in new }
                        self.histories = history.filter { active.contains($0.key) }
                        // Publish one result to every visible panel. No queued
                        // polling backlog even when a device takes a long time.
                        for callback in Array(self.callbacks.values) { callback() }
                    }
                }
            }
        }
        return values
    }
    func release(owner: String) {
        requests.removeValue(forKey:owner); callbacks.removeValue(forKey:owner)
        if requests.isEmpty {
            pending = nil; source = DeckMonitors(); values = [:]; histories = [:]
            sampledKinds = []; lastSample = -Double.infinity
        }
    }
}
