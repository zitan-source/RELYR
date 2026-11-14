import AppKit
import Foundation
// Ask only this installed app to quit normally, allowing its input cleanup to run.
let target = URL(fileURLWithPath:CommandLine.arguments[1]).standardizedFileURL
let running = NSWorkspace.shared.runningApplications.filter { $0.bundleURL?.standardizedFileURL == target }
for app in running where !app.terminate() {
    fputs("RELYR could not quit normally; update cancelled.\n",stderr); exit(1)
}
let deadline = Date().addingTimeInterval(15)
while running.contains(where: { !$0.isTerminated }) && Date() < deadline {
    RunLoop.current.run(until:Date().addingTimeInterval(0.1))
}
if running.contains(where: { !$0.isTerminated }) {
    fputs("RELYR is still running; update cancelled without replacing it.\n",stderr); exit(1)
}
