import Foundation

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else { fputs("FAIL: \(message)\n",stderr); exit(1) }
}

@main struct MouseDeviceTrackerTests {
static func main() {
expect(MouseDeviceTracker.id(vendor:0x046d,product:0xb042) == "046d:b042", "known hardware identity stays compatible")
expect(MouseDeviceTracker.id(vendor:0x1234,product:0x5678,unique:nil,serial:nil,location:nil,builtIn:false) == "1234:5678", "new mouse model is selectable")
expect(MouseDeviceTracker.displayName("  ",id:"1234:5678") == "汎用マウス (1234:5678)", "unnamed mouse gets readable label")
expect(MouseDeviceTracker.displayName(" MX Master 4 ",id:"046d:b042") == "MX Master 4", "hardware product name is used")
let anonymous = MouseDeviceTracker.id(vendor:0,product:0,unique:"device-A",serial:nil,location:nil,builtIn:false)
expect(anonymous != nil && anonymous!.hasPrefix("generic:"), "anonymous external mouse gets an opaque identity")
expect(anonymous == MouseDeviceTracker.id(vendor:0,product:0,unique:"device-A",serial:nil,location:nil,builtIn:false), "anonymous identity is stable")
expect(anonymous != MouseDeviceTracker.id(vendor:0,product:0,unique:"device-B",serial:nil,location:nil,builtIn:false), "anonymous mice remain separate")
expect(MouseDeviceTracker.id(vendor:0,product:0,unique:"internal",serial:nil,location:1,builtIn:true) == nil, "built-in trackpad stays out of mouse list")
expect(MouseDeviceTracker.id(vendor:0,product:0,unique:nil,serial:nil,location:nil,builtIn:false) == nil, "unidentifiable device uses common assignments")
print("PASS: known, new, unnamed, anonymous and built-in mouse identity")
}
}
