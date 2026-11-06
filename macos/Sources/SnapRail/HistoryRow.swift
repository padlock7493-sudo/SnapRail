import AppKit
import SwiftUI

struct RowInteraction: NSViewRepresentable {
  let select: (NSEvent.ModifierFlags) -> Void
  let preview: () -> Void
  let menu: () -> NSMenu
  func makeNSView(context: Context) -> RowHitView { RowHitView() }
  func updateNSView(_ view: RowHitView, context: Context) {
    view.select = select
    view.preview = preview
    view.buildMenu = menu
    view.setAccessibilityElement(false)
  }
}
final class RowHitView: NSView {
  var select: ((NSEvent.ModifierFlags) -> Void)?
  var preview: (() -> Void)?
  var buildMenu: (() -> NSMenu)?
  override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }
  override func mouseDown(with event: NSEvent) {
    if event.clickCount == 2 { preview?() } else { select?(event.modifierFlags) }
  }
  override func rightMouseDown(with event: NSEvent) {
    if event.modifierFlags.contains(.control) {
      select?(event.modifierFlags)
    } else {
      super.rightMouseDown(with: event)
    }
  }
  override func menu(for event: NSEvent) -> NSMenu? { buildMenu?() }
}
