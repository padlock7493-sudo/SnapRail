import SwiftUI

struct FlowLayout: Layout {
  var spacing: CGFloat = 6
  func sizeThatFits(proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) -> CGSize {
    arrange(width: proposal.width ?? 1000, subviews: subviews).size
  }
  func placeSubviews(
    in bounds: CGRect, proposal: ProposedViewSize, subviews: Subviews, cache: inout ()
  ) {
    let result = arrange(width: bounds.width, subviews: subviews)
    for (index, point) in result.points.enumerated() {
      subviews[index].place(
        at: CGPoint(x: bounds.minX + point.x, y: bounds.minY + point.y), proposal: .unspecified)
    }
  }
  func arrange(width: CGFloat, subviews: Subviews) -> (size: CGSize, points: [CGPoint]) {
    var x: CGFloat = 0
    var y: CGFloat = 0
    var row: CGFloat = 0
    var points: [CGPoint] = []
    for view in subviews {
      let size = view.sizeThatFits(.unspecified)
      if x + size.width > width && x > 0 {
        x = 0
        y += row + spacing
        row = 0
      }
      points.append(CGPoint(x: x, y: y))
      x += size.width + spacing
      row = max(row, size.height)
    }
    return (CGSize(width: width, height: y + row), points)
  }
}
