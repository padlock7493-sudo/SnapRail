import AppKit

final class AnnotationView: NSView {
  let image: NSImage
  var tool = 6 {
    didSet {
      selected = nil
      needsDisplay = true
      changed?()
    }
  }
  var color = NSColor(hex: "E34B4B")
  var lineWidth: CGFloat = 4
  var fontSize: CGFloat = 24
  var marks: [Mark] = []
  var undoStates: [[Mark]] = [], redoStates: [[Mark]] = []
  var pending: Mark?
  var selected: Int?
  var selection = NSRect(x: 0, y: 0, width: 1, height: 1)
  var captureMode = false
  var initialMarks: [Mark] = []
  var isDirty: Bool {
    marks != initialMarks || selection != NSRect(x: 0, y: 0, width: 1, height: 1)
  }
  var undoCount: Int { undoStates.count }
  var changed: (() -> Void)?
  var finish: (() -> Void)?
  var cancel: (() -> Void)?
  var saveAs: (() -> Void)?
  var fullScreen: (() -> Void)?
  var selectionChanged: (() -> Void)?
  var windowAtPoint: ((NSPoint) -> NSRect?)?
  private var dragStart = NSPoint.zero
  private var initialSelection = NSRect.zero
  private var initialMark: Mark?
  private var resizeHandle = -1
  private var movingSelection = false
  private var mosaics: [String: NSImage] = [:]
  init(image: NSImage) {
    self.image = image
    super.init(frame: .zero)
  }
  required init?(coder: NSCoder) { fatalError("init(coder:) has not been implemented") }
  override var acceptsFirstResponder: Bool { true }
  var imageRect: NSRect {
    if captureMode { return bounds }
    let available = bounds.insetBy(dx: 18, dy: 18)
    let pixels = image.pixels
    let scale = min(
      available.width / max(pixels.width, 1), available.height / max(pixels.height, 1))
    let size = NSSize(width: pixels.width * scale, height: pixels.height * scale)
    return NSRect(
      x: bounds.midX - size.width / 2, y: bounds.midY - size.height / 2, width: size.width,
      height: size.height)
  }
  func displayRect(_ normalized: NSRect) -> NSRect {
    let rect = imageRect
    return NSRect(
      x: rect.minX + normalized.minX * rect.width, y: rect.minY + normalized.minY * rect.height,
      width: normalized.width * rect.width, height: normalized.height * rect.height)
  }
  override func draw(_ dirtyRect: NSRect) {
    NSColor(calibratedWhite: 0.14, alpha: 1).setFill()
    bounds.fill()
    image.draw(in: imageRect)
    let crop = displayRect(selection)
    if captureMode {
      let mask = NSBezierPath(rect: bounds)
      mask.appendRect(crop)
      mask.windingRule = .evenOdd
      NSColor.black.withAlphaComponent(0.48).setFill()
      mask.fill()
    }
    NSGraphicsContext.saveGraphicsState()
    NSBezierPath(rect: crop).addClip()
    for mark in marks { paint(mark, in: imageRect) }
    if let pending { paint(pending, in: imageRect) }
    NSGraphicsContext.restoreGraphicsState()
    if captureMode || tool == 6 {
      NSColor.white.setStroke()
      let path = NSBezierPath(rect: crop)
      path.lineWidth = 1
      path.stroke()
      if selection.width > 0 && tool == 6 {
        for point in handles(crop) {
          NSColor.white.setFill()
          NSColor(hex: "126B64").setStroke()
          let handle = NSBezierPath(
            rect: NSRect(x: point.x - 3, y: point.y - 3, width: 6, height: 6))
          handle.fill()
          handle.stroke()
        }
      }
    }
    if captureMode && selection.width > 0 {
      let size = image.pixels
      let label =
        "\(Int((selection.width * size.width).rounded())) × \(Int((selection.height * size.height).rounded())) px"
        as NSString
      let location = NSPoint(x: crop.minX + 6, y: min(bounds.maxY - 25, crop.maxY + 8))
      NSColor.black.withAlphaComponent(0.85).setFill()
      NSRect(x: location.x - 5, y: location.y - 3, width: 150, height: 24).fill()
      label.draw(
        at: location,
        withAttributes: [
          .font: NSFont.monospacedSystemFont(ofSize: 12, weight: .medium),
          .foregroundColor: NSColor.white,
        ])
    }
    if let selected, marks.indices.contains(selected) {
      NSColor(hex: "126B64").setStroke()
      let path = NSBezierPath(
        rect: markBounds(marks[selected], in: imageRect).insetBy(dx: -5, dy: -5))
      path.setLineDash([4, 3], count: 2, phase: 0)
      path.lineWidth = 1
      path.stroke()
    }
  }
  func handles(_ box: NSRect) -> [NSPoint] {
    [
      NSPoint(x: box.minX, y: box.minY), NSPoint(x: box.midX, y: box.minY),
      NSPoint(x: box.maxX, y: box.minY), NSPoint(x: box.maxX, y: box.midY),
      NSPoint(x: box.maxX, y: box.maxY), NSPoint(x: box.midX, y: box.maxY),
      NSPoint(x: box.minX, y: box.maxY), NSPoint(x: box.minX, y: box.midY),
    ]
  }
  func normalized(_ event: NSEvent) -> NSPoint {
    let point = convert(event.locationInWindow, from: nil)
    let rect = imageRect
    return NSPoint(
      x: min(1, max(0, (point.x - rect.minX) / max(rect.width, 1))),
      y: min(1, max(0, (point.y - rect.minY) / max(rect.height, 1))))
  }
  func checkpoint() {
    undoStates.append(marks)
    if undoStates.count > 100 { undoStates.removeFirst() }
    redoStates = []
  }
  override func mouseDown(with event: NSEvent) {
    window?.makeFirstResponder(self)
    guard imageRect.contains(convert(event.locationInWindow, from: nil)) else { return }
    let point = normalized(event)
    dragStart = point
    initialSelection = selection
    initialMark = nil
    if tool == 6 {
      let local = convert(event.locationInWindow, from: nil)
      resizeHandle =
        handles(displayRect(selection)).firstIndex { hypot($0.x - local.x, $0.y - local.y) < 8 }
        ?? -1
      movingSelection = resizeHandle == -1 && selection.contains(point) && selection.width < 0.99
      if !movingSelection && resizeHandle == -1 { selection = NSRect(origin: point, size: .zero) }
      needsDisplay = true
      return
    }
    guard selection.contains(point) else { return }
    if tool == 7 {
      let local = convert(event.locationInWindow, from: nil)
      selected = marks.indices.reversed().first {
        markBounds(marks[$0], in: imageRect).insetBy(dx: -7, dy: -7).contains(local)
      }
      if let selected {
        if event.clickCount == 2 && marks[selected].tool == 4 {
          editText(at: point, index: selected)
          return
        }
        initialMark = marks[selected]
        color = marks[selected].color
        lineWidth = marks[selected].width
        fontSize = marks[selected].fontSize
      }
      needsDisplay = true
      changed?()
      return
    }
    if tool == 4 {
      editText(at: point, index: nil)
    } else {
      pending = Mark(
        tool: tool, points: [point, point], color: color, width: lineWidth, fontSize: fontSize)
    }
  }
  override func mouseDragged(with event: NSEvent) {
    let point = normalized(event)
    if tool == 6 {
      if movingSelection {
        selection.origin.x = min(
          1 - selection.width, max(0, initialSelection.origin.x + point.x - dragStart.x))
        selection.origin.y = min(
          1 - selection.height, max(0, initialSelection.origin.y + point.y - dragStart.y))
      } else if resizeHandle >= 0 {
        var left = initialSelection.minX
        var right = initialSelection.maxX
        var bottom = initialSelection.minY
        var top = initialSelection.maxY
        if [0, 6, 7].contains(resizeHandle) { left = min(point.x, right - 0.002) }
        if [2, 3, 4].contains(resizeHandle) { right = max(point.x, left + 0.002) }
        if [0, 1, 2].contains(resizeHandle) { bottom = min(point.y, top - 0.002) }
        if [4, 5, 6].contains(resizeHandle) { top = max(point.y, bottom + 0.002) }
        selection = NSRect(x: left, y: bottom, width: right - left, height: top - bottom)
      } else {
        selection = NSRect(
          x: min(point.x, dragStart.x), y: min(point.y, dragStart.y),
          width: abs(point.x - dragStart.x), height: abs(point.y - dragStart.y))
      }
      needsDisplay = true
      selectionChanged?()
      return
    }
    if tool == 7, let index = selected, let initialMark {
      let box = normalizedBounds(initialMark)
      let dx = min(selection.maxX - box.maxX, max(selection.minX - box.minX, point.x - dragStart.x))
      let dy = min(selection.maxY - box.maxY, max(selection.minY - box.minY, point.y - dragStart.y))
      marks[index].points = initialMark.points.map { NSPoint(x: $0.x + dx, y: $0.y + dy) }
      needsDisplay = true
      return
    }
    guard pending != nil else { return }
    let bounded = NSPoint(
      x: min(selection.maxX, max(selection.minX, point.x)),
      y: min(selection.maxY, max(selection.minY, point.y)))
    if tool == 3 { pending?.points.append(bounded) } else { pending?.points[1] = bounded }
    needsDisplay = true
  }
  override func mouseUp(with event: NSEvent) {
    if tool == 6 && selection.width * imageRect.width < 3
      && selection.height * imageRect.height < 3, let box = windowAtPoint?(NSEvent.mouseLocation)
    {
      selection = box
    }
    if let mark = pending {
      checkpoint()
      marks.append(mark)
    }
    if let index = selected, let initialMark, initialMark != marks[index] {
      let current = marks[index]
      marks[index] = initialMark
      checkpoint()
      marks[index] = current
    }
    pending = nil
    initialMark = nil
    resizeHandle = -1
    needsDisplay = true
    changed?()
    selectionChanged?()
  }
  override func rightMouseDown(with event: NSEvent) {
    if selected != nil {
      selected = nil
      needsDisplay = true
    } else {
      cancel?()
    }
  }
  override func keyDown(with event: NSEvent) {
    if event.keyCode == 53 {
      if selected != nil {
        selected = nil
        needsDisplay = true
      } else {
        cancel?()
      }
    } else if event.keyCode == 36 {
      finish?()
    } else if [51, 117].contains(event.keyCode) {
      deleteSelection()
    } else if [123, 124, 125, 126].contains(event.keyCode) {
      let step = event.modifierFlags.contains(.shift) ? 10.0 : 1.0
      let dx =
        event.keyCode == 123
        ? -step / image.pixels.width : event.keyCode == 124 ? step / image.pixels.width : 0
      let dy =
        event.keyCode == 125
        ? -step / image.pixels.height : event.keyCode == 126 ? step / image.pixels.height : 0
      if let selected {
        checkpoint()
        marks[selected].points = marks[selected].points.map {
          NSPoint(x: min(1, max(0, $0.x + dx)), y: min(1, max(0, $0.y + dy)))
        }
      } else {
        selection.origin.x = min(1 - selection.width, max(0, selection.origin.x + dx))
        selection.origin.y = min(1 - selection.height, max(0, selection.origin.y + dy))
        selectionChanged?()
      }
      needsDisplay = true
      changed?()
    } else {
      super.keyDown(with: event)
    }
  }
  @objc func undo(_ sender: Any?) {
    guard let state = undoStates.popLast() else { return }
    redoStates.append(marks)
    marks = state
    selected = nil
    needsDisplay = true
    changed?()
  }
  @objc func redo(_ sender: Any?) {
    guard let state = redoStates.popLast() else { return }
    undoStates.append(marks)
    marks = state
    selected = nil
    needsDisplay = true
    changed?()
  }
  func deleteSelection() {
    if let selected {
      checkpoint()
      marks.remove(at: selected)
      self.selected = nil
      needsDisplay = true
      changed?()
    }
  }
  func style(ink: String? = nil, width: CGFloat? = nil, size: CGFloat? = nil) {
    if let selected {
      checkpoint()
      if let ink { marks[selected].ink = ink }
      if let width { marks[selected].width = width }
      if let size { marks[selected].fontSize = size }
    }
    if let ink { color = NSColor(hex: ink) }
    if let width { lineWidth = width }
    if let size { fontSize = size }
    needsDisplay = true
    changed?()
  }
  override func performKeyEquivalent(with event: NSEvent) -> Bool {
    let flags = event.modifierFlags
    guard flags.contains(.command) || flags.contains(.control) else {
      return super.performKeyEquivalent(with: event)
    }
    switch event.charactersIgnoringModifiers?.lowercased() {
    case "z": flags.contains(.shift) ? redo(nil) : undo(nil)
    case "y": redo(nil)
    case "s": saveAs?()
    case "a":
      if captureMode { fullScreen?() } else { return super.performKeyEquivalent(with: event) }
    default: return super.performKeyEquivalent(with: event)
    }
    return true
  }
  func editText(at point: NSPoint, index: Int?) {
    let alert = NSAlert()
    alert.messageText = index == nil ? "添加文字" : "修改文字"
    alert.informativeText = "支持中文多行文字。Enter 换行，Command+Enter 完成。"
    alert.addButton(withTitle: "完成")
    alert.addButton(withTitle: "取消")
    alert.buttons[0].keyEquivalent = ""
    let scroll = NSScrollView(frame: NSRect(x: 0, y: 0, width: 360, height: 135))
    scroll.hasVerticalScroller = true
    scroll.borderType = .bezelBorder
    let input = MultiLineAnnotationText(frame: scroll.bounds)
    input.isRichText = false
    input.font = .systemFont(ofSize: 18)
    input.string = index.map { marks[$0].text } ?? ""
    scroll.documentView = input
    input.commit = { [weak alert] in alert?.buttons.first?.performClick(nil) }
    alert.accessoryView = scroll
    alert.window.initialFirstResponder = input
    alert.window.level = NSWindow.Level(rawValue: (window?.level.rawValue ?? 0) + 2)
    if alert.runModal() == .alertFirstButtonReturn && !input.string.isEmpty {
      checkpoint()
      if let index {
        marks[index].text = input.string
      } else {
        marks.append(
          Mark(
            tool: 4, points: [point], color: color, width: lineWidth, text: input.string,
            fontSize: fontSize))
      }
      needsDisplay = true
      changed?()
    }
    window?.makeFirstResponder(self)
  }
  func normalizedBounds(_ mark: Mark) -> NSRect {
    guard let first = mark.points.first else { return .zero }
    if mark.tool == 4 {
      let size = (mark.text as NSString).size(withAttributes: [
        .font: NSFont.systemFont(ofSize: mark.fontSize)
      ])
      return NSRect(
        x: first.x, y: first.y - size.height / image.pixels.height,
        width: size.width / image.pixels.width, height: size.height / image.pixels.height)
    }
    let minX = mark.points.map(\.x).min() ?? first.x
    let maxX = mark.points.map(\.x).max() ?? first.x
    let minY = mark.points.map(\.y).min() ?? first.y
    let maxY = mark.points.map(\.y).max() ?? first.y
    return NSRect(x: minX, y: minY, width: maxX - minX, height: maxY - minY)
  }
  func markBounds(_ mark: Mark, in rect: NSRect) -> NSRect {
    let box = normalizedBounds(mark)
    return NSRect(
      x: rect.minX + box.minX * rect.width, y: rect.minY + box.minY * rect.height,
      width: box.width * rect.width, height: box.height * rect.height)
  }
  func paint(_ mark: Mark, in rect: NSRect) {
    let points = mark.points.map {
      NSPoint(x: rect.minX + $0.x * rect.width, y: rect.minY + $0.y * rect.height)
    }
    guard let start = points.first, let end = points.last else { return }
    let scale = rect.width / max(image.pixels.width, 1)
    let width = max(0.5, mark.width * scale)
    mark.color.setStroke()
    mark.color.setFill()
    let box = NSRect(
      x: min(start.x, end.x), y: min(start.y, end.y), width: abs(end.x - start.x),
      height: abs(end.y - start.y))
    let path = NSBezierPath()
    path.lineWidth = width
    path.lineCapStyle = .round
    path.lineJoinStyle = .round
    switch mark.tool {
    case 0:
      path.appendRect(box)
      path.stroke()
    case 1:
      path.appendOval(in: box)
      path.stroke()
    case 2:
      path.move(to: start)
      path.line(to: end)
      path.stroke()
      let angle = atan2(end.y - start.y, end.x - start.x)
      let length = max(14, mark.width * 4) * scale
      let head = NSBezierPath()
      head.move(to: end)
      head.line(
        to: NSPoint(x: end.x - length * cos(angle - 0.42), y: end.y - length * sin(angle - 0.42)))
      head.line(
        to: NSPoint(x: end.x - length * cos(angle + 0.42), y: end.y - length * sin(angle + 0.42)))
      head.close()
      head.fill()
    case 3:
      if points.count <= 2 && start == end {
        NSBezierPath(
          ovalIn: NSRect(
            x: start.x - width / 2, y: start.y - width / 2, width: width, height: width)
        ).fill()
      } else {
        path.move(to: start)
        for point in points.dropFirst() { path.line(to: point) }
        path.stroke()
      }
    case 4:
      let attributes: [NSAttributedString.Key: Any] = [
        .font: NSFont.systemFont(ofSize: mark.fontSize * scale), .foregroundColor: mark.color,
      ]
      let text = mark.text as NSString
      let size = text.size(withAttributes: attributes)
      text.draw(at: NSPoint(x: start.x, y: start.y - size.height), withAttributes: attributes)
    case 5:
      let size = image.pixels
      let sourceRect = NSRect(
        x: min(mark.points.first!.x, mark.points.last!.x) * size.width,
        y: min(mark.points.first!.y, mark.points.last!.y) * size.height,
        width: abs(mark.points.last!.x - mark.points.first!.x) * size.width,
        height: abs(mark.points.last!.y - mark.points.first!.y) * size.height
      ).integral.intersection(NSRect(origin: .zero, size: size))
      guard sourceRect.width > 0, sourceRect.height > 0 else { return }
      let key = "\(sourceRect.minX),\(sourceRect.minY),\(sourceRect.width),\(sourceRect.height)"
      if mosaics[key] == nil, let source = try? image.cropped(to: sourceRect) {
        mosaics[key] = pixelatedImage(source)
      }
      NSGraphicsContext.saveGraphicsState()
      NSBezierPath(rect: box).addClip()
      NSGraphicsContext.current?.imageInterpolation = .none
      let destination = NSRect(
        x: rect.minX + sourceRect.minX * scale, y: rect.minY + sourceRect.minY * scale,
        width: sourceRect.width * scale, height: sourceRect.height * scale)
      mosaics[key]?.draw(in: destination)
      NSGraphicsContext.restoreGraphicsState()
    default: break
    }
  }
  func pixelatedImage(_ image: NSImage) -> NSImage? {
    // Downsample then nearest-neighbor upscale. Every block contains averaged source pixels.
    let pixels = image.pixels
    let width = max(1, Int(ceil(pixels.width / 14)))
    let height = max(1, Int(ceil(pixels.height / 14)))
    guard
      let bitmap = NSBitmapImageRep(
        bitmapDataPlanes: nil, pixelsWide: width, pixelsHigh: height, bitsPerSample: 8,
        samplesPerPixel: 4, hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB,
        bytesPerRow: 0, bitsPerPixel: 0), let context = NSGraphicsContext(bitmapImageRep: bitmap)
    else { return nil }
    NSGraphicsContext.saveGraphicsState()
    NSGraphicsContext.current = context
    context.imageInterpolation = .high
    image.draw(in: NSRect(x: 0, y: 0, width: width, height: height))
    NSGraphicsContext.restoreGraphicsState()
    let result = NSImage(size: pixels)
    result.addRepresentation(bitmap)
    return result
  }
  func result() -> EditResult? {
    let pixels = image.pixels
    let pixelRect = NSRect(
      x: (selection.minX * pixels.width).rounded(), y: (selection.minY * pixels.height).rounded(),
      width: (selection.width * pixels.width).rounded(),
      height: (selection.height * pixels.height).rounded()
    ).intersection(NSRect(origin: .zero, size: pixels))
    guard pixelRect.width >= 1, pixelRect.height >= 1,
      pixelRect.width * pixelRect.height <= 50_000_000
    else { return nil }
    func render(includeMarks: Bool) -> NSImage? {
      guard
        let bitmap = NSBitmapImageRep(
          bitmapDataPlanes: nil, pixelsWide: Int(pixelRect.width),
          pixelsHigh: Int(pixelRect.height), bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true,
          isPlanar: false, colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0),
        let context = NSGraphicsContext(bitmapImageRep: bitmap)
      else { return nil }
      NSGraphicsContext.saveGraphicsState()
      NSGraphicsContext.current = context
      let rect = NSRect(
        x: -pixelRect.minX, y: -pixelRect.minY, width: pixels.width, height: pixels.height)
      image.draw(in: rect)
      if includeMarks { for mark in marks { paint(mark, in: rect) } }
      NSGraphicsContext.restoreGraphicsState()
      let output = NSImage(size: pixelRect.size)
      output.addRepresentation(bitmap)
      return output
    }
    guard let flattened = render(includeMarks: true), let original = render(includeMarks: false)
    else { return nil }
    let shifted = marks.map { mark -> Mark in
      var mark = mark
      mark.points = mark.points.map {
        NSPoint(
          x: ($0.x * pixels.width - pixelRect.minX) / pixelRect.width,
          y: ($0.y * pixels.height - pixelRect.minY) / pixelRect.height)
      }
      return mark
    }
    return EditResult(
      image: flattened, original: original, document: AnnotationDocument(marks: shifted))
  }
  func rendered() -> NSImage? { result()?.image }
}
