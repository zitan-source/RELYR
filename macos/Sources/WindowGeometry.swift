import Foundation
import CoreGraphics

enum WindowCommand: String, Codable, CaseIterable {
    case maximize, toggleMaximize, minimize, restoreOrMinimize, close, closeSurface, snapLeft, snapRight
    case monitorLeft, monitorRight, monitorUp, monitorDown, toggleMinimizeAll, minimizeAll, restoreAll
    case spaceLeft, spaceRight
    case space1, space2, space3, space4, space5, space6, space7, space8, space9, space10, space11, space12, space13, space14, space15, space16
    var spaceNumber: Int? { rawValue.hasPrefix("space") ? Int(rawValue.dropFirst(5)) : nil }
    var isSpaceMove: Bool { self == .spaceLeft || self == .spaceRight || spaceNumber != nil }
    var title: String {
        if let number = spaceNumber { return "ウィンドウをデスクトップ\(number)へ移動" }
        switch self {
        case .spaceLeft: return "ウィンドウを左のデスクトップへ移動"
        case .spaceRight: return "ウィンドウを右のデスクトップへ移動"
        case .space1,.space2,.space3,.space4,.space5,.space6,.space7,.space8,.space9,.space10,.space11,.space12,.space13,.space14,.space15,.space16: return "ウィンドウをデスクトップへ移動"
        case .maximize: return "最大化"
        case .toggleMaximize: return "最大化／元に戻す"
        case .minimize: return "最小化"
        case .restoreOrMinimize: return "復元／最小化"
        case .closeSurface: return "画面を閉じる（Finder・全画面対応）"
        case .close: return "ウィンドウを閉じる（赤い×）"
        case .snapLeft: return "左半分に配置"
        case .snapRight: return "右半分に配置"
        case .monitorLeft: return "左のモニターへ移動"
        case .monitorRight: return "右のモニターへ移動"
        case .monitorUp: return "上のモニターへ移動"
        case .monitorDown: return "下のモニターへ移動"
        case .minimizeAll: return "すべてのウィンドウを最小化"
        case .restoreAll: return "RELYRで最小化したウィンドウを復元"
        case .toggleMinimizeAll: return "すべて最小化／元に戻す"
        }
    }
}
enum WindowTargetMode: String, Codable, CaseIterable {
    case active, underPointer
    var title: String { self == .active ? "使用中のウィンドウ" : "カーソル下のウィンドウ" }
}

/// Geometry uses Quartz's top-left coordinate system, including negative monitor origins.
enum WindowGeometry {
    static func valid(_ frame: CGRect) -> Bool {
        [frame.minX,frame.minY,frame.width,frame.height].allSatisfy { $0.isFinite } && frame.width > 0 && frame.height > 0
    }
    static func near(_ a: CGRect, _ b: CGRect) -> Bool {
        abs(a.minX-b.minX) <= 2 && abs(a.minY-b.minY) <= 2 && abs(a.width-b.width) <= 2 && abs(a.height-b.height) <= 2
    }
    static func screen(for frame: CGRect, in areas: [CGRect]) -> Int? {
        guard !areas.isEmpty else { return nil }
        func area(_ a: CGRect) -> CGFloat { let intersection = frame.intersection(a); return intersection.isNull ? 0 : intersection.width * intersection.height }
        func distance(_ a: CGRect) -> CGFloat { pow(frame.midX-a.midX,2) + pow(frame.midY-a.midY,2) }
        return areas.indices.max { lhs,rhs in area(areas[lhs]) == area(areas[rhs]) ? distance(areas[lhs]) > distance(areas[rhs]) : area(areas[lhs]) < area(areas[rhs]) }
    }
    static func adjacent(_ index: Int, command: WindowCommand, areas: [CGRect]) -> Int? {
        guard areas.indices.contains(index) else { return nil }
        let source = areas[index]
        return areas.indices.filter { i in
            guard i != index else { return false }
            switch command {
            case .monitorLeft: return areas[i].midX < source.midX - 1
            case .monitorRight: return areas[i].midX > source.midX + 1
            case .monitorUp: return areas[i].midY < source.midY - 1
            case .monitorDown: return areas[i].midY > source.midY + 1
            default: return false
            }
        }.min { pow(areas[$0].midX-source.midX,2)+pow(areas[$0].midY-source.midY,2) < pow(areas[$1].midX-source.midX,2)+pow(areas[$1].midY-source.midY,2) }
    }
    static func relocated(_ frame: CGRect, from source: CGRect, to target: CGRect) -> CGRect {
        let width = min(frame.width,target.width), height = min(frame.height,target.height)
        let x = source.width > frame.width ? (frame.minX-source.minX)/(source.width-frame.width) : 0
        let y = source.height > frame.height ? (frame.minY-source.minY)/(source.height-frame.height) : 0
        return CGRect(x: target.minX + max(0,min(1,x))*(target.width-width), y: target.minY + max(0,min(1,y))*(target.height-height), width: width, height: height)
    }
}
