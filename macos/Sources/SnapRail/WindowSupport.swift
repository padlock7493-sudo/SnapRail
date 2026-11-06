import AppKit
import Carbon
import SwiftUI

final class EscapeWindow: NSWindow {
  var closesOnEscape = true
  override func keyDown(with event: NSEvent) {
    if event.keyCode == 53 && closesOnEscape { close() } else { super.keyDown(with: event) }
  }
  override func cancelOperation(_ sender: Any?) {
    if closesOnEscape { close() } else { super.cancelOperation(sender) }
  }
  override func performKeyEquivalent(with event: NSEvent) -> Bool {
    if event.keyCode == 53 && closesOnEscape {
      close()
      return true
    }
    return super.performKeyEquivalent(with: event)
  }
}
final class FloatingPanel: NSPanel {
  override var canBecomeKey: Bool { true }
  override var canBecomeMain: Bool { false }
}
