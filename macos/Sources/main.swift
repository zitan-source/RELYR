import AppKit
import Darwin

// UI verification writes fixture settings. Reject incomplete/unsafe arguments before constructing
// the delegate, so a manually invoked test can never fall back to the user's real configuration.
if CommandLine.arguments.contains("--ui-smoke-test") {
    let args = CommandLine.arguments
    var valid = args.count == 3 && args[1] == "--ui-smoke-test"
    if valid {
        let image = URL(fileURLWithPath: args[2]).standardizedFileURL
        let parent = image.deletingLastPathComponent()
        valid = image.pathExtension == "png" && parent.pathComponents.contains(".verification") &&
            parent == Bundle.main.bundleURL.deletingLastPathComponent().standardizedFileURL
    }
    if !valid {
        FileHandle.standardError.write(Data("UI verification requires a PNG path beside the built app inside .verification. User settings were not opened.\n".utf8))
        exit(2)
    }
}

let application = NSApplication.shared
let delegate = AppDelegate()
application.delegate = delegate
application.setActivationPolicy(.regular)
application.run()
