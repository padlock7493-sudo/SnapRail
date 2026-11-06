import AppKit

struct Mark: Codable, Equatable {
  var tool: Int
  var points: [NSPoint]
  var ink: String
  var width: CGFloat
  var text = ""
  var fontSize: CGFloat = 24
  var color: NSColor { NSColor(hex: ink) }
  init(
    tool: Int, points: [NSPoint], color: NSColor, width: CGFloat, text: String = "",
    fontSize: CGFloat = 24
  ) {
    self.tool = tool
    self.points = points
    ink = color.hex
    self.width = width
    self.text = text
    self.fontSize = fontSize
  }
}
struct AnnotationDocument: Codable {
  var marks: [Mark] = []
  func validate() throws {
    guard marks.count <= 10_000 else { throw appError("标注数量超过安全限制") }
    for mark in marks {
      guard (0...5).contains(mark.tool), !mark.points.isEmpty,
        mark.width.isFinite, (0.5...64).contains(mark.width),
        mark.fontSize.isFinite, (8...192).contains(mark.fontSize),
        mark.ink.count == 6, UInt32(mark.ink, radix: 16) != nil,
        mark.points.allSatisfy({ $0.x.isFinite && $0.y.isFinite })
      else {
        throw appError("标注资料包含无效数值")
      }
    }
  }
}
struct EditResult {
  let image: NSImage
  let original: NSImage
  let document: AnnotationDocument
}
