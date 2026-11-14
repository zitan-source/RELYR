import Foundation
@main struct TrackpadTests {
    static func main() {
        func points(_ count:Int,_ dx:Double = 0,_ dy:Double = 0) -> [TrackpadContact] {
            (0..<count).map { TrackpadContact(id:$0,x:0.25+Double($0)*0.1+dx,y:0.5+dy) }
        }
        for count in [3,4] {
            var tap = TrackpadRecognizer()
            precondition(tap.update(points(count),time:1) == nil)
            precondition(tap.update([],time:1.15) == (count == 3 ? 400 : 405))
            precondition(tap.update([],time:1.2) == nil)
            for (dx,dy,offset) in [(0.0,0.2,0),(0.0,-0.2,1),(-0.2,0.0,2),(0.2,0.0,3)] {
                var r = TrackpadRecognizer(); _ = r.update(points(count),time:1)
                precondition(r.update(points(count,dx,dy),time:1.2) == UInt16((count == 3 ? 401 : 406)+offset))
                precondition(r.update(points(count,dx,dy),time:1.25) == nil)
                precondition(r.update([],time:1.3) == nil)
            }
        }
        for (distance,code) in [(0.1,410),(0.4,411)] {
            var r = TrackpadRecognizer()
            _ = r.update([.init(id:0,x:0.3,y:0.5),.init(id:1,x:0.5,y:0.5)],time:1)
            precondition(r.update([.init(id:0,x:0.3,y:0.5),.init(id:1,x:0.3+distance,y:0.5)],time:1.1) == UInt16(code))
        }
        for (angle,code) in [(-0.6,412),(0.6,413)] {
            var r = TrackpadRecognizer()
            _ = r.update([.init(id:0,x:0.3,y:0.5),.init(id:1,x:0.5,y:0.5)],time:1)
            precondition(r.update([.init(id:0,x:0.3,y:0.5),.init(id:1,x:0.3+0.2*cos(angle),y:0.5+0.2*sin(angle))],time:1.1) == UInt16(code))
        }
        var r = TrackpadRecognizer(); _ = r.update(points(3),time:1)
        _ = r.update(points(3,0.05,0),time:1.1)
        precondition(r.update([],time:1.2) == nil,"Moving contact must not become a tap")
        _ = r.update(points(4),time:2); r.reset()
        precondition(r.update([],time:2.1) == nil,"Reset cannot emit a tap")
        _ = r.update(points(4),time:3)
        precondition(r.update(points(3,0.2,0),time:3.1) == nil,"Finger release cannot turn a 4-finger gesture into a 3-finger gesture")
        for (x,y,code) in [(0.08,0.92,414),(0.92,0.92,416),(0.08,0.08,418),(0.92,0.08,420)] {
            let point = [TrackpadContact(id:1,x:x,y:y)]
            var single = TrackpadRecognizer()
            precondition(single.update(point,time:1,doubleTapEnabled:{ _ in false }) == nil)
            precondition(single.update([],time:1.1,doubleTapEnabled:{ _ in false }) == UInt16(code),"Corner tap fires immediately when double tap is unassigned")
            var double = TrackpadRecognizer()
            _ = double.update(point,time:1)
            precondition(double.update([],time:1.1) == nil && double.pendingCornerDeadline != nil)
            _ = double.update(point,time:1.25)
            precondition(double.update([],time:1.34) == UInt16(code+1),"Two corner taps emit only the double action")
            precondition(double.flush(time:1.6) == nil,"Double tap cancels pending single")
            var delayed = TrackpadRecognizer()
            _ = delayed.update(point,time:1)
            _ = delayed.update([],time:1.1)
            precondition(delayed.flush(time:1.45) == nil && delayed.flush(time:1.47) == UInt16(code),"Single tap waits only when double is assigned")
        }
        var differentCorners = TrackpadRecognizer()
        _ = differentCorners.update([.init(id:1,x:0.08,y:0.92)],time:1)
        _ = differentCorners.update([],time:1.1)
        _ = differentCorners.update([.init(id:1,x:0.92,y:0.92)],time:1.2)
        _ = differentCorners.update([],time:1.3)
        precondition(differentCorners.flush(time:1.47) == 414 && differentCorners.flush(time:1.67) == 416,"Quick taps on different corners preserve both actions")
        var ordinary = TrackpadRecognizer()
        _ = ordinary.update([.init(id:1,x:0.5,y:0.5)],time:1)
        precondition(ordinary.update([],time:1.1) == nil,"Center tap stays native")
        _ = ordinary.update([.init(id:1,x:0.08,y:0.92)],time:2)
        _ = ordinary.update([.init(id:1,x:0.13,y:0.92)],time:2.1)
        precondition(ordinary.update([],time:2.2) == nil,"Pointer movement cancels corner tap")
        for (start,dx,dy,code) in [
            (TrackpadContact(id:0,x:0.36,y:0.06),0.0,0.27,422),
            (TrackpadContact(id:0,x:0.36,y:0.94),0.0,-0.27,423),
            (TrackpadContact(id:0,x:0.88,y:0.4),-0.27,0.0,424),
            (TrackpadContact(id:0,x:0.06,y:0.4),0.27,0.0,425)] {
            let second = TrackpadContact(id:1,x:start.x+(dy == 0 ? 0 : 0.2),y:start.y+(dx == 0 ? 0 : 0.2))
            var swipe = TrackpadRecognizer()
            _ = swipe.update([start,second],time:1)
            let moved = [start,second].map { TrackpadContact(id:$0.id,x:$0.x+dx,y:$0.y+dy) }
            precondition(swipe.update(moved,time:1.2) == UInt16(code),"Two-finger edge swipe emits direction once")
            precondition(swipe.update(moved,time:1.3) == nil)
        }
        var centerScroll = TrackpadRecognizer()
        _ = centerScroll.update([.init(id:0,x:0.3,y:0.5),.init(id:1,x:0.5,y:0.5)],time:1)
        precondition(centerScroll.update([.init(id:0,x:0.3,y:0.77),.init(id:1,x:0.5,y:0.77)],time:1.2) == nil,"Ordinary center scroll does not become edge swipe")
        for count in 1...2 {
            for (index, motion) in [(-0.1,0.0),(0.1,0.0),(-0.1,0.0),(0.1,0.0),
                                    (0.0,0.1),(0.0,-0.1),(0.0,0.1),(0.0,-0.1)].enumerated() {
                let origin = [(0.5,0.94),(0.5,0.06),(0.06,0.5),(0.94,0.5)][index/2]
                func frame(_ distance: Double) -> [TrackpadContact] {
                    (0..<count).map { i in
                        TrackpadContact(id:i,x:origin.0+motion.0*distance+(index < 4 ? Double(i)*0.12 : 0),
                                       y:origin.1+motion.1*distance+(index >= 4 ? Double(i)*0.12 : 0))
                    }
                }
                let code = UInt16(426+(count-1)*8+index)
                var edge = TrackpadRecognizer()
                precondition(edge.update(frame(0),time:1,edgeMapping:{ _ in true }) == nil)
                precondition(edge.update(frame(0.1),time:1.08,edgeMapping:{ _ in true }) == nil)
                precondition(edge.update(frame(1),time:1.18,edgeMapping:{ _ in true }) == code)
                precondition(edge.update(frame(1.6),time:1.22,edgeMapping:{ _ in true }) == nil,"Pulse is rate limited")
                precondition(edge.update(frame(1.6),time:1.32,edgeMapping:{ _ in true }) == code)
                precondition(edge.update(frame(0.85),time:1.46,edgeMapping:{ _ in true }) == (code ^ 1),"Direction can reverse on the locked edge")
                precondition(edge.update([],time:1.6,edgeMapping:{ _ in true }) == nil && !edge.ownsEdge)
                _ = edge.update(frame(0),time:2,edgeMapping:{ _ in false })
                precondition(edge.update(frame(1),time:2.2,edgeMapping:{ _ in false }) == code)
                precondition(edge.update(frame(1.8),time:2.4,edgeMapping:{ _ in false }) == nil,"Single action fires only once")
                edge.reset()
                precondition(edge.update([],time:2.5,edgeMapping:{ _ in true }) == nil)
            }
        }
        let corner = TrackpadContact(id:0,x:0.06,y:0.94)
        var cornerEdge = TrackpadRecognizer()
        _ = cornerEdge.update([corner],time:1)
        precondition(cornerEdge.update([.init(id:0,x:0.17,y:0.94)],time:1.2,edgeMapping:{ _ in false }) == 427,"Corner first motion selects the top edge")
        cornerEdge.reset()
        _ = cornerEdge.update([corner],time:2)
        precondition(cornerEdge.update([.init(id:0,x:0.06,y:0.82)],time:2.2,edgeMapping:{ _ in false }) == 431,"Corner first motion selects the left edge")
        var cancel = TrackpadRecognizer()
        _ = cancel.update([corner],time:1)
        _ = cancel.update([corner,.init(id:1,x:0.21,y:0.94)],time:1.05)
        _ = cancel.update([corner],time:1.10)
        precondition(cancel.update([.init(id:0,x:0.16,y:0.94),.init(id:1,x:0.31,y:0.94)],time:1.2,edgeMapping:{ _ in true }) == nil,"Lifting and rejoining does not restart edge recognition")
        var center = TrackpadRecognizer()
        _ = center.update(points(2),time:1)
        precondition(center.update(points(2,0.08,0),time:1.2,edgeMapping:{ _ in true }) == nil)
        precondition(!center.mayOwnScroll(273,width:0.12,assigned:{ _ in true }))
        var volume = TrackpadRecognizer()
        var distance = 0.0
        func slide(_ x: Double, _ y: Double, _ time: Double) {
            let action = volume.update([.init(id:0,x:x,y:y)],time:time,
                edgeMapping:{ _ in true },
                isEdgeVolume:{ $0 == 430 || $0 == 431 },
                edgeVolume:{ code, travel in distance += code == 430 ? travel : -travel })
            precondition(action == nil, "Volume never emits a discrete Action")
        }
        slide(0.05,0.2,1)
        slide(0.05,0.23,1.04)
        precondition(abs(distance - 0.03) < 0.0001, "Small slides respond immediately")
        slide(0.12,0.3,1.08)
        slide(0.12,0.3,1.2)
        precondition(abs(distance - 0.10) < 0.0001, "Volume follows total travel and stops at rest")
        slide(0.12,0.28,1.24)
        precondition(abs(distance - 0.08) < 0.0001, "Reversal subtracts travel")
        precondition(volume.update([],time:1.28) == nil && !volume.ownsEdge)
        for (startX, movedX) in [(0.10,0.10),(0.04,0.14)] {
            distance = 0
            slide(startX,0.2,2)
            slide(movedX,0.31,2.08)
            precondition(distance == 0 && !volume.adjustingVolume, "Volume starts and locks only in the narrow edge strip")
            _ = volume.update([],time:2.1)
        }
        slide(0.04,0.2,3)
        slide(0.04,0.25,3.04)
        slide(0.04,0.244,3.08)
        slide(0.04,0.25,3.12)
        precondition(abs(distance - 0.05) < 0.0001, "Touch jitter does not adjust volume")
        _ = volume.update([.init(id:0,x:0.04,y:0.25),.init(id:1,x:0.04,y:0.35)],time:3.16)
        precondition(!volume.adjustingVolume, "A changed contact count ends the cursor lock")
        _ = volume.update([],time:3.2)
        distance = 0
        slide(0.075,0.85,4)
        slide(0.094,0.826,4.02)
        precondition(abs(distance + 0.024) < 0.0001, "A fresh downward slide from the edge starts volume down despite inward drift")
        slide(0.15,0.766,4.04)
        precondition(abs(distance + 0.084) < 0.0001, "A downward stroke follows distance after moving inward")
        _ = volume.update([],time:4.08)
        distance = 0
        slide(0.16,0.85,5)
        slide(0.055,0.68,5.05)
        precondition(distance == 0 && !volume.adjustingVolume, "A stroke starting inside the pad cannot acquire volume control")
        print("PASS: legacy trackpad gestures and 16 edge directions, reversal, repeat timing, corner arbitration and cancellation; simulated frames only")
    }
}
