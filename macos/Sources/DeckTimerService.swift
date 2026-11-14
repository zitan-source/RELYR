import AppKit

/// One shared timer, independent from any visible panel (Windows DeckTimerService).
final class DeckTimerService {
    static let shared = DeckTimerService()
    private var countdown = DeckCountdown()
    private var pending: DispatchWorkItem?
    private var notification: NSPanel?
    private var generation = 0
    var label: String { countdown.label(now:Date().timeIntervalSince1970) }
    func start(minutes: Double) {
        guard countdown.start(seconds:minutes*60,now:Date().timeIntervalSince1970) else { return }
        generation += 1; pending?.cancel(); schedule()
    }
    func cancel() { generation += 1; countdown.cancel(); pending?.cancel(); pending = nil }
    private func schedule() {
        let ticket = generation
        let work = DispatchWorkItem { [weak self] in
            guard let self = self,self.generation == ticket else { return }
            if self.countdown.consumeCompletion(now:Date().timeIntervalSince1970) { self.presentCompletion() }
            else { self.schedule() }
        }
        pending = work
        DispatchQueue.main.asyncAfter(deadline:.now()+max(0.1,countdown.remaining(now:Date().timeIntervalSince1970)),execute:work)
    }
    private func presentCompletion() {
        notification?.orderOut(nil)
        guard let screen = NSScreen.main?.visibleFrame else { return }
        let panel = NSPanel(contentRect:NSRect(x:screen.midX-140,y:screen.maxY-110,width:280,height:70),styleMask:[.borderless,.nonactivatingPanel],backing:.buffered,defer:false)
        panel.isReleasedWhenClosed = false; panel.ignoresMouseEvents = true; panel.level = .statusBar; panel.backgroundColor = .windowBackgroundColor
        panel.collectionBehavior = [.canJoinAllSpaces,.fullScreenAuxiliary]
        let text = NSTextField(labelWithString:"タイマーが終了しました")
        text.font = .systemFont(ofSize:17,weight:.semibold); text.alignment = .center; text.frame = NSRect(x:12,y:22,width:256,height:28)
        panel.contentView?.addSubview(text); notification = panel; panel.orderFrontRegardless()
        DispatchQueue.main.asyncAfter(deadline:.now()+4) { [weak self,weak panel] in panel?.orderOut(nil); if self?.notification === panel { self?.notification = nil } }
    }
}
