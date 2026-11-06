import AppKit
import SwiftUI

func require(_ condition: @autoclosure () throws -> Bool, _ message: String) throws {
  if try !condition() { throw appError(message) }
}
func rejects(_ action: () throws -> Void) throws {
  var rejected = false
  do { try action() } catch { rejected = true }
  try require(rejected, "Invalid operation was accepted")
}
func withTestDirectory(_ body: (URL) throws -> Void) throws {
  let directory = FileManager.default.temporaryDirectory.appendingPathComponent(
    "SnapRail-regression-\(UUID().uuidString)", isDirectory: true)
  try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
  defer { try? FileManager.default.removeItem(at: directory) }
  try body(directory)
}
func texture(width: Int = 64, height: Int = 240, period: Int? = nil) -> PixelBuffer {
  var data = [UInt8](repeating: 255, count: width * height * 4)
  for y in 0..<height {
    var random = UInt64((y % (period ?? height)) + 1234)
    for x in 0..<width {
      for channel in 0..<3 {
        random = random &* 6_364_136_223_846_793_005 &+ 1
        data[(y * width + x) * 4 + channel] = UInt8((random >> 32) & 255)
      }
    }
  }
  return PixelBuffer(width: width, height: height, data: data)
}
func scrollFrame(_ buffer: PixelBuffer, start: Int, height: Int = 80) -> NSImage {
  PixelBuffer(
    width: buffer.width, height: height,
    data: Array(buffer.data[(start * buffer.width * 4)..<((start + height) * buffer.width * 4)])
  ).image()!
}
func runRegressionSuite() {
  check("CLI rejects missing profiles and keeps demo data isolated") {
    try rejects { _ = try LaunchOptions.parse(["--profile"]) }
    try rejects { _ = try LaunchOptions.parse(["--profile", "relative"]) }
    try rejects { _ = try LaunchOptions.parse(["--demo-capture"]) }
    try rejects { _ = try LaunchOptions.parse(["--unknown"]) }
    try rejects {
      _ = try LaunchOptions.parse(["--profile", "/tmp/qa", "--demo-capture", "--demo-scroll"])
    }
    let options = try LaunchOptions.parse(["--profile", "/tmp/qa", "--ui-review", "--demo-capture"])
    try require(
      options.profile?.path == "/tmp/qa" && options.keepRailOpen && options.demoCapture,
      "CLI options not preserved")
  }
  check("Top hover opens once, stays still and rearms only after leaving") {
    try withTestDirectory { directory in
      let app = AppDelegate(profile: directory)
      app.configuration.hoverEnabled = true
      app.configuration.hoverDelayMs = 100
      app.configuration.hideDelayMs = 100
      app.makeRail()
      defer { app.rail.close() }
      let screen = NSScreen.main!.frame
      let top = NSPoint(x: screen.midX, y: screen.maxY - 1)
      let away = NSPoint(x: screen.midX, y: screen.minY + 10)
      let start = Date().addingTimeInterval(20)
      var swings = 0
      let observer = NotificationCenter.default.addObserver(
        forName: Notification.Name("SnapRailSwing"), object: nil, queue: nil
      ) { _ in swings += 1 }
      defer { NotificationCenter.default.removeObserver(observer) }
      func tick(_ point: NSPoint, _ seconds: Double) {
        app.hoverTick(point: point, now: start.addingTimeInterval(seconds), pressedMouseButtons: 0)
      }
      tick(top, 0)
      try require(!app.rail.isVisible, "Opened before hover delay")
      for i in 1...100 { tick(top, Double(i) / 5) }
      try require(swings == 1 && app.rail.isVisible, "Stationary hover restarted swing or hid rail")
      app.toggleRail()
      tick(top, 21)
      try require(!app.rail.isVisible && swings == 1, "Manual collapse immediately reopened")
      tick(away, 22)
      tick(top, 23)
      tick(top, 23.2)
      try require(app.rail.isVisible && swings == 2, "Leaving did not rearm hover")
      tick(away, 24)
      tick(away, 24.2)
      try require(!app.rail.isVisible, "Leaving did not hide after delay")
      app.showRail()
      tick(top, 25)
      tick(top, 25.2)
      try require(swings == 3, "Hover restarted animation on an already visible rail")
    }
  }
  check("History, Settings and More collapse rail without immediate hover reopening") {
    try withTestDirectory { directory in
      let app = AppDelegate(profile: directory)
      _ = try app.library.add(
        demoImage(size: NSSize(width: 32, height: 32)), copyToClipboard: false)
      app.makeRail()
      defer {
        app.history?.close()
        app.preferences?.close()
        app.rail.close()
      }
      let screen = NSScreen.main!.frame
      let top = NSPoint(x: screen.midX, y: screen.maxY - 1)
      for open in [app.showHistory, app.showPreferences, app.showMore] {
        app.showRail()
        app.hoverTriggered = false
        open()
        try require(!app.rail.isVisible, "Opening a destination left rail expanded")
        for tick in 0...30 {
          app.hoverTick(
            point: top, now: Date().addingTimeInterval(Double(tick)), pressedMouseButtons: 0)
        }
        try require(!app.rail.isVisible, "Stationary cursor reopened rail over destination")
        try require(
          app.history?.isVisible == true || app.preferences?.isVisible == true,
          "Destination window did not open")
        app.history?.close()
        app.preferences?.close()
      }
    }
  }
  check("Default images stay beside app; read-only and translocated apps fall back safely") {
    let app = URL(fileURLWithPath: "/Users/example/Applications/SnapRail.app")
    try require(
      SettingsStore.defaultDataRoot(appURL: app, isWritable: { _ in true }).path
        == "/Users/example/Applications/SnapRail-data", "Default data is not beside app")
    try require(
      SettingsStore.defaultDataRoot(appURL: app, isWritable: { _ in false }) == SettingsStore.base,
      "Read-only app directory did not fall back")
    let translocated = URL(
      fileURLWithPath: "/private/var/folders/test/AppTranslocation/id/d/SnapRail.app")
    try require(
      SettingsStore.defaultDataRoot(appURL: translocated, isWritable: { _ in true })
        == SettingsStore.base,
      "Translocated app used temporary data path")
    try require(
      SettingsStore.defaultDataRoot(
        appURL: URL(fileURLWithPath: "/tmp/SnapRail"), isWritable: { _ in true })
        == SettingsStore.base,
      "Unbundled development executable used build directory")
  }
  check("Configuration defaults and serialization") {
    let config = Configuration()
    try config.validate()
    let decoded = try JSONDecoder().decode(Configuration.self, from: JSONEncoder().encode(config))
    try require(config == decoded, "Configuration round-trip changed values")
    try require(config.capture.description == "Control+Option+A", "Mac modifier names missing")
    var legacy = config.capture
    legacy.key = 36
    legacy.label = "Enter"
    try require(legacy.description == "Control+Option+Return", "Legacy Return label not normalized")
    legacy.modifiers = 256 | 512 | 2048 | 4096
    try require(
      legacy.description == "Control+Option+Shift+Command+Return", "Modifier mapping changed")
  }
  check("Reject unmodified letters, arrows, modifier keys and unknown flags") {
    for shortcut in [
      Shortcut(key: 0, modifiers: 0, label: "A"), Shortcut(key: 123, modifiers: 0, label: "Left"),
      Shortcut(key: 56, modifiers: 256, label: "Shift"), Shortcut(key: 0, modifiers: 1, label: "A"),
    ] {
      try rejects { try shortcut.validate() }
    }
    try Shortcut(key: 122, modifiers: 0, label: "F1").validate()
  }
  check("Reject duplicate shortcuts and out-of-range settings") {
    var config = Configuration()
    config.toggle = config.capture
    try rejects { try config.validate() }
    config = Configuration()
    config.photoWidth = .nan
    try rejects { try config.validate() }
    config = Configuration()
    config.hideDelayMs = 299
    try rejects { try config.validate() }
    config = Configuration()
    config.dataRoot = "relative/path"
    try rejects { try config.validate() }
  }
  check("Profile keeps its migrated data root after restart") {
    try withTestDirectory { directory in
      let profile = directory.appendingPathComponent("profile")
      let data = directory.appendingPathComponent("moved")
      try FileManager.default.createDirectory(at: profile, withIntermediateDirectories: true)
      var config = Configuration()
      config.dataRoot = data.path
      try JSONEncoder().encode(config).write(to: profile.appendingPathComponent("settings.json"))
      let app = AppDelegate(profile: profile)
      try require(app.library.root.path == data.path, "Profile incorrectly reset dataRoot")
    }
  }
  check("Damaged index without backup is read-only and never reseeds") {
    try withTestDirectory { directory in
      let file = directory.appendingPathComponent("library.json")
      let bytes = Data("damaged-json".utf8)
      try bytes.write(to: file)
      let library = Library(root: directory)
      try require(library.readOnlyReason != nil, "Damaged index was not protected")
      library.seedWelcome()
      try rejects {
        _ = try library.add(demoImage(size: NSSize(width: 100, height: 80)), copyToClipboard: false)
      }
      try require(try Data(contentsOf: file) == bytes, "Damaged index was overwritten")
      try require(
        try FileManager.default.contentsOfDirectory(atPath: library.images.path).isEmpty,
        "Protected library wrote images")
    }
  }
  check("Recover known-good index backup") {
    try withTestDirectory { directory in
      let library = Library(root: directory)
      let image = demoImage(size: NSSize(width: 100, height: 80))
      let first = try library.add(image, copyToClipboard: false)
      _ = try library.add(image, copyToClipboard: false)
      try Data("broken".utf8).write(to: library.manifest)
      let restored = Library(root: directory)
      try require(
        restored.readOnlyReason == nil && restored.shots.map(\.id) == [first.id],
        "Known-good backup not loaded")
      try restored.persist()
      try require(Library(root: directory).shots.count == 1, "Restored index not writable")
    }
  }
  check("Reject duplicate IDs and unsafe image filenames") {
    try withTestDirectory { directory in
      let shot = Shot(id: UUID(), filename: "safe.png", created: Date())
      try JSONEncoder().encode([shot, shot]).write(
        to: directory.appendingPathComponent("library.json"))
      try require(Library(root: directory).readOnlyReason != nil, "Duplicate IDs accepted")
      let bad = Shot(id: UUID(), filename: "../escape.png", created: Date())
      try JSONEncoder().encode([bad]).write(to: directory.appendingPathComponent("library.json"))
      try require(Library(root: directory).readOnlyReason != nil, "Traversal filename accepted")
    }
  }
  check("Favorite and reorder writes roll back after disk failure") {
    try withTestDirectory { directory in
      let library = Library(root: directory)
      let image = demoImage(size: NSSize(width: 100, height: 80))
      let first = try library.add(image, copyToClipboard: false)
      let second = try library.add(image, copyToClipboard: false)
      let before = library.shots
      try FileManager.default.removeItem(at: library.manifest)
      try FileManager.default.createDirectory(
        at: library.manifest, withIntermediateDirectories: false)
      library.toggleFavorite(first)
      try require(library.shots == before, "Favorite remained after failed save")
      library.move(first.id, before: second.id)
      try require(library.shots == before, "Order remained after failed save")
      library.applyOrder([first.id, first.id, second.id])
      try require(library.shots == before, "Bulk reorder failed to roll back")
    }
  }
  check("Migration lock blocks all mutations and image writes") {
    try withTestDirectory { directory in
      let library = Library(root: directory)
      let image = demoImage(size: NSSize(width: 100, height: 80))
      let shot = try library.add(image, copyToClipboard: false)
      let before = library.shots
      library.maintenance = true
      library.toggleFavorite(shot)
      library.update(shot) { $0.onRail = false }
      try rejects { _ = try library.add(image, copyToClipboard: false) }
      try require(library.shots == before, "Mutation bypassed migration lock")
      library.maintenance = false
    }
  }
  check("Copy migration preserves source and validates destination") {
    try withTestDirectory { directory in
      let source = directory.appendingPathComponent("source")
      let target = directory.appendingPathComponent("target")
      let library = Library(root: source)
      let image = demoImage(size: NSSize(width: 100, height: 80))
      let shot = try library.add(image, copyToClipboard: false)
      let bytes = try Data(contentsOf: library.url(shot))
      try SettingsStore.migrate(from: source, to: target)
      try require(
        try Data(contentsOf: Library(root: target).url(shot)) == bytes,
        "Migration changed image bytes")
      try require(try Data(contentsOf: library.url(shot)) == bytes, "Migration modified source")
      try rejects { try SettingsStore.migrate(from: source, to: target) }
    }
  }
  check("Failed migration rejects symlinks and leaves target empty") {
    try withTestDirectory { directory in
      let source = directory.appendingPathComponent("source")
      let target = directory.appendingPathComponent("target")
      let library = Library(root: source)
      let original = directory.appendingPathComponent("private.png")
      try Data("must-not-follow".utf8).write(to: original)
      try FileManager.default.createSymbolicLink(
        at: library.images.appendingPathComponent("link.png"), withDestinationURL: original)
      try FileManager.default.createDirectory(at: target, withIntermediateDirectories: true)
      try rejects { try SettingsStore.migrate(from: source, to: target) }
      try require(
        try FileManager.default.contentsOfDirectory(atPath: target.path).isEmpty,
        "Failed migration left partial target")
      try require(
        !(try FileManager.default.contentsOfDirectory(atPath: directory.path)).contains {
          $0.hasPrefix(".SnapRail-migration-")
        }, "Staging directory leaked")
    }
  }
  check("Pending deletion recovery follows migrated data root") {
    try withTestDirectory { directory in
      let source = Library(root: directory.appendingPathComponent("source"))
      let target = directory.appendingPathComponent("target")
      let shot = try source.add(
        demoImage(size: NSSize(width: 100, height: 80)), copyToClipboard: false)
      source.recycle([shot])
      guard let group = source.recovery else { throw appError("Delete journal missing") }
      defer {
        for file in group.files where FileManager.default.fileExists(atPath: file.trash.path) {
          try? FileManager.default.moveItem(at: file.trash, to: file.source)
        }
      }
      try SettingsStore.migrate(from: source.root, to: target)
      let moved = Library(root: target)
      moved.undoDelete()
      try require(
        FileManager.default.fileExists(atPath: moved.url(shot).path),
        "Undo restored into old directory")
      try require(
        !FileManager.default.fileExists(atPath: source.url(shot).path),
        "Migration recovery wrote into source")
      try require(
        moved.shots.contains { $0.id == shot.id }, "Recovered image absent from new index")
    }
  }
  check("Undo refuses collisions and preserves existing files") {
    try withTestDirectory { directory in
      let library = Library(root: directory)
      let shot = try library.add(
        demoImage(size: NSSize(width: 100, height: 80)), copyToClipboard: false)
      library.recycle([shot])
      let bytes = Data("existing-file".utf8)
      try bytes.write(to: library.url(shot))
      library.undoDelete()
      try require(
        try Data(contentsOf: library.url(shot)) == bytes, "Undo overwrote an existing file")
      try require(library.recovery != nil, "Failed undo lost recovery journal")
      try FileManager.default.removeItem(at: library.url(shot))
      library.undoDelete()
      try require(
        library.recovery == nil && library.shots.contains { $0.id == shot.id },
        "Retry did not restore")
    }
  }
  check("Recycle rolls back file moves when index write fails") {
    try withTestDirectory { directory in
      let library = Library(root: directory)
      let shot = try library.add(
        demoImage(size: NSSize(width: 100, height: 80)), copyToClipboard: false)
      let bytes = try Data(contentsOf: library.url(shot))
      try FileManager.default.removeItem(at: library.manifest)
      try FileManager.default.createDirectory(
        at: library.manifest, withIntermediateDirectories: false)
      library.recycle([shot])
      try require(
        try Data(contentsOf: library.url(shot)) == bytes, "Failed delete did not restore file")
      try require(library.shots.contains { $0.id == shot.id }, "Failed delete changed index")
    }
  }
  check("All six drawing tools render actual pixels") {
    let image = texture(width: 200, height: 160).image()!
    let canvas = AnnotationView(image: image)
    let baseline = try PixelBuffer(image: canvas.rendered()!).data
    for tool in 0...5 {
      canvas.marks = [
        Mark(
          tool: tool, points: [NSPoint(x: 0.2, y: 0.7), NSPoint(x: 0.7, y: 0.3)],
          color: NSColor(hex: "E34B4B"), width: 4, text: "中文\nTEST")
      ]
      try require(
        try PixelBuffer(image: canvas.rendered()!).data != baseline, "Tool \(tool) did not render")
    }
  }
  check("Annotation undo/redo branching and clean state") {
    let canvas = AnnotationView(image: demoImage(size: NSSize(width: 200, height: 160)))
    let mark = Mark(
      tool: 0, points: [NSPoint(x: 0.2, y: 0.2), NSPoint(x: 0.7, y: 0.7)], color: .red, width: 4)
    canvas.marks = [mark]
    canvas.initialMarks = [mark]
    try require(!canvas.isDirty, "Unchanged saved document marked dirty")
    canvas.selected = 0
    canvas.style(ink: "2675D8")
    try require(canvas.isDirty, "Edited document not dirty")
    canvas.undo(nil)
    try require(!canvas.isDirty && !canvas.redoStates.isEmpty, "Undo did not restore clean state")
    canvas.selected = 0
    canvas.style(width: 6)
    try require(canvas.redoStates.isEmpty, "New edit retained stale redo branch")
  }
  check("Invalid annotation values are rejected before saving") {
    var document = AnnotationDocument(marks: [
      Mark(tool: 2, points: [.zero, NSPoint(x: 0.5, y: 0.5)], color: .red, width: 4)
    ])
    try document.validate()
    document.marks[0].fontSize = .infinity
    try rejects { try document.validate() }
    document.marks[0].fontSize = 24
    document.marks[0].points[0].x = .nan
    try rejects { try document.validate() }
  }
  check("Scroll exact overlap, unchanged and incompatible frame") {
    let buffer = texture()
    let stitcher = try ScrollStitcher(scrollFrame(buffer, start: 0))
    try require(
      try stitcher.append(scrollFrame(buffer, start: 20)) == .added, "Unique overlap rejected")
    try require(
      try stitcher.append(scrollFrame(buffer, start: 20)) == .unchanged, "Identical frame appended")
    let rendered = try PixelBuffer(image: stitcher.render()!)
    try require(
      rendered.data == Array(buffer.data.prefix(buffer.width * 100 * 4)),
      "Pixels lost or duplicated")
    try require(
      try stitcher.append(texture(width: 65, height: 80).image()!) == .noMatch,
      "Different dimensions accepted")
  }
  check("Scroll rejects ambiguous repeated rows") {
    let buffer = texture(period: 8)
    let stitcher = try ScrollStitcher(scrollFrame(buffer, start: 0))
    try require(
      try stitcher.append(scrollFrame(buffer, start: 5)) == .noMatch,
      "Repeated rows aligned ambiguously")
    try require(stitcher.frames == 1 && stitcher.height == 80, "Rejected frame changed stitcher")
  }
  check("Scroll frame, height and pixel limits preserve previous output") {
    let buffer = texture()
    for limits in [
      ScrollLimits(frames: 1, height: 1000, pixels: 1_000_000),
      ScrollLimits(frames: 80, height: 90, pixels: 1_000_000),
      ScrollLimits(frames: 80, height: 1000, pixels: 6000),
    ] {
      let stitcher = try ScrollStitcher(scrollFrame(buffer, start: 0), limits: limits)
      try require(
        try stitcher.append(scrollFrame(buffer, start: 20)) == .limit, "Limit not enforced")
      try require(stitcher.frames == 1 && stitcher.height == 80, "Limit discarded previous image")
    }
    try rejects {
      _ = try ScrollStitcher(scrollFrame(buffer, start: 0), limits: ScrollLimits(height: 60))
    }
  }
  check("Retina and negative-origin desktop crop preserves exact coordinates") {
    let image = texture(width: 400, height: 240).image()!
    let snapshot = DesktopSnapshot(
      image: image, frame: NSRect(x: -100, y: -20, width: 200, height: 120), windows: [])
    let crop = try snapshot.crop(NSRect(x: -80, y: 0, width: 50, height: 30))
    try require(crop.pixels == NSSize(width: 100, height: 60), "Retina crop has wrong size")
    let expected = try image.cropped(to: NSRect(x: 40, y: 40, width: 100, height: 60))
    try require(
      try PixelBuffer(image: crop).data == PixelBuffer(image: expected).data,
      "Global crop used wrong origin")
  }
  check("Mosaic and annotations remain pixel-identical after editing reload") {
    try withTestDirectory { directory in
      let image = texture(width: 400, height: 300).image()!
      let canvas = AnnotationView(image: image)
      canvas.selection = NSRect(x: 0.1, y: 0.1, width: 0.7, height: 0.7)
      canvas.marks = [
        Mark(
          tool: 5, points: [NSPoint(x: 0.2, y: 0.2), NSPoint(x: 0.6, y: 0.6)], color: .red, width: 4
        )
      ]
      guard let result = canvas.result() else { throw appError("Cannot render document") }
      let library = Library(root: directory)
      let shot = try library.add(
        result.image, original: result.original, document: result.document, copyToClipboard: false)
      guard let (original, document) = Library(root: directory).editable(shot) else {
        throw appError("Cannot reopen document")
      }
      let editor = AnnotationView(image: original)
      editor.marks = document.marks
      try require(
        try PixelBuffer(image: editor.rendered()!).data == PixelBuffer(image: result.image).data,
        "Mosaic changed on reload")
    }
  }
  check("Long screenshot pin stays within screen and responds to wheel zoom") {
    try withTestDirectory { directory in
      let app = AppDelegate(profile: directory)
      let image = demoImage(size: NSSize(width: 2150, height: 3614))
      let shot = try app.library.add(image, copyToClipboard: false)
      let pin = PinController(shot: shot, image: image, app: app)
      defer { pin.closeWithoutReturning() }
      guard let window = pin.window else { throw appError("Missing pin window") }
      window.contentView?.layoutSubtreeIfNeeded()
      let work = NSScreen.main!.visibleFrame
      try require(
        window.frame.width <= work.width && window.frame.height <= work.height,
        "Image intrinsic size expanded pin beyond screen")
      let width = window.frame.width
      guard
        let event = CGEvent(
          scrollWheelEvent2Source: nil, units: .pixel, wheelCount: 1,
          wheel1: -40, wheel2: 0, wheel3: 0),
        let wheel = NSEvent(cgEvent: event)
      else { throw appError("Cannot construct wheel") }
      pin.scroll(wheel)
      window.contentView?.layoutSubtreeIfNeeded()
      try require(window.frame.width < width, "Wheel did not shrink pin")
      pin.persist()
      try require(
        app.library.shots.first?.placement?.width == window.frame.width,
        "Resized pin not persisted")
    }
  }
  check("Preview SwiftUI/AppKit bridge renders without runtime trap") {
    let host = NSHostingView(
      rootView: PreviewView(image: demoImage(size: NSSize(width: 300, height: 200))))
    host.frame = NSRect(x: 0, y: 0, width: 700, height: 500)
    host.layoutSubtreeIfNeeded()
    guard let bitmap = host.bitmapImageRepForCachingDisplay(in: host.bounds) else {
      throw appError("Preview bitmap unavailable")
    }
    host.cacheDisplay(in: host.bounds, to: bitmap)
    try require(bitmap.pixelsWide > 0 && bitmap.pixelsHigh > 0, "Preview did not render")
  }
}
