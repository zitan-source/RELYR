import Foundation
import CoreWLAN

protocol WiFiBackend {
    func powered() -> Bool?
    func setPower(_ value: Bool) throws
}
struct NativeWiFiBackend: WiFiBackend {
    private let interface = CWWiFiClient.shared().interface()
    func powered() -> Bool? { interface?.powerOn() }
    func setPower(_ value: Bool) throws {
        guard let interface = interface else { throw ConfigurationError.invalid("Wi-Fi機器が見つかりません。") }
        try interface.setPower(value)
    }
}
enum ConnectivityActions {
    static func perform(_ command: SystemCommand, backend: WiFiBackend = NativeWiFiBackend()) -> String? {
        guard [.wifiOn,.wifiOff,.wifiToggle].contains(command), let current = backend.powered() else { return "Wi-Fi機器の状態を取得できません。" }
        let target = command == .wifiToggle ? !current : command == .wifiOn
        do { if target != current { try backend.setPower(target) }; return nil }
        catch { return "Wi-Fiを変更できません：\(error.localizedDescription)" }
    }
}
