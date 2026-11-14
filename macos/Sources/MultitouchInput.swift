import Foundation
import Darwin

/// Optional private macOS backend. Symbols are loaded at runtime; unavailable devices fail closed.
/// ABI reference: https://github.com/lauschue/Remotastic/blob/main/MultitouchSupport.h
final class MultitouchInput {
    private typealias Callback = @convention(c) (UnsafeMutableRawPointer?, UnsafeRawPointer?, Int32, Double, Int32) -> Int32
    private typealias Create = @convention(c) () -> UnsafeMutableRawPointer?
    private typealias Register = @convention(c) (UnsafeMutableRawPointer?, Callback) -> Void
    private typealias Clock = @convention(c) () -> Double
    private typealias Start = @convention(c) (UnsafeMutableRawPointer?, Int32) -> Int32
    private typealias Stop = @convention(c) (UnsafeMutableRawPointer?) -> Int32
    private static weak var owner: MultitouchInput?
    // Keep the framework loaded for the lifetime of the process: callbacks may be in flight.
    private static let library = dlopen("/System/Library/PrivateFrameworks/MultitouchSupport.framework/MultitouchSupport",RTLD_NOW)
    private static let clock = symbol("MTAbsoluteTimeGetCurrent",Clock.self)
    private var device: UnsafeMutableRawPointer?
    private var startedAt = 0.0
    private var recognizer = TrackpadRecognizer()
    private var touching = false
    private var waitForLift = false
    private var scheduledCornerDeadline: Double?
    var action: ((UInt16) -> Void)?
    var isAllowed: (() -> Bool)?
    var doubleTapEnabled: ((UInt16) -> Bool)?
    var edgeMapping: ((UInt16) -> Bool?)?
    var isEdgeVolume: ((UInt16) -> Bool)?
    var edgeVolume: ((UInt16, Double) -> Void)?
    var onEdgeVolumeEnd: (() -> Void)?
    var onEdgeVolumeFrame: (() -> Void)?
    var edgeWidth = 0.12
    func reservesScroll(_ input: UInt16) -> Bool {
        recognizer.mayOwnScroll(input,width:edgeWidth) { edgeMapping?($0) != nil }
    }
    func reset() {
        waitForLift = touching
        let wasAdjustingVolume = recognizer.adjustingVolume
        recognizer.reset(); scheduledCornerDeadline = nil
        if wasAdjustingVolume { onEdgeVolumeEnd?() }
    }
    private func schedulePendingCorner(after time: Double) {
        guard let deadline = recognizer.pendingCornerDeadline else { scheduledCornerDeadline = nil; return }
        guard scheduledCornerDeadline != deadline else { return }
        scheduledCornerDeadline = deadline
        DispatchQueue.main.asyncAfter(deadline:.now()+max(0,deadline-time)+0.02) { [weak self] in
            guard let self = self, self.device != nil, self.scheduledCornerDeadline == deadline else { return }
            self.scheduledCornerDeadline = nil
            guard self.isAllowed?() == true else { self.reset(); return }
            guard let now = Self.clock?() else { return }
            if let code = self.recognizer.flush(time:now) { self.action?(code) }
            self.schedulePendingCorner(after:now)
        }
    }
    private static func symbol<T>(_ name:String, _:T.Type) -> T? {
        guard let library = library, let pointer = dlsym(library,name) else { return nil }
        return unsafeBitCast(pointer,to:T.self)
    }
    private static let callback: Callback = { source, raw, count, time, _ in
        guard count >= 0, count <= 10, count == 0 || raw != nil else { return 0 }
        var points: [TrackpadContact] = []
        if let raw = raw {
            for index in 0..<Int(count) {
                let item = raw.advanced(by:index*96)
                let state = item.loadUnaligned(fromByteOffset:20,as:Int32.self)
                if state == 3 || state == 4 {
                    let x = Double(item.loadUnaligned(fromByteOffset:32,as:Float.self))
                    let y = Double(item.loadUnaligned(fromByteOffset:36,as:Float.self))
                    // Contacts that straddle the physical rim can report a
                    // position slightly outside the normalized surface.
                    guard (-0.05...1.05).contains(x), (-0.05...1.05).contains(y) else { return 0 }
                    points.append(TrackpadContact(id:Int(item.loadUnaligned(fromByteOffset:24,as:Int32.self)),
                                                  x:min(1,max(0,x)), y:min(1,max(0,y))))
                }
            }
        }
        // Never run Actions on the framework's callback thread.
        let copied = points
        let deviceAddress = UInt(bitPattern: source)
        DispatchQueue.main.async {
            guard let owner = MultitouchInput.owner, let device = owner.device,
                  UInt(bitPattern: device) == deviceAddress, time >= owner.startedAt else { return }
            owner.touching = !copied.isEmpty
            if owner.waitForLift {
                owner.waitForLift = owner.touching
                return
            }
            guard owner.isAllowed?() == true else { owner.reset(); return }
            let wasAdjustingVolume = owner.recognizer.adjustingVolume
            if let code = owner.recognizer.update(copied,time:time,
                                                  doubleTapEnabled:{ owner.doubleTapEnabled?($0) ?? false },
                                                  edgeWidth:owner.edgeWidth,
                                                  edgeMapping:{ owner.edgeMapping?($0) },
                                                  isEdgeVolume:{ owner.isEdgeVolume?($0) ?? false },
                                                  edgeVolume:{ owner.edgeVolume?($0,$1) }) { owner.action?(code) }
            if owner.recognizer.adjustingVolume { owner.onEdgeVolumeFrame?() }
            else if wasAdjustingVolume { owner.onEdgeVolumeEnd?() }
            owner.schedulePendingCorner(after:time)
        }
        return 0
    }
    @discardableResult func start() -> Bool {
        guard device == nil else { return true }
        guard let create = Self.symbol("MTDeviceCreateDefault",Create.self),
              let register = Self.symbol("MTRegisterContactFrameCallback",Register.self),
              let start = Self.symbol("MTDeviceStart",Start.self),
              Self.symbol("MTDeviceStop",Stop.self) != nil,
              let clock = Self.clock,
              let device = create() else { return false }
        // Callback timestamps use the framework clock, which can diverge from
        // systemUptime after sleep. Use it for startup and tap deadlines.
        touching = false
        reset()
        startedAt = clock()
        self.device = device
        Self.owner = self
        register(device,Self.callback)
        guard start(device,0) == 0 else { stop(); return false }
        return true
    }
    func stop() {
        guard let device = device else { return }
        self.device = nil; Self.owner = nil; reset()
        _ = Self.symbol("MTDeviceStop",Stop.self)?(device)
        Self.symbol("MTUnregisterContactFrameCallback",Register.self)?(device,Self.callback)
        typealias Release = @convention(c) (UnsafeMutableRawPointer?) -> Void
        Self.symbol("MTDeviceRelease",Release.self)?(device)
    }
    deinit { stop() }
}
