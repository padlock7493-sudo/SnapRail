import AppKit

final class AnnotationToolbar: NSView {
  let canvas: AnnotationView
  var confirm: (() -> Void)?, cancel: (() -> Void)?, ocr: (() -> Void)?, scroll: (() -> Void)?,
    saveAs: (() -> Void)?, fullScreen: (() -> Void)?
  var toolButtons: [Int: NSButton] = [:], colorButtons: [String: NSButton] = [:]
  var undoButton: NSButton!, redoButton: NSButton!, deleteButton: NSButton!, widthButton: NSButton!,
    sizeButton: NSButton!
  init(canvas: AnnotationView, capture: Bool) {
    self.canvas = canvas
    super.init(frame: NSRect(x: 0, y: 0, width: 780, height: 100))
    wantsLayer = true
    layer?.backgroundColor = NSColor.white.cgColor
    layer?.borderColor = NSColor(hex: "C7D9D3").cgColor
    layer?.borderWidth = 1
    layer?.cornerRadius = 12
    let rows = NSStackView()
    rows.orientation = .vertical
    rows.alignment = .leading
    rows.spacing = 7
    let row1 = NSStackView()
    row1.spacing = 4
    for (tool, label) in [
      (6, "选区"), (7, "对象"), (0, "矩形"), (1, "椭圆"), (2, "箭头"), (3, "画笔"), (4, "文字"), (5, "马赛克"),
    ] {
      let button = add(label, to: row1) { [weak canvas] in
        canvas?.tool = tool
        canvas?.window?.makeFirstResponder(canvas)
      }
      toolButtons[tool] = button
    }
    for ink in ["E34B4B", "126B64", "2675D8", "F4BD50", "FFFFFF", "20282F"] {
      let button = add("●", to: row1) { [weak canvas] in canvas?.style(ink: ink) }
      button.contentTintColor = NSColor(hex: ink)
      button.font = .systemFont(ofSize: 20)
      button.widthAnchor.constraint(equalToConstant: 29).isActive = true
      button.toolTip = "标注颜色 #\(ink)"
      colorButtons[ink] = button
    }
    let row2 = NSStackView()
    row2.spacing = 5
    widthButton = add("线宽 4 ⌄", to: row2) { [weak self] in self?.choice(width: true) }
    sizeButton = add("字号 24 ⌄", to: row2) { [weak self] in self?.choice(width: false) }
    undoButton = add("↶", to: row2) { [weak canvas] in canvas?.undo(nil) }
    undoButton.toolTip = "撤销 · Command+Z"
    redoButton = add("↷", to: row2) { [weak canvas] in canvas?.redo(nil) }
    redoButton.toolTip = "重做 · Command+Shift+Z"
    deleteButton = add("删除", to: row2) { [weak canvas] in canvas?.deleteSelection() }
    _ = add("识别文字", to: row2) { [weak self] in self?.ocr?() }
    if capture { _ = add("长截图", to: row2) { [weak self] in self?.scroll?() } }
    _ = add("更多 ···", to: row2) { [weak self] in self?.more(capture: capture) }
    _ = add("取消", to: row2) { [weak self] in self?.cancel?() }
    let finish = add(capture ? "完成并挂上 ↵" : "保存为新挂图", to: row2) { [weak self] in self?.confirm?() }
    finish.font = .systemFont(ofSize: 12, weight: .semibold)
    finish.layer?.backgroundColor = NSColor(hex: "CDE8DE").cgColor
    rows.addArrangedSubview(row1)
    rows.addArrangedSubview(row2)
    rows.translatesAutoresizingMaskIntoConstraints = false
    addSubview(rows)
    NSLayoutConstraint.activate([
      rows.leadingAnchor.constraint(equalTo: leadingAnchor, constant: 10),
      rows.topAnchor.constraint(equalTo: topAnchor, constant: 10),
      rows.trailingAnchor.constraint(lessThanOrEqualTo: trailingAnchor, constant: -10),
    ])
    canvas.changed = { [weak self] in self?.refresh() }
    refresh()
  }
  required init?(coder: NSCoder) { fatalError("init(coder:) has not been implemented") }
  final class ActionButton: NSButton {
    var actionBlock: (() -> Void)?
    @objc func invoke() { actionBlock?() }
  }
  func add(_ title: String, to stack: NSStackView, action: @escaping () -> Void) -> NSButton {
    let button = ActionButton(title: title, target: nil, action: nil)
    button.target = button
    button.action = #selector(ActionButton.invoke)
    button.actionBlock = action
    button.bezelStyle = .regularSquare
    button.isBordered = false
    button.font = .systemFont(ofSize: 12)
    button.wantsLayer = true
    button.layer?.cornerRadius = 7
    button.layer?.borderWidth = 1
    button.layer?.borderColor = NSColor(hex: "D0DEDA").cgColor
    button.layer?.backgroundColor = NSColor(hex: "F3F7F6").cgColor
    button.heightAnchor.constraint(equalToConstant: 32).isActive = true
    button.contentTintColor = NSColor(hex: "253A40")
    button.widthAnchor.constraint(
      greaterThanOrEqualToConstant: title == "●"
        ? 28
        : max(
          32,
          (title as NSString).size(withAttributes: [.font: NSFont.systemFont(ofSize: 12)]).width
            + 16)
    ).isActive = true
    stack.addArrangedSubview(button)
    return button
  }
  func refresh() {
    for (tool, button) in toolButtons {
      button.layer?.backgroundColor =
        NSColor(hex: tool == canvas.tool ? "CDE8DE" : "F3F7F6").cgColor
      button.layer?.borderColor = NSColor(hex: tool == canvas.tool ? "126B64" : "D0DEDA").cgColor
    }
    for (ink, button) in colorButtons {
      button.layer?.borderColor =
        NSColor(hex: ink == canvas.color.hex ? "126B64" : "D0DEDA").cgColor
    }
    undoButton.isEnabled = !canvas.undoStates.isEmpty
    redoButton.isEnabled = !canvas.redoStates.isEmpty
    deleteButton.isEnabled = canvas.selected != nil
    widthButton.title = "线宽 \(Int(canvas.lineWidth)) ⌄"
    sizeButton.title = "字号 \(Int(canvas.fontSize)) ⌄"
  }
  func choice(width: Bool) {
    let menu = NSMenu()
    for value in width ? [2, 4, 6, 10] : [16, 24, 32, 48] {
      let item = NSMenuItem(
        title: "\(value)", action: #selector(selectStyle(_:)), keyEquivalent: "")
      item.target = self
      item.tag = width ? value : value + 100
      menu.addItem(item)
    }
    let button = width ? widthButton! : sizeButton!
    menu.popUp(positioning: nil, at: NSPoint(x: 0, y: button.bounds.minY), in: button)
  }
  @objc func selectStyle(_ item: NSMenuItem) {
    item.tag >= 100
      ? canvas.style(size: CGFloat(item.tag - 100)) : canvas.style(width: CGFloat(item.tag))
  }
  func more(capture: Bool) {
    let menu = NSMenu()
    let save = NSMenuItem(
      title: "另存为  Command+S", action: #selector(exportAction), keyEquivalent: "")
    save.target = self
    menu.addItem(save)
    if capture {
      let full = NSMenuItem(
        title: "选择全屏  Command+A", action: #selector(fullAction), keyEquivalent: "")
      full.target = self
      menu.addItem(full)
    }
    menu.popUp(positioning: nil, at: NSPoint(x: bounds.maxX - 200, y: 0), in: self)
  }
  @objc func exportAction() { saveAs?() }
  @objc func fullAction() { fullScreen?() }
}
