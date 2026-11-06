import AppKit
import ScreenCaptureKit

struct DesktopSnapshot {
  let image: NSImage
  let frame: NSRect  // AppKit global coordinates, origin bottom-left
  let windows: [NSRect]
  func crop(_ globalRect: NSRect) throws -> NSImage {
    let scale = image.pixels.width / frame.width
    return try image.cropped(
      to: NSRect(
        x: (globalRect.minX - frame.minX) * scale,
        y: (globalRect.minY - frame.minY) * scale, width: globalRect.width * scale,
        height: globalRect.height * scale))
  }
}
enum ScreenGrabber {
  @MainActor static func desktop() async throws -> DesktopSnapshot {
    let screens = NSScreen.screens
    guard !screens.isEmpty else { throw appError("没有可用的显示器") }
    let frame = screens.map(\.frame).reduce(NSRect.null) { $0.union($1) }
    let scale = screens.map(\.backingScaleFactor).max() ?? 1
    guard frame.width * frame.height * scale * scale <= 100_000_000 else {
      throw appError("屏幕总尺寸过大，请临时减少显示器或分辨率后截图。")
    }
    var captures: [(NSRect, CGImage)] = []
    if #available(macOS 14.0, *) {
      let content = try await SCShareableContent.excludingDesktopWindows(
        false, onScreenWindowsOnly: true)
      for screen in screens {
        guard let display = content.displays.first(where: { $0.displayID == screen.displayID })
        else { continue }
        let filter = SCContentFilter(display: display, excludingWindows: [])
        let config = SCStreamConfiguration()
        config.width = Int(screen.frame.width * screen.backingScaleFactor)
        config.height = Int(screen.frame.height * screen.backingScaleFactor)
        config.showsCursor = false
        let image = try await SCScreenshotManager.captureImage(
          contentFilter: filter, configuration: config)
        captures.append((screen.frame, image))
      }
    } else {
      for (index, screen) in screens.enumerated() {
        let image = try await commandCapture(arguments: ["-D", String(index + 1)])
        var rect = NSRect(origin: .zero, size: image.size)
        if let cg = image.cgImage(forProposedRect: &rect, context: nil, hints: nil) {
          captures.append((screen.frame, cg))
        }
      }
    }
    guard captures.count == screens.count else { throw appError("部分显示器截图失败，请检查屏幕录制权限。") }
    guard
      let bitmap = NSBitmapImageRep(
        bitmapDataPlanes: nil, pixelsWide: Int(frame.width * scale),
        pixelsHigh: Int(frame.height * scale), bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true,
        isPlanar: false, colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0),
      let context = NSGraphicsContext(bitmapImageRep: bitmap)
    else { throw appError("无法分配截图内存") }
    NSGraphicsContext.saveGraphicsState()
    NSGraphicsContext.current = context
    for (screen, cg) in captures {
      NSImage(cgImage: cg, size: screen.size).draw(
        in: NSRect(
          x: (screen.minX - frame.minX) * scale, y: (screen.minY - frame.minY) * scale,
          width: screen.width * scale, height: screen.height * scale))
    }
    NSGraphicsContext.restoreGraphicsState()
    let image = NSImage(size: NSSize(width: frame.width * scale, height: frame.height * scale))
    image.addRepresentation(bitmap)
    let primaryTop = NSScreen.screens.first?.frame.maxY ?? 0
    let windowInfo =
      CGWindowListCopyWindowInfo([.optionOnScreenOnly, .excludeDesktopElements], kCGNullWindowID)
      as? [[String: Any]] ?? []
    let windows = windowInfo.compactMap { info -> NSRect? in
      guard (info[kCGWindowLayer as String] as? Int ?? 0) == 0,
        (info[kCGWindowOwnerPID as String] as? Int32) != ProcessInfo.processInfo.processIdentifier,
        let dict = info[kCGWindowBounds as String] as? [String: Any],
        let rect = CGRect(dictionaryRepresentation: dict as CFDictionary), rect.width > 30,
        rect.height > 30
      else { return nil }
      return NSRect(x: rect.minX, y: primaryTop - rect.maxY, width: rect.width, height: rect.height)
    }
    return DesktopSnapshot(image: image, frame: frame, windows: windows)
  }
  @MainActor static func region(_ quartzRect: NSRect) async throws -> NSImage {
    if #available(macOS 14.0, *) {
      let snapshot = try await desktop()
      let top = NSScreen.screens.first?.frame.maxY ?? 0
      return try snapshot.crop(
        NSRect(
          x: quartzRect.minX, y: top - quartzRect.maxY, width: quartzRect.width,
          height: quartzRect.height))
    }
    return try await commandCapture(arguments: [
      "-R",
      "\(Int(quartzRect.minX)),\(Int(quartzRect.minY)),\(Int(quartzRect.width)),\(Int(quartzRect.height))",
    ])
  }
  static func commandCapture(arguments: [String]) async throws -> NSImage {
    let file = FileManager.default.temporaryDirectory.appendingPathComponent(
      "SnapRail-\(UUID().uuidString).png")
    defer { try? FileManager.default.removeItem(at: file) }
    try await withCheckedThrowingContinuation { (continuation: CheckedContinuation<Void, Error>) in
      let process = Process()
      process.executableURL = URL(fileURLWithPath: "/usr/sbin/screencapture")
      process.arguments = ["-x", "-t", "png"] + arguments + [file.path]
      process.standardError = FileHandle.nullDevice
      process.terminationHandler = { result in
        if result.terminationStatus == 0 {
          continuation.resume()
        } else {
          continuation.resume(throwing: appError("截图失败，请检查屏幕录制权限。"))
        }
      }
      do { try process.run() } catch { continuation.resume(throwing: error) }
    }
    guard let image = NSImage(contentsOf: file) else { throw appError("截图文件无效") }
    return image
  }
}

