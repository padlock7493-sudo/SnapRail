import AppKit

enum StitchStatus: String { case added, unchanged, noMatch, limit }
struct PixelBuffer {
  let width: Int, height: Int
  var data: [UInt8]
  init(image: NSImage) throws {
    var rect = NSRect(origin: .zero, size: image.size)
    guard let cg = image.cgImage(forProposedRect: &rect, context: nil, hints: nil) else {
      throw appError("无法读取截图像素")
    }
    width = cg.width
    height = cg.height
    data = Array(repeating: 0, count: width * height * 4)
    let success = data.withUnsafeMutableBytes { raw -> Bool in
      guard
        let context = CGContext(
          data: raw.baseAddress, width: width, height: height, bitsPerComponent: 8,
          bytesPerRow: width * 4, space: CGColorSpaceCreateDeviceRGB(),
          bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue
            | CGBitmapInfo.byteOrder32Big.rawValue)
      else { return false }
      context.draw(cg, in: CGRect(x: 0, y: 0, width: width, height: height))
      return true
    }
    if !success { throw appError("无法分配像素内存") }
  }
  init(width: Int, height: Int, data: [UInt8]) {
    self.width = width
    self.height = height
    self.data = data
  }
  func image() -> NSImage? {
    guard let provider = CGDataProvider(data: Data(data) as CFData),
      let cg = CGImage(
        width: width, height: height, bitsPerComponent: 8, bitsPerPixel: 32, bytesPerRow: width * 4,
        space: CGColorSpaceCreateDeviceRGB(),
        bitmapInfo: CGBitmapInfo(
          rawValue: CGImageAlphaInfo.premultipliedLast.rawValue
            | CGBitmapInfo.byteOrder32Big.rawValue), provider: provider, decode: nil,
        shouldInterpolate: false, intent: .defaultIntent)
    else { return nil }
    return NSImage(cgImage: cg, size: NSSize(width: width, height: height))
  }
}
struct ScrollLimits {
  var frames = 80
  var height = 30_000
  var pixels = 50_000_000
}
final class ScrollStitcher {
  let limits: ScrollLimits
  let width: Int, frameHeight: Int
  private(set) var height: Int
  private(set) var frames = 1
  private var previous: [UInt8]
  private var strips: [[UInt8]]
  init(_ initial: NSImage, limits: ScrollLimits = ScrollLimits()) throws {
    self.limits = limits
    let buffer = try PixelBuffer(image: initial)
    width = buffer.width
    frameHeight = buffer.height
    height = buffer.height
    previous = buffer.data
    strips = [buffer.data]
    guard width >= 16, frameHeight >= 40, height <= limits.height, width * height <= limits.pixels
    else {
      throw appError("选区过小或超过 5000 万像素限制")
    }
  }
  func error(_ next: [UInt8], shift: Int, dense: Bool = false) -> Double {
    let overlap = frameHeight - shift
    var total = 0.0
    var samples = 0
    for y in stride(from: 5, to: overlap - 5, by: max(1, overlap / (dense ? 500 : 34))) {
      for x in stride(from: 6, to: width - 6, by: max(1, width / (dense ? 800 : 32))) {
        let a = ((y + shift) * width + x) * 4
        let b = (y * width + x) * 4
        for c in 0..<3 {
          total += Double(abs(Int(previous[a + c]) - Int(next[b + c])))
          samples += 1
        }
      }
    }
    return samples == 0 ? .infinity : total / Double(samples)
  }
  func append(_ image: NSImage) throws -> StitchStatus {
    let next = try PixelBuffer(image: image)
    guard next.width == width, next.height == frameHeight else { return .noMatch }
    guard next.data != previous else { return .unchanged }
    let maximum = frameHeight - max(32, frameHeight / 5)
    guard maximum >= 1 else { return .noMatch }
    var best = Double.infinity
    var shift = 0
    var errors = Array(repeating: Double.infinity, count: maximum + 1)
    for candidate in 1...maximum {
      if error(next.data, shift: candidate) > 3 { continue }
      let value = error(next.data, shift: candidate, dense: true)
      errors[candidate] = value
      if value < best {
        best = value
        shift = candidate
      }
    }
    guard shift > 0, best <= 2.5 else { return .noMatch }
    for candidate in 1...maximum where abs(candidate - shift) > 4 && errors[candidate] <= best + 0.3
    { return .noMatch }
    guard height + shift <= limits.height, width * (height + shift) <= limits.pixels,
      frames < limits.frames
    else {
      return .limit
    }
    strips.append(Array(next.data.suffix(width * shift * 4)))
    height += shift
    frames += 1
    previous = next.data
    return .added
  }
  func render() -> NSImage? {
    PixelBuffer(width: width, height: height, data: strips.flatMap { $0 }).image()
  }
}
