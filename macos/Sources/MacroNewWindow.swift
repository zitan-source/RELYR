import AppKit
import ApplicationServices

/// A new-window request followed by placement must not resize the original
/// window a second time. Wait for a new, focused window in the same application.
final class MacroNewWindow {
    private var token: UUID?
    var isBusy: Bool { token != nil }
    func cancel() { token = nil }

    static func requiresConfirmation(_ step: MacroStep, remaining: ArraySlice<MacroStep>) -> Bool {
        guard step.phase == .pulse, let action = step.action, action.action.isKey,
              action.output == 45, action.modifiers == .command else { return false }
        return remaining.first(where: { $0.action != nil || $0.phase == .move })?.action?.resolvedWindowShortcut.action == .window
    }
    private func value(_ element: AXUIElement, _ name: String) -> CFTypeRef? {
        var result: CFTypeRef?
        return AXUIElementCopyAttributeValue(element,name as CFString,&result) == .success ? result : nil
    }
    private func element(_ value: CFTypeRef?) -> AXUIElement? {
        guard let value = value, CFGetTypeID(value) == AXUIElementGetTypeID() else { return nil }
        return (value as! AXUIElement)
    }
    private func newWindowMenu(_ root: AXUIElement, depth: Int = 0) -> AXUIElement? {
        guard depth < 5 else { return nil }
        if (value(root,kAXMenuItemCmdCharAttribute) as? String)?.lowercased() == "n",
           (value(root,kAXMenuItemCmdModifiersAttribute) as? NSNumber)?.intValue == 0,
           value(root,kAXEnabledAttribute) as? Bool != false { return root }
        for child in value(root,kAXChildrenAttribute) as? [AXUIElement] ?? [] {
            if let match = newWindowMenu(child,depth:depth+1) { return match }
        }
        return nil
    }
    func start(completion: @escaping (String?) -> Void) {
        cancel()
        guard let app = NSWorkspace.shared.frontmostApplication,
              app.processIdentifier != ProcessInfo.processInfo.processIdentifier else {
            completion("新規ウィンドウを開く対象アプリが前面にありません。"); return
        }
        let root = AXUIElementCreateApplication(app.processIdentifier)
        AXUIElementSetMessagingTimeout(root,0.5)
        let before = value(root,kAXWindowsAttribute) as? [AXUIElement] ?? []
        guard !before.isEmpty, let menu = element(value(root,kAXMenuBarAttribute)),
              let command = newWindowMenu(menu),
              AXUIElementPerformAction(command,kAXPressAction as CFString) == .success else {
            completion("対象アプリの「新規ウィンドウ（Command＋N）」を実行できませんでした。元のウィンドウの再配置は行いません。"); return
        }
        let request = UUID(); token = request
        let started = ProcessInfo.processInfo.systemUptime
        func poll() {
            guard self.token == request else { return }
            guard !app.isTerminated, NSWorkspace.shared.frontmostApplication?.processIdentifier == app.processIdentifier else {
                self.token = nil; completion("対象アプリが切り替わったため、新規ウィンドウの配置を停止しました。"); return
            }
            if let focused = self.element(self.value(root,kAXFocusedWindowAttribute)),
               !before.contains(where: { CFEqual($0,focused) }),
               self.value(focused,kAXRoleAttribute) as? String == kAXWindowRole,
               self.value(focused,kAXSubroleAttribute) as? String == kAXStandardWindowSubrole {
                self.token = nil; completion(nil); return
            }
            guard ProcessInfo.processInfo.systemUptime - started < 8 else {
                self.token = nil; completion("新しいウィンドウを確認できなかったため停止しました。元のウィンドウは移動していません。"); return
            }
            DispatchQueue.main.asyncAfter(deadline:.now()+0.05,execute:poll)
        }
        DispatchQueue.main.asyncAfter(deadline:.now()+0.05,execute:poll)
    }
}
