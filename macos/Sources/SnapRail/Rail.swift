import AppKit
import Combine
import SwiftUI

struct RailView: View {
  @ObservedObject var library: Library
  let app: AppDelegate
  var body: some View {
    VStack(spacing: 0) {
      HStack(spacing: 8) {
        Text("随手挂").font(.system(size: 16, weight: .semibold))
        Text(
          library.welcomeOnly
            ? "截一张，随手挂一下！"
            : "挂图 \(library.railShots.count) · 贴图 \(library.shots.filter(\.isPinned).count)"
        ).font(.system(size: 11)).foregroundStyle(Color(hex: "53686B"))
        Button("截图  \(app.configuration.capture.description)", action: app.capture).buttonStyle(
          RailButtonStyle(active: true)
        ).disabled(library.capturing)
        Button("历史", action: app.showHistory)
        Button("设置", action: app.showPreferences)
        Button("收起", action: app.hideRail)
      }.padding(.horizontal, 12).padding(.vertical, 8).background(
        Color(hex: "FAFDFC"), in: RoundedRectangle(cornerRadius: 12)
      ).overlay(RoundedRectangle(cornerRadius: 12).stroke(Color(hex: "CDDDD7")))
      RailRepresentable(library: library, app: app).frame(maxWidth: .infinity, maxHeight: .infinity)
    }.padding(.top, 2).buttonStyle(RailButtonStyle()).foregroundStyle(Color(hex: "253A40"))
      .environment(\.colorScheme, .light)
  }
}
struct RailRepresentable: NSViewRepresentable {
  let library: Library
  let app: AppDelegate
  func makeNSView(context: Context) -> RailCanvas { RailCanvas(library: library, app: app) }
  func updateNSView(_ view: RailCanvas, context: Context) { view.needsDisplay = true }
}
final class RailCanvas: NSView, NSDraggingSource {
  let library: Library
  unowned let app: AppDelegate
  var subscription: AnyCancellable?
  var swingObserver: NSObjectProtocol?
  var animationTimer: Timer?
  var animationStart = Date.distantPast
  var cards: [(Shot, NSRect, Int)] = []
  var cache: [UUID: NSImage] = [:]
  var accessibleButtons: [String: AccessibleButton] = [:]
  var moreRect = NSRect.zero
  var pressed: Shot?, pressPoint = NSPoint.zero, dragPoint = NSPoint.zero
  var dragging = false, externalDrag = false
  var workingOrder: [UUID]?
  override var isFlipped: Bool { true }
  override var acceptsFirstResponder: Bool { true }
  init(library: Library, app: AppDelegate) {
    self.library = library
    self.app = app
    super.init(frame: .zero)
    subscription = library.objectWillChange.sink { [weak self] _ in
      DispatchQueue.main.async { self?.needsDisplay = true }
    }
    swingObserver = NotificationCenter.default.addObserver(
      forName: Notification.Name("SnapRailSwing"), object: nil, queue: .main
    ) { [weak self] _ in self?.swing() }
  }
  required init?(coder: NSCoder) { fatalError("init(coder:) has not been implemented") }
  deinit {
    animationTimer?.invalidate()
    if let swingObserver { NotificationCenter.default.removeObserver(swingObserver) }
  }
  func swing() {
    guard app.configuration.animationEnabled,
      !NSWorkspace.shared.accessibilityDisplayShouldReduceMotion
    else { return }
    animationStart = Date()
    animationTimer?.invalidate()
    animationTimer = Timer.scheduledTimer(withTimeInterval: 1 / 45, repeats: true) {
      [weak self] timer in
      guard let self else {
        timer.invalidate()
        return
      }
      self.needsDisplay = true
      if Date().timeIntervalSince(self.animationStart) > 1.6 { timer.invalidate() }
    }
  }
  func point(_ x: CGFloat, _ y: CGFloat) -> NSPoint { NSPoint(x: x, y: y) }
  override func draw(_ dirtyRect: NSRect) {
    let cord = NSBezierPath()
    cord.move(to: point(12, 19))
    cord.curve(
      to: point(bounds.width - 12, 19), controlPoint1: point(bounds.width / 3, 107),
      controlPoint2: point(bounds.width * 2 / 3, 107))
    NSColor(hex: "617D78").setStroke()
    cord.lineWidth = 5
    cord.stroke()
    NSColor(hex: "E9D4A9").setStroke()
    cord.lineWidth = 2.5
    cord.stroke()
    for x in [CGFloat(12), bounds.width - 12] {
      NSColor(hex: "E9D4A9").setFill()
      NSColor(hex: "617D78").setStroke()
      let end = NSBezierPath(ovalIn: NSRect(x: x - 5, y: 14, width: 10, height: 10))
      end.lineWidth = 1.5
      end.fill()
      end.stroke()
    }
    var shots = library.railShots
    if let order = workingOrder {
      shots.sort { (order.firstIndex(of: $0.id) ?? 0) < (order.firstIndex(of: $1.id) ?? 0) }
    }
    cache = cache.filter { id, _ in library.shots.contains { $0.id == id } }
    cards = []
    moreRect = .zero
    defer { updateAccessibility() }
    let welcome = library.welcomeOnly
    let available = max(1, bounds.width - 48)
    let width =
      welcome
      ? max(20, min(app.configuration.photoWidth, available / 10 - 10))
      : app.configuration.photoWidth
    let capacity = max(1, Int(available / (width + 24)))
    let count =
      welcome ? min(shots.count, 10) : (shots.count > capacity ? max(0, capacity - 1) : shots.count)
    let slots = count + (shots.count > count ? 1 : 0)
    if shots.isEmpty {
      let rect = NSRect(x: 24, y: 40, width: 270, height: 142)
      drawCardBackground(rect, fill: "F5FAF8")
      let text =
        library.shots.contains(where: \.isPinned)
        ? "图片正在桌面上\n\n点击挂回所有贴图" : "第一张截图，从这里开始\n\n\(app.configuration.capture.description)"
      drawText(
        text, in: rect.insetBy(dx: 20, dy: 25), size: 15, color: "253A40", alignment: .center)
      moreRect = rect
      return
    }
    for (index, shot) in shots.prefix(count).enumerated() {
      let center = 24 + available * (CGFloat(index) + 0.5) / CGFloat(max(1, slots))
      let t = center / bounds.width
      let scale = width / 196
      let y = 19 + 264 * t * (1 - t) - 20 * scale
      let rect = NSRect(
        x: center - (width + (welcome ? 0 : 24)) / 2, y: y, width: width + (welcome ? 0 : 24),
        height: 206 * scale + 15)
      cards.append((shot, rect, index))
      if shot.id == pressed?.id && dragging { continue }
      drawShot(shot, rect: rect, index: index, welcome: welcome, lift: false)
    }
    if let pressed, dragging, let card = cards.first(where: { $0.0.id == pressed.id }) {
      var rect = card.1
      rect.origin.x = dragPoint.x - rect.width / 2
      rect.origin.y -= 16
      drawShot(pressed, rect: rect, index: card.2, welcome: welcome, lift: true)
    }
    if shots.count > count {
      let center = 24 + available * (CGFloat(count) + 0.5) / CGFloat(max(1, slots))
      let t = center / bounds.width
      moreRect = NSRect(x: center - 28, y: 19 + 264 * t * (1 - t) - 10, width: 56, height: 56)
      NSColor(hex: "E4F1E9").setFill()
      NSColor(hex: "77958B").setStroke()
      let circle = NSBezierPath(ovalIn: moreRect)
      circle.lineWidth = 2
      circle.fill()
      circle.stroke()
      drawText(
        "＋", in: moreRect.offsetBy(dx: 0, dy: 7), size: 30, color: "253A40", alignment: .center)
    }
  }
  final class AccessibleButton: NSAccessibilityElement {
    var press: (() -> Void)?
    override func accessibilityPerformPress() -> Bool {
      press?()
      return true
    }
  }
  func updateAccessibility() {
    var children: [NSAccessibilityElement] = []
    for (shot, rect, _) in cards {
      let cardKey = "rail-" + shot.id.uuidString
      let element = accessibleButtons[cardKey] ?? AccessibleButton()
      accessibleButtons[cardKey] = element
      element.setAccessibilityIdentifier(cardKey)
      element.setAccessibilityEnabled(true)
      element.setAccessibilityRole(.button)
      element.setAccessibilityLabel("挂图 " + shot.filename)
      element.setAccessibilityParent(self)
      element.setAccessibilityFrame(window?.convertToScreen(convert(rect, to: nil)) ?? .zero)
      element.press = { [weak self] in self?.library.copy(shot) }
      children.append(element)
      let pegKey = "pin-" + shot.id.uuidString
      let peg = accessibleButtons[pegKey] ?? AccessibleButton()
      accessibleButtons[pegKey] = peg
      peg.setAccessibilityIdentifier(pegKey)
      peg.setAccessibilityEnabled(true)
      peg.setAccessibilityRole(.button)
      peg.setAccessibilityLabel(shot.favorite ? "已固定收藏，取下为桌面贴图" : "取下为桌面贴图")
      peg.setAccessibilityParent(self)
      let pegRect = NSRect(x: rect.midX - 12, y: rect.minY, width: 24, height: 30)
      peg.setAccessibilityFrame(window?.convertToScreen(convert(pegRect, to: nil)) ?? .zero)
      peg.press = { [weak self] in self?.app.pin(shot) }
      children.append(peg)
    }
    if !moreRect.isEmpty {
      let more = accessibleButtons["rail-more"] ?? AccessibleButton()
      accessibleButtons["rail-more"] = more
      more.setAccessibilityIdentifier("rail-more")
      more.setAccessibilityEnabled(true)
      more.setAccessibilityRole(.button)
      more.setAccessibilityLabel("更多挂图")
      more.setAccessibilityParent(self)
      more.setAccessibilityFrame(window?.convertToScreen(convert(moreRect, to: nil)) ?? .zero)
      more.press = { [weak self] in self?.app.showMore() }
      children.append(more)
    }
    let live = Set(
      ["rail-more"] + cards.flatMap { ["rail-" + $0.0.id.uuidString, "pin-" + $0.0.id.uuidString] })
    accessibleButtons = accessibleButtons.filter { live.contains($0.key) }
    setAccessibilityElement(false)
    setAccessibilityChildren(children)
  }
  func drawShot(_ shot: Shot, rect: NSRect, index: Int, welcome: Bool, lift: Bool) {
    NSGraphicsContext.saveGraphicsState()
    let scale = (rect.width - (welcome ? 0 : 24)) / 196
    let center = NSPoint(x: rect.midX, y: rect.minY + 8)
    let transform = NSAffineTransform()
    transform.translateX(by: center.x, yBy: center.y)
    let elapsed = Date().timeIntervalSince(animationStart) - Double(index) * 0.035
    let swing =
      elapsed > 0 && elapsed < 1.2 && !dragging ? sin(elapsed * 14) * exp(-elapsed * 3) * 10 : 0
    transform.rotate(byDegrees: (index % 2 == 0 ? -2 : 2) + swing)
    let liftScale = lift ? 1.08 : (dragging ? 0.92 : 1)
    transform.scale(by: liftScale)
    transform.translateX(by: -center.x, yBy: -center.y)
    transform.concat()
    let card = NSRect(
      x: rect.minX, y: rect.minY + 27 * scale, width: rect.width,
      height: 158 * scale + (welcome ? 0 : 24))
    if lift {
      let shadow = NSShadow()
      shadow.shadowColor = NSColor.black.withAlphaComponent(0.22)
      shadow.shadowBlurRadius = 10
      shadow.shadowOffset = NSSize(width: 0, height: 4)
      shadow.set()
    }
    drawCardBackground(card, fill: welcome ? "FFF8E7" : "FFFFFF")
    if cache[shot.id] == nil { cache[shot.id] = library.image(shot) }
    if let image = cache[shot.id] {
      let target = card.insetBy(dx: 8, dy: welcome ? 8 : 10)
      let imageBox = NSRect(
        x: target.minX, y: target.minY, width: target.width,
        height: target.height - (welcome ? 0 : 16))
      let ratio = min(imageBox.width / image.size.width, imageBox.height / image.size.height)
      let size = NSSize(width: image.size.width * ratio, height: image.size.height * ratio)
      image.draw(
        in: NSRect(
          x: imageBox.midX - size.width / 2, y: imageBox.midY - size.height / 2, width: size.width,
          height: size.height), from: .zero, operation: .sourceOver,
        fraction: dragging && !lift ? 0.78 : 1, respectFlipped: true, hints: nil)
    }
    if !welcome {
      drawText(
        shot.created.formatted(date: .omitted, time: .standard) + " · 已保存",
        in: NSRect(x: card.minX + 4, y: card.maxY - 17, width: card.width - 8, height: 14), size: 9,
        color: "53686B", alignment: .center)
    }
    drawPeg(
      at: NSPoint(x: rect.midX - 14 * scale, y: rect.minY), scale: scale,
      color: shot.favorite ? "F5C64D" : index % 2 == 0 ? "A9D8C6" : "F2B5A7",
      favorite: shot.favorite)
    if shot.favorite {
      let badge = NSRect(x: rect.midX + 23 * scale, y: rect.minY + 3, width: 43, height: 23)
      drawCardBackground(badge, fill: "F5C64D")
      drawText("收藏", in: badge.insetBy(dx: 3, dy: 4), size: 12, color: "624014", alignment: .center)
    }
    NSGraphicsContext.restoreGraphicsState()
  }
  func drawCardBackground(_ rect: NSRect, fill: String) {
    NSColor(hex: fill).setFill()
    NSColor(hex: "CDDDD7").setStroke()
    let path = NSBezierPath(roundedRect: rect, xRadius: 5, yRadius: 5)
    path.lineWidth = 1
    path.fill()
    path.stroke()
  }
  func drawText(
    _ text: String, in rect: NSRect, size: CGFloat, color: String, alignment: NSTextAlignment
  ) {
    let paragraph = NSMutableParagraphStyle()
    paragraph.alignment = alignment
    paragraph.lineBreakMode = .byTruncatingTail
    (text as NSString).draw(
      in: rect,
      withAttributes: [
        .font: NSFont.systemFont(ofSize: size), .foregroundColor: NSColor(hex: color),
        .paragraphStyle: paragraph,
      ])
  }
  func drawPeg(at origin: NSPoint, scale: CGFloat, color: String, favorite: Bool) {
    NSGraphicsContext.saveGraphicsState()
    let t = NSAffineTransform()
    t.translateX(by: origin.x, yBy: origin.y)
    t.scale(by: scale)
    t.concat()
    NSColor(hex: color).setFill()
    NSColor(hex: "57766C").setStroke()
    let peg = NSBezierPath(
      roundedRect: NSRect(x: 5, y: 1, width: 18, height: 41), xRadius: 8, yRadius: 8)
    peg.lineWidth = 1.7
    peg.fill()
    peg.stroke()
    let line = NSBezierPath()
    line.lineWidth = 1.7
    line.move(to: point(14, 6))
    line.line(to: point(14, 14))
    line.move(to: point(14, 27))
    line.line(to: point(14, 36))
    line.stroke()
    NSColor(hex: "FFF8E7").setFill()
    let ring = NSBezierPath(ovalIn: NSRect(x: 10.5, y: 16.5, width: 7, height: 7))
    ring.fill()
    ring.stroke()
    if favorite {
      NSColor(hex: "FFF5CF").setFill()
      let circle = NSBezierPath(ovalIn: NSRect(x: 3, y: -1, width: 22, height: 22))
      circle.fill()
      NSColor(hex: "9C641B").setStroke()
      circle.stroke()
      let star = NSBezierPath()
      for i in 0..<10 {
        let angle = -Double.pi / 2 + Double(i) * Double.pi / 5
        let r = i % 2 == 0 ? 8.0 : 3.6
        let p = point(14 + cos(angle) * r, 10 + sin(angle) * r)
        if i == 0 { star.move(to: p) } else { star.line(to: p) }
      }
      star.close()
      NSColor(hex: "90631F").setFill()
      star.fill()
    }
    NSGraphicsContext.restoreGraphicsState()
  }
  override func mouseDown(with event: NSEvent) {
    let p = convert(event.locationInWindow, from: nil)
    window?.makeFirstResponder(self)
    if moreRect.contains(p) {
      app.showMore()
      return
    }
    guard let card = cards.last(where: { $0.1.contains(p) }) else { return }
    let scale = (card.1.width - 24) / 196
    if p.y < card.1.minY + 30 * scale && abs(p.x - card.1.midX) < 20 * scale {
      app.pin(card.0)
      return
    }
    if event.clickCount == 2 {
      app.preview(card.0)
      return
    }
    pressed = card.0
    pressPoint = p
    dragPoint = p
  }
  override func mouseDragged(with event: NSEvent) {
    guard let pressed else { return }
    dragPoint = convert(event.locationInWindow, from: nil)
    guard hypot(dragPoint.x - pressPoint.x, dragPoint.y - pressPoint.y) > 5 else { return }
    if !dragging {
      dragging = true
      workingOrder = library.railShots.map(\.id)
    }
    if !bounds.insetBy(dx: -20, dy: -25).contains(dragPoint), !externalDrag {
      externalDrag = true
      dragging = false
      workingOrder = nil
      let item = NSDraggingItem(pasteboardWriter: library.url(pressed) as NSURL)
      item.setDraggingFrame(
        NSRect(x: dragPoint.x - 60, y: dragPoint.y - 40, width: 120, height: 80),
        contents: library.image(pressed))
      beginDraggingSession(with: [item], event: event, source: self)
      return
    }
    if let target = cards.min(by: { abs($0.1.midX - dragPoint.x) < abs($1.1.midX - dragPoint.x) }),
      target.0.id != pressed.id, target.0.favorite == pressed.favorite, var order = workingOrder,
      let from = order.firstIndex(of: pressed.id), let to = order.firstIndex(of: target.0.id)
    {
      order.remove(at: from)
      order.insert(pressed.id, at: to)
      workingOrder = order
    }
    needsDisplay = true
  }
  override func mouseUp(with event: NSEvent) {
    if dragging, let workingOrder {
      library.applyOrder(workingOrder)
    } else if let pressed, !externalDrag {
      library.copy(pressed)
    }
    resetDrag()
  }
  func resetDrag() {
    dragging = false
    externalDrag = false
    pressed = nil
    workingOrder = nil
    needsDisplay = true
  }
  override func keyDown(with event: NSEvent) {
    if event.keyCode == 53 { resetDrag() } else { super.keyDown(with: event) }
  }
  func draggingSession(
    _ session: NSDraggingSession, sourceOperationMaskFor context: NSDraggingContext
  ) -> NSDragOperation { .copy }
  func draggingSession(
    _ session: NSDraggingSession, endedAt screenPoint: NSPoint, operation: NSDragOperation
  ) { resetDrag() }
  override func menu(for event: NSEvent) -> NSMenu? {
    let point = convert(event.locationInWindow, from: nil)
    guard let shot = cards.last(where: { $0.1.contains(point) })?.0 else { return nil }
    return app.imageMenu(shot)
  }
}
