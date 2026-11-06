import AppKit
import Carbon
import SwiftUI

final class ShortcutField: NSTextField {
  var change: ((Shortcut) -> Void)?
  var start: (() -> Void)?
  var stop: (() -> Void)?
  var recording = false
  var keyObservers: [NSObjectProtocol] = []
  func beginRecording() {
    if !recording {
      recording = true
      start?()
    }
  }
  func endRecording() {
    if recording {
      recording = false
      stop?()
    }
  }
  override func becomeFirstResponder() -> Bool {
    let accepted = super.becomeFirstResponder()
    if accepted { beginRecording() }
    return accepted
  }
  override func resignFirstResponder() -> Bool {
    endRecording()
    return super.resignFirstResponder()
  }
  override func viewDidMoveToWindow() {
    super.viewDidMoveToWindow()
    for observer in keyObservers { NotificationCenter.default.removeObserver(observer) }
    keyObservers = []
    guard let window else {
      endRecording()
      return
    }
    keyObservers.append(
      NotificationCenter.default.addObserver(
        forName: NSWindow.didResignKeyNotification, object: window, queue: .main
      ) { [weak self] _ in self?.endRecording() })
    keyObservers.append(
      NotificationCenter.default.addObserver(
        forName: NSWindow.didBecomeKeyNotification, object: window, queue: .main
      ) { [weak self] _ in if self?.window?.firstResponder === self { self?.beginRecording() } })
  }
  deinit { for observer in keyObservers { NotificationCenter.default.removeObserver(observer) } }
  override var acceptsFirstResponder: Bool { true }
  override func mouseDown(with event: NSEvent) {
    window?.makeFirstResponder(self)
    beginRecording()
    stringValue = "请按下组合键…"
  }
  override func keyDown(with event: NSEvent) { record(event) }
  override func performKeyEquivalent(with event: NSEvent) -> Bool {
    guard window?.firstResponder === self else { return false }
    record(event)
    return true
  }
  func record(_ event: NSEvent) {
    if event.keyCode == 53 {
      window?.makeFirstResponder(nil)
      return
    }
    let flags = event.modifierFlags
    let modifiers =
      (flags.contains(.control) ? UInt32(controlKey) : 0)
      | (flags.contains(.option) ? UInt32(optionKey) : 0)
      | (flags.contains(.shift) ? UInt32(shiftKey) : 0)
      | (flags.contains(.command) ? UInt32(cmdKey) : 0)
    let names: [UInt16: String] = [
      122: "F1", 120: "F2", 99: "F3", 118: "F4", 96: "F5", 97: "F6", 98: "F7", 100: "F8", 101: "F9",
      109: "F10", 103: "F11", 111: "F12", 105: "F13", 107: "F14", 113: "F15", 106: "F16", 64: "F17",
      79: "F18", 80: "F19", 90: "F20", 49: "Space", 36: "Return",
    ]
    let key = Shortcut(
      key: UInt32(event.keyCode), modifiers: modifiers,
      label: names[event.keyCode] ?? event.charactersIgnoringModifiers?.uppercased()
        ?? "\(event.keyCode)")
    do {
      try key.validate()
      stringValue = key.description
      change?(key)
    } catch { stringValue = error.localizedDescription }
  }
}
struct ShortcutRecorder: NSViewRepresentable {
  let app: AppDelegate
  @Binding var shortcut: Shortcut
  func makeNSView(context: Context) -> ShortcutField {
    let field = ShortcutField()
    field.isEditable = false
    field.isSelectable = false
    field.isBezeled = true
    field.bezelStyle = .squareBezel
    field.font = .systemFont(ofSize: 13)
    return field
  }
  func updateNSView(_ field: ShortcutField, context: Context) {
    if field.window?.firstResponder !== field { field.stringValue = shortcut.description }
    field.change = { shortcut = $0 }
    field.start = { app.suspendHotkeys() }
    field.stop = { app.resumeHotkeys() }
  }
}
