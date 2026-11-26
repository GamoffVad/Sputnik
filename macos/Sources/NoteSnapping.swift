import Foundation

enum NoteSnapping {
    static func snap(_ moving: CGRect, to neighbours: [CGRect]) -> CGRect {
        var dx: CGFloat = 0, dy: CGFloat = 0
        var bestX: CGFloat = 10.001, bestY: CGFloat = 10.001
        func x(_ delta: CGFloat) { if abs(delta) < bestX { bestX = abs(delta); dx = delta } }
        func y(_ delta: CGFloat) { if abs(delta) < bestY { bestY = abs(delta); dy = delta } }
        for other in neighbours {
            let verticalGap = max(0, max(other.minY - moving.maxY, moving.minY - other.maxY))
            let horizontalGap = max(0, max(other.minX - moving.maxX, moving.minX - other.maxX))
            if verticalGap <= 48 {
                x(other.minX - moving.minX)
                x(other.maxX - moving.maxX)
                x(other.midX - moving.midX)
            }
            if horizontalGap <= 48 {
                y(other.minY - moving.minY)
                y(other.maxY - moving.maxY)
                y(other.midY - moving.midY)
            }
            if verticalGap == 0 {
                x(other.maxX + 12 - moving.minX)
                x(other.minX - 12 - moving.maxX)
            }
            if horizontalGap == 0 {
                y(other.maxY + 12 - moving.minY)
                y(other.minY - 12 - moving.maxY)
            }
        }
        return moving.offsetBy(dx: dx, dy: dy)
    }
}
