import AppKit
import Quartz

/// One bounded preview at a time. Hover surfaces never intercept input.
final class DeckFilePreview: NSObject, NSPopoverDelegate {
    static let shared = DeckFilePreview()
    private var ticket = 0
    private var owner: String?
    private var interactiveOwner: String?
    private var hoverPanel: NSPanel?
    private var hoverView: QLPreviewView?
    private var popover: NSPopover?
    private var interactiveView: QLPreviewView?
    static func clipboardFile() -> URL? {
        (NSPasteboard.general.readObjects(forClasses:[NSURL.self],options:[.urlReadingFileURLsOnly:true]) as? [URL])?.first
    }
    func isInteracting(owners: Set<String>) -> Bool { popover?.isShown == true && interactiveOwner.map { owners.contains($0) } == true }
    static func supports(_ url: URL) -> Bool {
        ["png","jpg","jpeg","bmp","gif","tif","tiff","webp","heic","pdf","mp3","wav","m4a","aac","flac","ogg","mp4","m4v","mov","avi","mkv","webm","txt","md","csv","json","xml","log","srt","vtt"].contains(url.pathExtension.lowercased())
    }
    func hover(_ url: URL?, owner: String, anchor: NSView) {
        guard let url = url,Self.supports(url),FileManager.default.fileExists(atPath:url.path) else { cancel(owner:owner); return }
        cancelHover(); ticket += 1; let current = ticket; self.owner = owner
        DispatchQueue.main.asyncAfter(deadline:.now()+0.22) { [weak self,weak anchor] in
            guard let self = self,self.ticket == current,self.popover?.isShown != true,let anchor = anchor,let window = anchor.window,window.isVisible,DeckTile.activeDrags == 0 else { return }
            let bounds = window.convertToScreen(anchor.convert(anchor.bounds,to:nil))
            let screen = window.screen?.visibleFrame ?? bounds
            let size = NSSize(width:min(420,screen.width),height:min(280,screen.height))
            let x = max(screen.minX,min(bounds.maxX+12,screen.maxX-size.width))
            let y = max(screen.minY,min(bounds.maxY-size.height,screen.maxY-size.height))
            let panel = NSPanel(contentRect:NSRect(origin:NSPoint(x:x,y:y),size:size),styleMask:[.borderless,.nonactivatingPanel],backing:.buffered,defer:false)
            panel.isOpaque = true; panel.backgroundColor = .windowBackgroundColor; panel.ignoresMouseEvents = true
            panel.level = .floating; panel.hidesOnDeactivate = false; panel.isReleasedWhenClosed = false
            panel.collectionBehavior = [.canJoinAllSpaces,.fullScreenAuxiliary]
            guard let preview = QLPreviewView(frame:NSRect(origin:.zero,size:size),style:.compact) else { return }
            preview.autostarts = true; preview.shouldCloseWithWindow = false; preview.previewItem = url as NSURL
            panel.contentView = preview; self.hoverPanel = panel; self.hoverView = preview; panel.orderFrontRegardless()
        }
    }
    @discardableResult func show(_ url: URL, anchor: NSView, owner: String) -> Bool {
        guard Self.supports(url),FileManager.default.fileExists(atPath:url.path) else { return false }
        cancelHover(); popover?.close()
        guard let preview = QLPreviewView(frame:NSRect(x:0,y:0,width:480,height:320),style:.normal) else { return false }
        preview.autostarts = true; preview.shouldCloseWithWindow = false; preview.previewItem = url as NSURL
        let controller = NSViewController(); controller.view = preview
        let panel = NSPopover(); panel.behavior = .transient; panel.delegate = self; panel.contentViewController = controller
        interactiveOwner = owner; interactiveView = preview; popover = panel; panel.show(relativeTo:anchor.bounds,of:anchor,preferredEdge:.maxX)
        return true
    }
    func cancel(owner: String) { if self.owner == owner { cancelHover() } }
    private func cancelHover() { ticket += 1; hoverView?.close(); hoverView = nil; hoverPanel?.orderOut(nil); hoverPanel = nil; owner = nil }
    func close(owners: Set<String>) { if owner.map({ owners.contains($0) }) == true { cancelHover() }; if interactiveOwner.map({ owners.contains($0) }) == true { close() } }
    static func verifyLifecycle(anchor: NSView) {
        let preview = DeckFilePreview()
        let url = URL(fileURLWithPath:CommandLine.arguments[2]).deletingLastPathComponent().appendingPathComponent("deck-preview-fixture.txt")
        try! Data("RELYR isolated Quick Look preview".utf8).write(to:url)
        verifySmoke(preview.show(url,anchor:anchor,owner:"fixture"),"Quick Look opens isolated text fixture")
        verifySmoke(preview.isInteracting(owners:["fixture"]),"Preview retains its owner")
        preview.close(owners:["unrelated"])
        verifySmoke(preview.isInteracting(owners:["fixture"]),"Other Deck cannot close active preview")
        preview.close(owners:["fixture"])
        verifySmoke(!preview.isInteracting(owners:["fixture"]) && preview.interactiveView == nil,"Owning Deck releases Quick Look view")
        preview.hover(url,owner:"fixture",anchor:anchor); preview.close(owners:["fixture"])
        verifySmoke(preview.owner == nil && preview.hoverPanel == nil,"Hide cancels pending hover preview")
        print("PASS: Quick Look text preview, owner isolation, close and delayed-hover cancellation; isolated file only")
    }
    private func close() { cancelHover(); popover?.close(); interactiveView?.close(); interactiveView = nil; interactiveOwner = nil; popover = nil }
    func popoverDidClose(_ notification: Notification) { guard notification.object as? NSPopover === popover else { return }; interactiveView?.close(); interactiveView = nil; interactiveOwner = nil; popover = nil }
}
