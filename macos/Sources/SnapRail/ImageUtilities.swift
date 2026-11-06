import AppKit

extension NSImage {
  func pngData() throws -> Data {
    guard let tiff = tiffRepresentation, let bitmap = NSBitmapImageRep(data: tiff),
      let data = bitmap.representation(using: .png, properties: [:])
    else { throw appError("无法生成 PNG 图片") }
    return data
  }
  var pixels: NSSize {
    var rect = NSRect(origin: .zero, size: size)
    guard let cg = cgImage(forProposedRect: &rect, context: nil, hints: nil) else { return size }
    return NSSize(width: cg.width, height: cg.height)
  }
}

extension NSImage {
  /// Pixel-coordinate crop with AppKit's bottom-left origin; output dimensions are exact pixels.
  func cropped(to requested: NSRect) throws -> NSImage {
    let size = pixels
    let crop = requested.integral.intersection(NSRect(origin: .zero, size: size))
    guard crop.width >= 1, crop.height >= 1, crop.width * crop.height <= 50_000_000,
      let bitmap = NSBitmapImageRep(
        bitmapDataPlanes: nil, pixelsWide: Int(crop.width), pixelsHigh: Int(crop.height),
        bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false,
        colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0),
      let context = NSGraphicsContext(bitmapImageRep: bitmap)
    else { throw appError("裁剪区域无效或超过像素限制") }
    NSGraphicsContext.saveGraphicsState()
    NSGraphicsContext.current = context
    context.imageInterpolation = .none
    draw(in: NSRect(x: -crop.minX, y: -crop.minY, width: size.width, height: size.height))
    NSGraphicsContext.restoreGraphicsState()
    let image = NSImage(size: crop.size)
    image.addRepresentation(bitmap)
    return image
  }
}
