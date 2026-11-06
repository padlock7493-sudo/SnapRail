import AppKit

final class EditorController: NSWindowController, NSWindowDelegate {
  let id = UUID()
  var didClose: ((UUID) -> Void)?
  let canvas: AnnotationView
  let completion: (EditResult) -> Bool
  var showOCR: ((NSImage) -> Void)?
  init(
    image: NSImage, document: AnnotationDocument = AnnotationDocument(),
    completion: @escaping (EditResult) -> Bool
  ) {
    canvas = AnnotationView(image: image)
    canvas.marks = document.marks
    canvas.initialMarks = document.marks
    canvas.tool = 7
    self.completion = completion
    let window = NSWindow(
      contentRect: NSRect(x: 0, y: 0, width: 1000, height: 740),
      styleMask: [.titled, .closable, .resizable], backing: .buffered, defer: false)
    super.init(window: window)
    window.title = "随手挂 · 编辑副本"
    window.isReleasedWhenClosed = false
    window.minSize = NSSize(width: 830, height: 450)
    window.delegate = self
    let root = NSView()
    window.contentView = root
    let toolbar = AnnotationToolbar(canvas: canvas, capture: false)
    let hint = NSTextField(
      labelWithString: "对象：选择并拖动标注 · 双击文字修改 · Delete 删除 · 方向键微调 · Command+Z 撤销")
    hint.font = .systemFont(ofSize: 11)
    hint.textColor = .secondaryLabelColor
    for view in [canvas, toolbar, hint] {
      view.translatesAutoresizingMaskIntoConstraints = false
      root.addSubview(view)
    }
    NSLayoutConstraint.activate([
      canvas.leadingAnchor.constraint(equalTo: root.leadingAnchor),
      canvas.trailingAnchor.constraint(equalTo: root.trailingAnchor),
      canvas.topAnchor.constraint(equalTo: root.topAnchor),
      canvas.bottomAnchor.constraint(equalTo: toolbar.topAnchor, constant: -12),
      toolbar.centerXAnchor.constraint(equalTo: root.centerXAnchor),
      toolbar.widthAnchor.constraint(equalToConstant: 790),
      toolbar.heightAnchor.constraint(equalToConstant: 94),
      toolbar.bottomAnchor.constraint(equalTo: hint.topAnchor, constant: -10),
      hint.leadingAnchor.constraint(equalTo: root.leadingAnchor, constant: 14),
      hint.bottomAnchor.constraint(equalTo: root.bottomAnchor, constant: -10),
    ])
    toolbar.confirm = { [weak self] in self?.saveImage() }
    toolbar.cancel = { [weak self] in self?.requestCancel() }
    toolbar.ocr = { [weak self] in
      guard let self, let image = self.canvas.rendered() else { return }
      self.showOCR?(image)
    }
    toolbar.saveAs = { [weak self] in self?.export() }
    canvas.finish = toolbar.confirm
    canvas.cancel = toolbar.cancel
    canvas.saveAs = toolbar.saveAs
    window.center()
    window.makeFirstResponder(canvas)
  }
  required init?(coder: NSCoder) { fatalError("init(coder:) has not been implemented") }
  func export() {
    guard let image = canvas.rendered() else { return }
    exportImage(image)
  }
  func saveImage() {
    guard let result = canvas.result() else { return }
    if completion(result) {
      window?.delegate = nil
      window?.close()
      didClose?(id)
    }
  }
  func requestCancel() { window?.performClose(nil) }
  func windowShouldClose(_ sender: NSWindow) -> Bool {
    guard canvas.isDirty else { return true }
    let alert = NSAlert()
    alert.messageText = "放弃未保存的标注？"
    alert.informativeText = "原图片会保留。"
    alert.addButton(withTitle: "继续编辑")
    alert.addButton(withTitle: "放弃更改")
    return alert.runModal() == .alertSecondButtonReturn
  }
  func windowWillClose(_ notification: Notification) { didClose?(id) }
}
func exportImage(_ image: NSImage) {
  let panel = NSSavePanel()
  panel.allowedContentTypes = [.png]
  panel.nameFieldStringValue = "SnapRail.png"
  if panel.runModal() == .OK, let url = panel.url {
    do { try image.pngData().write(to: url, options: .atomic) } catch {
      let alert = NSAlert(error: error)
      alert.runModal()
    }
  }
}

final class MultiLineAnnotationText: NSTextView {
  var commit: (() -> Void)?
  override func keyDown(with event: NSEvent) {
    if event.keyCode == 36 || event.keyCode == 76 {
      if event.modifierFlags.contains(.command) || event.modifierFlags.contains(.control) {
        commit?()
      } else {
        insertText("\n", replacementRange: selectedRange())
      }
    } else {
      super.keyDown(with: event)
    }
  }
  override func performKeyEquivalent(with event: NSEvent) -> Bool {
    if event.keyCode == 36 || event.keyCode == 76 {
      keyDown(with: event)
      return true
    }
    return super.performKeyEquivalent(with: event)
  }
}