final class CaptureController {
  let snapshot: DesktopSnapshot
  let canvas: AnnotationView
  let overlay: FloatingPanel
  let toolbar: AnnotationToolbar
  var completed: ((EditResult) -> Bool)?
  var cancelled: (() -> Void)?
  var scrollRequested: ((NSRect) -> Void)?
  var ocrRequested: ((NSImage) -> Void)?
  init(snapshot: DesktopSnapshot) {
    self.snapshot = snapshot
    canvas = AnnotationView(image: snapshot.image)
    canvas.captureMode = true
    canvas.selection = .zero
    overlay = FloatingPanel(
      contentRect: snapshot.frame, styleMask: [.borderless], backing: .buffered, defer: false)
    toolbar = AnnotationToolbar(canvas: canvas, capture: true)
    overlay.isReleasedWhenClosed = false
    overlay.level = NSWindow.Level(rawValue: NSWindow.Level.screenSaver.rawValue + 1)
    overlay.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
    overlay.contentView = canvas
    overlay.isOpaque = true
    overlay.backgroundColor = .black
    toolbar.frame = NSRect(x: 0, y: 0, width: 790, height: 94)
    toolbar.isHidden = true
    canvas.addSubview(toolbar)
    canvas.selectionChanged = { [weak self] in self?.positionToolbar() }
    canvas.windowAtPoint = { point in
      guard
        let rect = snapshot.windows.first(where: { $0.contains(point) })?.intersection(
          snapshot.frame)
      else { return nil }
      return NSRect(
        x: (rect.minX - snapshot.frame.minX) / snapshot.frame.width,
        y: (rect.minY - snapshot.frame.minY) / snapshot.frame.height,
        width: rect.width / snapshot.frame.width, height: rect.height / snapshot.frame.height)
    }
    toolbar.confirm = { [weak self] in
      guard let self, let result = self.canvas.result() else { return }
      if self.completed?(result) == true { self.close() }
    }
    toolbar.cancel = { [weak self] in
      self?.close()
      self?.cancelled?()
    }
    toolbar.saveAs = { [weak self] in
      guard let image = self?.canvas.rendered() else { return }
      self?.temporarilyLower { exportImage(image) }
    }
    toolbar.fullScreen = { [weak self] in self?.selectScreen() }
    toolbar.ocr = { [weak self] in
      guard let self, let image = self.canvas.rendered() else { return }
      self.temporarilyLower { self.ocrRequested?(image) }
    }
    toolbar.scroll = { [weak self] in self?.beginScroll() }
    canvas.finish = toolbar.confirm
    canvas.cancel = toolbar.cancel
    canvas.saveAs = toolbar.saveAs
    canvas.fullScreen = toolbar.fullScreen
  }
  func show() {
    NSApp.activate(ignoringOtherApps: true)
    overlay.makeKeyAndOrderFront(nil)
    overlay.makeFirstResponder(canvas)
    NSCursor.crosshair.push()
  }
  func close() {
    toolbar.isHidden = true
    overlay.orderOut(nil)
    NSCursor.pop()
  }
  func temporarilyLower(_ action: () -> Void) {
    let level = overlay.level
    overlay.level = .normal
    action()
    overlay.level = level
  }
  func selectScreen() {
    guard
      let screen = NSScreen.screens.first(where: { $0.frame.contains(NSEvent.mouseLocation) })
        ?? NSScreen.main
    else { return }
    let rect = screen.frame.intersection(snapshot.frame)
    canvas.selection = NSRect(
      x: (rect.minX - snapshot.frame.minX) / snapshot.frame.width,
      y: (rect.minY - snapshot.frame.minY) / snapshot.frame.height,
      width: rect.width / snapshot.frame.width, height: rect.height / snapshot.frame.height)
    canvas.needsDisplay = true
    positionToolbar()
  }
  func positionToolbar() {
    guard canvas.selection.width * snapshot.frame.width > 3,
      canvas.selection.height * snapshot.frame.height > 3
    else {
      toolbar.isHidden = true
      return
    }
    let crop = canvas.displayRect(canvas.selection).offsetBy(
      dx: snapshot.frame.minX, dy: snapshot.frame.minY)
    let screen =
      NSScreen.screens.first(where: { $0.frame.contains(NSPoint(x: crop.midX, y: crop.midY)) })
      ?? NSScreen.main!
    let work = screen.visibleFrame
    let width = min(790, work.width - 16)
    var y = crop.minY - 104
    if y < work.minY { y = min(work.maxY - 104, crop.maxY + 10) }
    let x = min(work.maxX - width - 8, max(work.minX + 8, crop.maxX - width))
    toolbar.frame = NSRect(
      x: x - snapshot.frame.minX, y: y - snapshot.frame.minY, width: width, height: 94)
    toolbar.isHidden = false
  }
  func beginScroll() {
    let selection = canvas.selection
    let frame = snapshot.frame
    let top = NSScreen.screens.first?.frame.maxY ?? 0
    let region = NSRect(
      x: frame.minX + selection.minX * frame.width,
      y: top - (frame.minY + selection.maxY * frame.height), width: selection.width * frame.width,
      height: selection.height * frame.height)
    guard region.width >= 32, region.height >= 64 else { return }
    close()
    scrollRequested?(region)
  }
}
