import AppKit
import Carbon
import SwiftUI

final class AppDelegate: NSObject, NSApplicationDelegate {
  let library: Library
  var configuration: Configuration
  let profile: URL?
  let options: LaunchOptions
  var status: NSStatusItem!
  var rail: NSPanel!
  var history: NSWindow?, preferences: NSWindow?
  var windows: [NSWindow] = []
  var editors: [EditorController] = []
  var pins: [UUID: PinController] = [:]
  var hotkeys: [EventHotKeyRef] = [], hotkeyHandler: EventHandlerRef?
  var hoverTimer: Timer?
  var observers: [NSObjectProtocol] = []
  var launchLock: Int32 = -1
  var captureController: CaptureController?
  var scrollSession: ScrollSession?
  var hoverSince: Date?, outsideSince: Date?, keepUntil = Date.distantPast
  var hoverTriggered = false
  var menuTracking = false
  var wasVisibleBeforeCapture: [NSWindow] = []
  var hiddenForMaintenance: [NSWindow] = []
  init(profile: URL? = nil, options: LaunchOptions = LaunchOptions()) {
    self.profile = profile
    self.options = options
    if let profile {
      var fallback = Configuration()
      fallback.dataRoot = profile.path
      configuration =
        (try? SettingsStore.read(profile.appendingPathComponent("settings.json"))) ?? fallback
    } else {
      configuration = SettingsStore.load()
    }
    library = Library(root: URL(fileURLWithPath: configuration.dataRoot, isDirectory: true))
    super.init()
  }
  func applicationDidFinishLaunching(_ notification: Notification) {
    launchLock = open(
      library.root.appendingPathComponent("app.lock").path, O_CREAT | O_RDWR, S_IRUSR | S_IWUSR)
    if launchLock < 0 || flock(launchLock, LOCK_EX | LOCK_NB) != 0 {
      NSApp.terminate(nil)
      return
    }
    NSApp.setActivationPolicy(.accessory)
    NSApp.appearance = NSAppearance(named: .aqua)
    library.seedWelcome()
    makeMenu()
    makeRail()
    registerHotkeys()
    observers.append(
      NotificationCenter.default.addObserver(
        forName: NSApplication.didChangeScreenParametersNotification, object: nil, queue: .main
      ) { [weak self] _ in self?.positionRail() })
    observers.append(
      NotificationCenter.default.addObserver(
        forName: NSMenu.didBeginTrackingNotification, object: nil, queue: .main
      ) { [weak self] _ in self?.menuTracking = true })
    observers.append(
      NotificationCenter.default.addObserver(
        forName: NSMenu.didEndTrackingNotification, object: nil, queue: .main
      ) { [weak self] _ in
        self?.menuTracking = false
        self?.keepUntil = Date().addingTimeInterval(1)
      })
    hoverTimer = Timer.scheduledTimer(withTimeInterval: 0.1, repeats: true) { [weak self] _ in
      self?.hoverTick()
    }
    library.changed = { [weak self] in self?.keepUntil = Date().addingTimeInterval(2.5) }
    for shot in library.shots where shot.isPinned { pin(shot, restoring: true) }
    showRail()
    if let reason = library.readOnlyReason {
      let alert = NSAlert()
      alert.messageText = "资料库已进入保护模式"
      alert.informativeText = reason
      alert.runModal()
    }
    if options.demoScroll {
      library.capturing = true
      rail.orderOut(nil)
      beginScroll(NSRect(x: 100, y: 100, width: 320, height: 240))
    }
    if options.demoCapture {
      let snapshot = makeDemoSnapshot()
      DispatchQueue.main.asyncAfter(deadline: .now() + 0.3) {
        self.library.capturing = true
        self.rail.orderOut(nil)
        self.presentCapture(snapshot)
      }
    }
  }
  func applicationWillTerminate(_ notification: Notification) {
    hoverTimer?.invalidate()
    scrollSession?.stop()
    for pin in pins.values { pin.persist() }
    for hotkey in hotkeys { UnregisterEventHotKey(hotkey) }
    if let handler = hotkeyHandler { RemoveEventHandler(handler) }
    for observer in observers { NotificationCenter.default.removeObserver(observer) }
    if launchLock >= 0 { close(launchLock) }
  }
  func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool
  {
    if library.capturing {
      if let captureController {
        captureController.overlay.makeKeyAndOrderFront(nil)
      } else {
        scrollSession?.window?.makeKeyAndOrderFront(nil)
      }
    } else {
      showRail()
    }
    return true
  }
  func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { false }
  func hoverTick(
    point: NSPoint = NSEvent.mouseLocation, now: Date = Date(),
    pressedMouseButtons: Int = NSEvent.pressedMouseButtons
  ) {
    guard !library.capturing, !menuTracking, !options.keepRailOpen else { return }
    let inHotspot =
      configuration.hoverEnabled
      && NSScreen.screens.contains(where: {
        abs(point.x - $0.frame.midX) < 65 && point.y > $0.frame.maxY - 4 && point.y <= $0.frame.maxY
      })
    if inHotspot {
      if hoverSince == nil { hoverSince = now }
      // One activation per visit: the timer must not restart the swing every tick.
      if !hoverTriggered, now.timeIntervalSince(hoverSince!) >= configuration.hoverDelayMs / 1000 {
        hoverTriggered = true
        if !rail.isVisible { showRail() }
      }
    } else {
      hoverSince = nil
      hoverTriggered = false
    }
    guard rail.isVisible else { return }
    // The trigger strip is above the rail, but staying there should keep it open.
    if inHotspot || rail.frame.contains(point) || now < keepUntil || pressedMouseButtons != 0 {
      outsideSince = nil
    } else {
      if outsideSince == nil { outsideSince = now }
      if now.timeIntervalSince(outsideSince!) >= configuration.hideDelayMs / 1000 {
        rail.orderOut(nil)
        outsideSince = nil
      }
    }
  }
  func makeMenu() {
    status = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
    status.button?.image = NSImage(
      systemSymbolName: "photo.on.rectangle.angled", accessibilityDescription: "随手挂")
    let menu = NSMenu()
    func item(_ title: String, _ action: Selector, _ key: String = "") {
      let item = NSMenuItem(title: title, action: action, keyEquivalent: key)
      item.target = self
      menu.addItem(item)
    }
    item("截图  \(configuration.capture.description)", #selector(capture))
    item("展开 / 收起  \(configuration.toggle.description)", #selector(toggleRail))
    item("截图历史…", #selector(showHistory))
    item("导入图片…", #selector(importImages))
    menu.addItem(.separator())
    item("挂回所有贴图", #selector(returnAllPins))
    item("打开截图文件夹", #selector(openFolder))
    item("设置…", #selector(showPreferences))
    menu.addItem(.separator())
    item("退出随手挂", #selector(quit), "q")
    status.menu = menu
    let mainMenu = NSMenu()
    let appItem = NSMenuItem()
    let editItem = NSMenuItem()
    mainMenu.addItem(appItem)
    appItem.submenu = menu.copy() as? NSMenu
    mainMenu.addItem(editItem)
    let edit = NSMenu(title: "编辑")
    for (title, action, key) in [
      ("撤销", "undo:", "z"), ("剪切", "cut:", "x"), ("复制", "copy:", "c"), ("粘贴", "paste:", "v"),
      ("全选", "selectAll:", "a"),
    ] { edit.addItem(withTitle: title, action: Selector(action), keyEquivalent: key) }
    editItem.submenu = edit
    let windowItem = NSMenuItem()
    let windowMenu = NSMenu(title: "窗口")
    mainMenu.addItem(windowItem)
    windowItem.submenu = windowMenu
    let close = NSMenuItem(title: "关闭窗口", action: #selector(closeActiveWindow), keyEquivalent: "w")
    close.target = self
    windowMenu.addItem(close)
    NSApp.mainMenu = mainMenu
  }
  func makeRail() {
    rail = FloatingPanel(
      contentRect: .zero, styleMask: [.borderless, .nonactivatingPanel], backing: .buffered,
      defer: false)
    rail.isReleasedWhenClosed = false
    rail.level = .floating
    rail.backgroundColor = .clear
    rail.isOpaque = false
    rail.hasShadow = false
    rail.becomesKeyOnlyIfNeeded = true
    rail.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
    rail.contentView = NSHostingView(rootView: RailView(library: library, app: self))
    positionRail()
  }
  func positionRail() {
    let screen =
      NSScreen.screens.first(where: { String($0.displayID) == configuration.railScreen })
      ?? NSScreen.screens.first(where: { $0.frame.contains(NSEvent.mouseLocation) }) ?? NSScreen
      .main!
    let bounds = screen.visibleFrame
    let height = min(bounds.height, 170 + configuration.photoWidth / 196 * 206)
    rail.setFrame(
      NSRect(
        x: bounds.minX + 16, y: bounds.maxY - configuration.railTopMargin - height,
        width: bounds.width - 32, height: height), display: true)
  }
  func showRail() {
    positionRail()
    keepUntil = Date().addingTimeInterval(2.5)
    outsideSince = nil
    rail.orderFrontRegardless()
    NotificationCenter.default.post(name: Notification.Name("SnapRailSwing"), object: nil)
  }
  func hideRail() {
    rail.orderOut(nil)
    keepUntil = .distantPast
    outsideSince = nil
    hoverSince = nil
    // Require leaving the trigger strip before a hover can reopen the rail.
    hoverTriggered = true
  }
  @objc func toggleRail() { if rail.isVisible { hideRail() } else { showRail() } }
  func showMore() {
    if library.railShots.isEmpty && library.shots.contains(where: \.isPinned) {
      returnAllPins()
      hideRail()
    } else if library.railShots.isEmpty {
      capture()
    } else {
      showHistory()
    }
  }
  @objc func showHistory() {
    hideRail()
    if history == nil {
      history = window(
        title: "随手挂 · 历史", size: NSSize(width: 1000, height: 760),
        view: HistoryView(library: library, app: self))
    }
    library.recoverRestoredImages()
    activate(history!)
  }
  @objc func showPreferences() {
    hideRail()
    if preferences == nil || preferences?.isVisible == false {
      preferences = window(
        title: "随手挂 · 设置", size: NSSize(width: 920, height: 660),
        view: SettingsView(app: self, draft: configuration))
    }
    activate(preferences!)
  }
  @objc func openFolder() { NSWorkspace.shared.open(library.images) }
  @objc func quit() { NSApp.terminate(nil) }
  @objc func closeActiveWindow() {
    if library.capturing {
      if let captureController {
        captureController.toolbar.cancel?()
      } else {
        scrollSession?.cancel()
      }
      return
    }
    if let pin = pins.values.first(where: { $0.window === NSApp.keyWindow }) {
      pin.returnToRail()
    } else {
      NSApp.keyWindow?.performClose(nil)
    }
  }
  @objc func returnAllPins() {
    for pin in Array(pins.values) { pin.returnToRail() }
    showRail()
  }
  func window<V: View>(title: String, size: NSSize, view: V) -> NSWindow {
    let window = EscapeWindow(
      contentRect: NSRect(origin: .zero, size: size),
      styleMask: [.titled, .closable, .miniaturizable, .resizable], backing: .buffered, defer: false
    )
    window.title = title
    window.closesOnEscape = !title.contains("设置") && !title.contains("历史")
    window.isReleasedWhenClosed = false
    window.contentView = NSHostingView(rootView: view)
    window.center()
    return window
  }
  func activate(_ window: NSWindow) {
    NSApp.activate(ignoringOtherApps: true)
    window.makeKeyAndOrderFront(nil)
  }
  @objc func capture() {
    guard !library.capturing, !library.maintenance else { return }
    guard CGPreflightScreenCaptureAccess() else {
      CGRequestScreenCaptureAccess()
      library.message = "请在系统设置中允许随手挂录制屏幕，然后重启应用"
      showPreferences()
      return
    }
    library.capturing = true
    wasVisibleBeforeCapture = NSApp.windows.filter(\.isVisible)
    for window in wasVisibleBeforeCapture { window.orderOut(nil) }
    Task { @MainActor in
      do {
        try await Task.sleep(nanoseconds: 220_000_000)
        let snapshot = try await ScreenGrabber.desktop()
        presentCapture(snapshot)
      } catch {
        library.message = "截图未完成：\(error.localizedDescription)"
        endCapture()
      }
    }
  }
  func presentCapture(_ snapshot: DesktopSnapshot) {
    let controller = CaptureController(snapshot: snapshot)
    captureController = controller
    controller.completed = { [weak self] result in
      guard let self else { return false }
      if self.save(result) {
        self.endCapture()
        return true
      }
      return false
    }
    controller.cancelled = { [weak self] in self?.endCapture() }
    controller.ocrRequested = { [weak self] image in self?.ocr(image) }
    controller.scrollRequested = { [weak self] region in self?.beginScroll(region) }
    controller.show()
  }
  func endCapture() {
    library.capturing = false
    captureController = nil
    scrollSession = nil
    for window in wasVisibleBeforeCapture { window.orderFrontRegardless() }
    wasVisibleBeforeCapture = []
    showRail()
  }
  func beginScroll(_ region: NSRect) {
    captureController = nil
    // Keep app windows hidden while sampling, including floating pins.
    let session: ScrollSession
    if options.demoScroll {
      var index = 0
      session = ScrollSession(
        region: region,
        captureFrame: { _ in
          defer { index += 1 }
          return demoScrollFrame(index)
        })
    } else {
      session = ScrollSession(region: region)
    }
    scrollSession = session
    session.finished = { [weak self] image in
      self?.endCapture()
      self?.edit(image)
    }
    session.cancelled = { [weak self] in self?.endCapture() }
    session.show()
  }
  @objc func importImages() {
    guard !library.maintenance else { return }
    let panel = NSOpenPanel()
    panel.allowedContentTypes = [.image]
    panel.allowsMultipleSelection = true
    NSApp.activate(ignoringOtherApps: true)
    panel.begin { response in
      guard response == .OK else { return }
      for url in panel.urls {
        guard let image = NSImage(contentsOf: url) else { continue }
        do { try self.library.add(image) } catch {
          self.library.message = error.localizedDescription
        }
      }
      self.showRail()
    }
  }
  func save(_ result: EditResult) -> Bool {
    do {
      try library.add(result.image, original: result.original, document: result.document)
      showRail()
      return true
    } catch {
      library.message = "保存失败：\(error.localizedDescription)"
      let alert = NSAlert(error: error)
      if let overlay = captureController?.overlay {
        alert.window.level = NSWindow.Level(rawValue: overlay.level.rawValue + 2)
      }
      alert.runModal()
      return false
    }
  }
  func edit(_ image: NSImage, document: AnnotationDocument = AnnotationDocument()) {
    let editor = EditorController(image: image, document: document) { [weak self] result in
      self?.save(result) ?? false
    }
    editors.append(editor)
    editor.didClose = { [weak self] id in self?.editors.removeAll { $0.id == id } }
    editor.showOCR = { [weak self] in self?.ocr($0) }
    activate(editor.window!)
  }
  func edit(_ shot: Shot) {
    if let (image, document) = library.editable(shot) { edit(image, document: document) }
  }
  func preview(_ shot: Shot) {
    guard let image = library.image(shot) else {
      library.message = "图片文件已不存在"
      return
    }
    let window = window(
      title: "随手挂 · 图片预览", size: NSSize(width: 820, height: 620),
      view: PreviewView(image: image, app: self, shot: shot))
    windows.removeAll { !$0.isVisible }
    windows.append(window)
    activate(window)
  }
  func pin(_ shot: Shot, restoring: Bool = false) {
    if let existing = pins[shot.id] {
      existing.window?.orderFrontRegardless()
      return
    }
    guard let image = library.image(shot) else {
      library.update(shot) { $0.isPinned = false }
      return
    }
    guard !library.maintenance else { return }
    let controller = PinController(shot: shot, image: image, app: self)
    pins[shot.id] = controller
    if !restoring { library.update(shot) { $0.isPinned = true } }
    controller.showWindow(nil)
    controller.window?.orderFrontRegardless()
  }
  func recycle(_ selected: [Shot]) {
    guard !selected.isEmpty else {
      library.message = "请先选择图片"
      return
    }
    let alert = NSAlert()
    alert.messageText = "将 \(selected.count) 张图片及其标注移到系统废纸篓？"
    alert.informativeText = "其中 \(selected.filter(\.favorite).count) 张收藏。可通过“撤销上次删除”恢复。"
    alert.addButton(withTitle: "取消")
    alert.addButton(withTitle: "移到废纸篓")
    guard alert.runModal() == .alertSecondButtonReturn else { return }
    for shot in selected {
      if let pin = pins[shot.id] {
        pin.closeWithoutReturning()
        pins.removeValue(forKey: shot.id)
      }
    }
    library.recycle(selected)
  }
  func ocr(_ shot: Shot) { if let image = library.image(shot) { ocr(image) } }
  func ocr(_ image: NSImage) {
    library.recognize(image) { text in
      let window = self.window(
        title: "随手挂 · 识别文字", size: NSSize(width: 620, height: 440), view: OCRView(text: text))
      self.windows.removeAll { !$0.isVisible }
      self.windows.append(window)
      if self.captureController != nil {
        window.level = NSWindow.Level(rawValue: NSWindow.Level.screenSaver.rawValue + 3)
      }
      self.activate(window)
    }
  }
  func beginMaintenance() throws {
    guard !library.capturing, !library.maintenance else { throw appError("请先结束截图或当前资料操作") }
    for pin in pins.values {
      pin.rememberTimer?.invalidate()
      pin.persist()
    }
    hiddenForMaintenance = NSApp.windows.filter { $0.isVisible && $0 !== preferences }
    for window in hiddenForMaintenance { window.orderOut(nil) }
    library.maintenance = true
  }
  func endMaintenance() {
    library.maintenance = false
    for window in hiddenForMaintenance { window.orderFrontRegardless() }
    hiddenForMaintenance = []
  }
  func saveConfiguration(_ value: Configuration) throws {
    try value.validate()
    if let profile {
      try JSONEncoder().encode(value).write(
        to: profile.appendingPathComponent("settings.json"), options: .atomic)
    } else {
      try SettingsStore.save(value)
    }
  }
  func relaunch() throws {
    let process = Process()
    process.executableURL = URL(fileURLWithPath: "/usr/bin/open")
    process.arguments =
      ["-n", Bundle.main.bundlePath, "--args", "--relaunch"]
      + (profile.map { ["--profile", $0.path] } ?? [])
      + (options.keepRailOpen ? ["--ui-review"] : [])
    try process.run()
    NSApp.terminate(nil)
  }
}
