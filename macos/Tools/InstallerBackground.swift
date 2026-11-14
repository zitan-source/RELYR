import AppKit
let size = NSSize(width:640,height:400)
let image = NSImage(size:size)
image.lockFocus()
NSColor(calibratedWhite:0.96,alpha:1).setFill()
NSRect(origin:.zero,size:size).fill()
func text(_ text:String,_ y:CGFloat,_ font:NSFont,_ color:NSColor) {
 let style = NSMutableParagraphStyle(); style.alignment = .center
 (text as NSString).draw(in:NSRect(x:24,y:y,width:592,height:45),withAttributes:[.font:font,.foregroundColor:color,.paragraphStyle:style])
}
text("RELYR",320,.systemFont(ofSize:30,weight:.bold),NSColor(calibratedWhite:0.12,alpha:1))
text("アプリを右のフォルダへドラッグ",278,.systemFont(ofSize:18,weight:.medium),.darkGray)
let arrow = NSBezierPath(); arrow.move(to:NSPoint(x:285,y:202)); arrow.line(to:NSPoint(x:355,y:202)); arrow.move(to:NSPoint(x:341,y:216)); arrow.line(to:NSPoint(x:355,y:202)); arrow.line(to:NSPoint(x:341,y:188)); arrow.lineWidth = 3
NSColor(calibratedRed:0.08,green:0.55,blue:0.47,alpha:1).setStroke(); arrow.stroke()
text("コピー後は「アプリケーション」から起動",65,.systemFont(ofSize:15),.darkGray)
image.unlockFocus()
let bitmap = NSBitmapImageRep(data:image.tiffRepresentation!)!
try bitmap.representation(using:.png,properties:[:])!.write(to:URL(fileURLWithPath:CommandLine.arguments[1]))
