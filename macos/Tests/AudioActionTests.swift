import Foundation

final class FakeAudio: AudioBackend {
    var level: Float32? = 0.5
    var mute: Bool? = false
    var writable = true
    func volume() -> Float32? { level }
    func muted() -> Bool? { mute }
    func setVolume(_ value: Float32) -> Bool { if writable { level = value }; return writable }
    func setMuted(_ value: Bool) -> Bool { if writable { mute = value }; return writable }
}
@main struct AudioActionTests {
    static func main() {
        let backend = FakeAudio()
        precondition(AudioActions.perform(.volumeUp,backend:backend) == nil && backend.level == 0.5625)
        precondition(AudioActions.perform(.volumeDown,backend:backend) == nil && backend.level == 0.5)
        guard case .success(let fineLevel) = AudioActions.adjustVolume(by: 0.012, backend: backend) else {
            preconditionFailure("Fine volume adjustment failed")
        }
        precondition(abs(fineLevel - 0.512) < 0.0001)
        backend.level = 0.99
        precondition(AudioActions.perform(.volumeUp,backend:backend) == nil && backend.level == 1)
        backend.level = 0.01
        precondition(AudioActions.perform(.volumeDown,backend:backend) == nil && backend.level == 0)
        precondition(AudioActions.perform(.mute,backend:backend) == nil && backend.mute == true)
        precondition(AudioActions.perform(.volumeUp,backend:backend) == nil && backend.mute == false)
        precondition(AudioActions.perform(.micMute,backend:backend) == nil && backend.mute == true)
        backend.level = nil
        precondition(AudioActions.perform(.volumeDown,backend:backend) != nil)
        backend.level = 0.5; backend.writable = false
        precondition(AudioActions.perform(.volumeUp,backend:backend) != nil && backend.level == 0.5)
        print("PASS: volume steps, clamps, mute toggle, unmute and unsupported hardware; fake audio only")
    }
}
