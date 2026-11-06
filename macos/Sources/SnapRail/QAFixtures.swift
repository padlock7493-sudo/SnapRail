import AppKit
import Vision

func demoImage(size: NSSize = NSSize(width: 1600, height: 1000)) -> NSImage {
  let bitmap = NSBitmapImageRep(
    bitmapDataPlanes: nil, pixelsWide: Int(size.width), pixelsHigh: Int(size.height),
    bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false,
    colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0)!
  NSGraphicsContext.saveGraphicsState()
  NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: bitmap)
  let transform = NSAffineTransform()
  transform.scaleX(by: size.width / 1600, yBy: size.height / 1000)
  transform.concat()
  NSColor(hex: "EAF3EF").setFill()
  NSRect(x: 0, y: 0, width: 1600, height: 1000).fill()
  func text(
    _ value: String, _ x: CGFloat, _ y: CGFloat, _ size: CGFloat, _ color: String = "253A40"
  ) {
    (value as NSString).draw(
      at: NSPoint(x: x, y: y),
      withAttributes: [
        .font: NSFont.systemFont(ofSize: size, weight: .medium),
        .foregroundColor: NSColor(hex: color),
      ])
  }
  text("截一张，随手挂一下！", 100, 900, 36)
  for (x, title, color) in [(100.0, "灵感清单", "83BFA8"), (820.0, "配色备忘", "EDAE72")] {
    NSColor.white.setFill()
    NSRect(x: x, y: 200, width: 640, height: 650).fill()
    NSColor(hex: color).setFill()
    NSRect(x: x, y: 836, width: 640, height: 14).fill()
    text(title, x + 45, 730, 44)
  }
  text("•  记录一个好点子", 145, 620, 32, "53686B")
  text("•  截图标注重点", 145, 520, 32, "53686B")
  text("•  随时找回灵感", 145, 420, 32, "53686B")
  for (i, color) in ["83BAD3", "8BC6AD", "F1BA89", "C0ACD2"].enumerated() {
    NSColor(hex: color).setFill()
    NSRect(x: 865 + CGFloat(i) * 137, y: 470, width: 108, height: 150).fill()
    text("#" + color, 865 + CGFloat(i) * 137, 410, 19, "53686B")
  }
  text("演示内容 · 本地生成 · 无个人资料", 100, 95, 25, "53686B")
  text("SnapRail macOS", 100, 45, 26)
  NSGraphicsContext.restoreGraphicsState()
  let image = NSImage(size: size)
  image.addRepresentation(bitmap)
  return image
}
func makeDemoSnapshot() -> DesktopSnapshot {
  let screen = NSScreen.main!
  let frame = screen.frame
  let scale = screen.backingScaleFactor
  return DesktopSnapshot(
    image: demoImage(size: NSSize(width: frame.width * scale, height: frame.height * scale)),
    frame: frame, windows: [frame.insetBy(dx: frame.width * 0.07, dy: frame.height * 0.12)])
}

func demoScrollFrame(_ index: Int) -> NSImage {
  let width = 320
  let height = 1200
  let frameHeight = 240
  let start = min(index * 80, height - frameHeight)
  var bytes = [UInt8](repeating: 255, count: width * height * 4)
  var random: UInt64 = 123456
  for i in 0..<(width * height) {
    for c in 0..<3 {
      random = random &* 6_364_136_223_846_793_005 &+ 1
      bytes[i * 4 + c] = UInt8((random >> 32) & 255)
    }
  }
  return PixelBuffer(
    width: width, height: frameHeight,
    data: Array(bytes[(start * width * 4)..<((start + frameHeight) * width * 4)])
  ).image()!
}
