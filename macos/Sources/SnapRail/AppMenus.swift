import AppKit

final class ClosureMenuItem: NSMenuItem {
  var invokeAction: (() -> Void)?
  convenience init(_ title: String, action: @escaping () -> Void) {
    self.init(title: title, action: nil, keyEquivalent: "")
    invokeAction = action
    target = self
    self.action = #selector(invoke)
  }
  @objc func invoke() { invokeAction?() }
}
extension AppDelegate {
  func imageMenu(_ shot: Shot) -> NSMenu {
    let menu = NSMenu()
    let actions: [(String, () -> Void)] = [
      ("复制图片", { self.library.copy(shot) }), ("查看大图…", { self.preview(shot) }),
      ("编辑副本…", { self.edit(shot) }),
      ("识别文字…", { self.ocr(shot) }), ("贴到桌面", { self.pin(shot) }),
      (shot.favorite ? "取消固定收藏" : "固定收藏", { self.library.toggleFavorite(shot) }),
      ("另存为…", { self.library.export(shot) }),
      (shot.onRail ? "从绳子取下" : "重新挂图", { self.library.update(shot) { $0.onRail.toggle() } }),
      ("打开截图文件夹", { self.openFolder() }),
    ]
    for (title, action) in actions { menu.addItem(ClosureMenuItem(title, action: action)) }
    return menu
  }
}
