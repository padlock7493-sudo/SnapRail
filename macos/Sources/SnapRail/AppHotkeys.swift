import AppKit
import Carbon

extension AppDelegate {
  func registerHotkeys() {
    var spec = EventTypeSpec(
      eventClass: OSType(kEventClassKeyboard), eventKind: UInt32(kEventHotKeyPressed))
    InstallEventHandler(
      GetApplicationEventTarget(),
      { _, event, data -> OSStatus in
        guard let event, let data else { return OSStatus(eventNotHandledErr) }
        var id = EventHotKeyID()
        let result = GetEventParameter(
          event, EventParamName(kEventParamDirectObject), EventParamType(typeEventHotKeyID), nil,
          MemoryLayout<EventHotKeyID>.size, nil, &id)
        guard result == noErr else { return result }
        let app = Unmanaged<AppDelegate>.fromOpaque(data).takeUnretainedValue()
        if id.id == 1 { app.capture() } else { app.toggleRail() }
        return noErr
      }, 1, &spec, Unmanaged.passUnretained(self).toOpaque(), &hotkeyHandler)
    do { hotkeys = try register(configuration) } catch {
      library.message = error.localizedDescription
    }
  }
  func register(_ configuration: Configuration) throws -> [EventHotKeyRef] {
    var registered: [EventHotKeyRef] = []
    for (key, id) in [(configuration.capture, 1), (configuration.toggle, 2)] {
      var reference: EventHotKeyRef?
      let result = RegisterEventHotKey(
        key.key, key.modifiers, EventHotKeyID(signature: 0x5352_414C, id: UInt32(id)),
        GetApplicationEventTarget(), 0, &reference)
      if result == noErr, let reference {
        registered.append(reference)
      } else {
        for reference in registered { UnregisterEventHotKey(reference) }
        throw appError("快捷键 \(key.description) 被占用（错误 \(result)），原绑定已保留。")
      }
    }
    return registered
  }
  func suspendHotkeys() {
    for reference in hotkeys { UnregisterEventHotKey(reference) }
    hotkeys = []
  }
  func resumeHotkeys() {
    guard hotkeys.isEmpty else { return }
    do { hotkeys = try register(configuration) } catch {
      library.message = error.localizedDescription
    }
  }
  func testShortcuts(_ draft: Configuration) throws {
    for reference in hotkeys { UnregisterEventHotKey(reference) }
    hotkeys = []
    defer {
      do { hotkeys = try register(configuration) } catch {
        library.message = error.localizedDescription
      }
    }
    let tested = try register(draft)
    for reference in tested { UnregisterEventHotKey(reference) }
  }
}
