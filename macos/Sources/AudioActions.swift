import CoreAudio
import Foundation

protocol AudioBackend {
    func volume() -> Float32?
    func setVolume(_ value: Float32) -> Bool
    func muted() -> Bool?
    func setMuted(_ value: Bool) -> Bool
}
struct AudioActions {
    static func perform(_ command: SystemCommand, backend: AudioBackend = SystemAudioBackend()) -> String? {
        guard command == .volumeUp || command == .volumeDown || command == .mute || command == .micMute else { return "音量Actionではありません。" }
        if command == .mute || command == .micMute {
            guard let muted = backend.muted(), backend.setMuted(!muted) else { return "この出力機器の消音を変更できません。" }
            return nil
        }
        switch adjustVolume(by: command == .volumeUp ? 0.0625 : -0.0625, backend: backend) {
        case .success: return nil
        case .failure(let error): return error.message
        }
    }

    struct VolumeError: Error { let message: String }

    static func adjustVolume(by delta: Float32, backend: AudioBackend) -> Result<Float32, VolumeError> {
        guard delta.isFinite else { return .failure(VolumeError(message: "音量の変更量が無効です。")) }
        guard let volume = backend.volume(), volume.isFinite else { return .failure(VolumeError(message: "この出力機器の音量を取得できません。")) }
        let target = min(1, max(0, volume + delta))
        if target != volume, !backend.setVolume(target) {
            return .failure(VolumeError(message: "この出力機器の音量を変更できません。"))
        }
        if delta > 0, backend.muted() == true, !backend.setMuted(false) {
            return .failure(VolumeError(message: "音量は変更しましたが、消音を解除できませんでした。"))
        }
        return .success(backend.volume() ?? target)
    }
}
struct SystemAudioBackend: AudioBackend {
    let device: AudioObjectID?
    private let scope: AudioObjectPropertyScope
    init(input: Bool = false) {
        scope = input ? kAudioDevicePropertyScopeInput : kAudioDevicePropertyScopeOutput
        var id = AudioObjectID(0), size = UInt32(MemoryLayout<AudioObjectID>.size)
        var address = AudioObjectPropertyAddress(mSelector:input ? kAudioHardwarePropertyDefaultInputDevice : kAudioHardwarePropertyDefaultOutputDevice,mScope:kAudioObjectPropertyScopeGlobal,mElement:kAudioObjectPropertyElementMain)
        device = AudioObjectGetPropertyData(AudioObjectID(kAudioObjectSystemObject),&address,0,nil,&size,&id) == noErr && id != 0 ? id : nil
    }
    private func read<T>(_ selector: AudioObjectPropertySelector, initial: T, element: AudioObjectPropertyElement = kAudioObjectPropertyElementMain) -> T? {
        guard let device = device else { return nil }
        var value = initial, size = UInt32(MemoryLayout<T>.size)
        var address = AudioObjectPropertyAddress(mSelector:selector,mScope:scope,mElement:element)
        return withUnsafeMutablePointer(to:&value) { pointer in
            AudioObjectGetPropertyData(device,&address,0,nil,&size,pointer) == noErr ? pointer.pointee : nil
        }
    }
    private func write<T>(_ selector: AudioObjectPropertySelector, value: T, element: AudioObjectPropertyElement = kAudioObjectPropertyElementMain) -> Bool {
        guard let device = device else { return false }
        var value = value, settable = DarwinBoolean(false)
        var address = AudioObjectPropertyAddress(mSelector:selector,mScope:scope,mElement:element)
        guard AudioObjectIsPropertySettable(device,&address,&settable) == noErr, settable.boolValue else { return false }
        return withUnsafePointer(to:&value) { AudioObjectSetPropertyData(device,&address,0,nil,UInt32(MemoryLayout<T>.size),$0) == noErr }
    }
    private func channels() -> [(AudioObjectPropertyElement,Float32)] {
        (1...32).compactMap { index in read(kAudioDevicePropertyVolumeScalar,initial:Float32(0),element:UInt32(index)).flatMap { $0.isFinite && (0...1).contains($0) ? (UInt32(index),$0) : nil } }
    }
    func volume() -> Float32? {
        if let master = read(kAudioDevicePropertyVolumeScalar,initial:Float32(0)),master.isFinite,(0...1).contains(master) { return master }
        let values = channels().map { $0.1 }; return values.isEmpty ? nil : values.reduce(0,+)/Float32(values.count)
    }
    func setVolume(_ value: Float32) -> Bool {
        guard value.isFinite,(0...1).contains(value) else { return false }
        if write(kAudioDevicePropertyVolumeScalar,value:value) { return true }
        let values = channels(); guard !values.isEmpty else { return false }
        var written: [(AudioObjectPropertyElement,Float32)] = []
        for (element,old) in values {
            guard write(kAudioDevicePropertyVolumeScalar,value:value,element:element) else {
                for (channel,original) in written { _ = write(kAudioDevicePropertyVolumeScalar,value:original,element:channel) }; return false
            }
            written.append((element,old))
        }
        return true
    }
    func muted() -> Bool? { read(kAudioDevicePropertyMute,initial:UInt32(0)).map { $0 != 0 } }
    func setMuted(_ value: Bool) -> Bool { write(kAudioDevicePropertyMute,value:UInt32(value ? 1 : 0)) }
}
