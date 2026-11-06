import AppKit

final class PinController: NSWindowController, NSWindowDelegate {
  let shot: Shot
  unowned let app: AppDelegate
  let image: NSImage
  var rememberTimer: Timer?
  var returning = false
  var label: NSTextField!
  init(shot: Shot, image: NSImage, app: AppDelegate) {
    self.shot = shot
    self.app = app
    self.image = image
    let panel = FloatingPanel(
      contentRect: NSRect(x: 0, y: 0, width: 460, height: 360),
      styleMask: [.borderless, .resizable, .nonactivatingPanel], backing: .buffered, defer: false)
    super.init(window: panel)
    panel.title = "随手挂 · 桌面贴图"
    panel.isReleasedWhenClosed = false
    panel.level = .floating
    panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
    panel.minSize = NSSize(width: 270, height: 140)
    panel.backgroundColor = .white
    panel.delegate = self
    let root = PinRoot()
    root.controller = self
    root.wantsLayer = true
    root.layer?.borderColor = NSColor(hex: "91AAA3").cgColor
    root.layer?.borderWidth = 1
    panel.contentView = root
    let header = PinHeader()
    header.wantsLayer = true
    header.layer?.backgroundColor = NSColor(hex: "EAF3EF").cgColor
    let stack = NSStackView()
    stack.spacing = 4
    func button(_ text: String, _ selector: Selector) {
      let button = NSButton(title: text, target: self, action: selector)
      button.bezelStyle = .rounded
      button.font = .systemFont(ofSize: 12)
      stack.addArrangedSubview(button)
    }
    button("复制", #selector(copyImage))
    button("编辑", #selector(edit))
    button("识字", #selector(ocr))
    button("挂回", #selector(returnToRail))
    label = NSTextField(labelWithString: "置顶 · 100%")
    label.font = .systemFont(ofSize: 11)
    label.textColor = NSColor(hex: "53686B")
    stack.addArrangedSubview(label)
    let imageView = PinImageView()
    imageView.controller = self
    imageView.image = image
    imageView.imageScaling = .scaleProportionallyUpOrDown
    // Large screenshots must shrink inside the floating window instead of
    // forcing Auto Layout to expand it to the image's intrinsic pixel size.
    imageView.setContentCompressionResistancePriority(.defaultLow, for: .horizontal)
    imageView.setContentCompressionResistancePriority(.defaultLow, for: .vertical)
    imageView.toolTip = "滚轮缩放；Control+滚轮调整透明度；拖动上沿移动"
    header.translatesAutoresizingMaskIntoConstraints = false
    stack.translatesAutoresizingMaskIntoConstraints = false
    imageView.translatesAutoresizingMaskIntoConstraints = false
    root.addSubview(header)
    header.addSubview(stack)
    root.addSubview(imageView)
    NSLayoutConstraint.activate([
      header.topAnchor.constraint(equalTo: root.topAnchor, constant: 1),
      header.leadingAnchor.constraint(equalTo: root.leadingAnchor, constant: 1),
      header.trailingAnchor.constraint(equalTo: root.trailingAnchor, constant: -1),
      header.heightAnchor.constraint(equalToConstant: 36),
      stack.leadingAnchor.constraint(equalTo: header.leadingAnchor, constant: 4),
      stack.centerYAnchor.constraint(equalTo: header.centerYAnchor),
      imageView.topAnchor.constraint(equalTo: header.bottomAnchor, constant: 3),
      imageView.bottomAnchor.constraint(equalTo: root.bottomAnchor, constant: -3),
      imageView.leadingAnchor.constraint(equalTo: root.leadingAnchor, constant: 3),
      imageView.trailingAnchor.constraint(equalTo: root.trailingAnchor, constant: -3),
    ])
    if let placement = shot.placement {
      place(placement.rect)
      panel.alphaValue = placement.opacity
    } else {
      resetSize()
    }
    refreshLabel()
  }
  required init?(coder: NSCoder) { fatalError("init(coder:) has not been implemented") }
  @objc func copyImage() { app.library.copy(shot) }
  @objc func edit() { app.edit(shot) }
  @objc func ocr() { app.ocr(shot) }
  @objc func returnToRail() {
    persist()
    returning = true
    window?.close()
    app.library.update(shot) {
      $0.isPinned = false
      $0.onRail = true
    }
    app.pins.removeValue(forKey: shot.id)
    app.showRail()
  }
  func closeWithoutReturning() {
    rememberTimer?.invalidate()
    returning = true
    window?.delegate = nil
    window?.close()
  }
  func windowWillClose(_ notification: Notification) { if !returning { returnToRail() } }
  func windowDidMove(_ notification: Notification) { schedule() }
  func windowDidResize(_ notification: Notification) { schedule() }
  func schedule() {
    rememberTimer?.invalidate()
    rememberTimer = Timer.scheduledTimer(withTimeInterval: 0.35, repeats: false) { [weak self] _ in
      self?.persist()
    }
  }
  func persist() {
    guard let window, !returning else { return }
    let rect = window.frame
    app.library.update(shot) {
      $0.placement = PinPlacement(
        x: rect.minX, y: rect.minY, width: rect.width, height: rect.height,
        opacity: window.alphaValue)
    }
    refreshLabel()
  }
  func refreshLabel() {
    guard let window else { return }
    label?.stringValue = "置顶 · \(Int(window.frame.width / max(1, image.pixels.width) * 100))%"
  }
  func resetSize() {
    let work = NSScreen.main!.visibleFrame
    let pixels = image.pixels
    let scale = min(1, min(work.width * 0.65 / pixels.width, work.height * 0.65 / pixels.height))
    let size = NSSize(
      width: max(300, pixels.width * scale), height: max(180, pixels.height * scale + 40))
    place(
      NSRect(
        x: work.midX - size.width / 2, y: work.midY - size.height / 2, width: size.width,
        height: size.height))
  }
  func place(_ requested: NSRect) {
    let work =
      (NSScreen.screens.max {
        $0.visibleFrame.intersection(requested).area < $1.visibleFrame.intersection(requested).area
      } ?? NSScreen.main!).visibleFrame
    let width = min(work.width, max(270, requested.width))
    let height = min(work.height, max(140, requested.height))
    window?.setFrame(
      NSRect(
        x: min(work.maxX - width, max(work.minX, requested.minX)),
        y: min(work.maxY - height, max(work.minY, requested.minY)), width: width, height: height),
      display: true)
  }
  func scroll(_ event: NSEvent) {
    guard let window else { return }
    if event.modifierFlags.contains(.control) {
      window.alphaValue = min(
        1, max(0.2, window.alphaValue + (event.scrollingDeltaY > 0 ? 0.1 : -0.1)))
      persist()
    } else {
      let factor = event.scrollingDeltaY > 0 ? 1.12 : 1 / 1.12
      let rect = window.frame
      let w = rect.width * factor
      let h = rect.height * factor
      place(NSRect(x: rect.midX - w / 2, y: rect.midY - h / 2, width: w, height: h))
      schedule()
    }
  }
}
final class PinRoot: NSView {
  weak var controller: PinController?
  override func scrollWheel(with event: NSEvent) { controller?.scroll(event) }
  override func keyDown(with event: NSEvent) {
    if event.keyCode == 53 { controller?.returnToRail() } else { super.keyDown(with: event) }
  }
}
final class PinHeader: NSView {
  override var mouseDownCanMoveWindow: Bool { true }
  override func mouseDown(with event: NSEvent) { window?.performDrag(with: event) }
}
extension NSRect { var area: CGFloat { isNull ? 0 : max(0, width) * max(0, height) } }

final class PinImageView: NSImageView {
  weak var controller: PinController?
  override func scrollWheel(with event: NSEvent) { controller?.scroll(event) }
}
