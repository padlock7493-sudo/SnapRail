import AppKit
import Vision

func runLegacyRegression() throws {
  let pasteboard = NSPasteboard.general
  let previous = (NSPasteboard.general.pasteboardItems ?? []).map { item in
    item.types.compactMap { type in item.data(forType: type).map { (type, $0) } }
  }
  defer {
    pasteboard.clearContents()
    pasteboard.writeObjects(
      previous.map { entries in
        let item = NSPasteboardItem()
        for (type, data) in entries { item.setData(data, forType: type) }
        return item
      })
  }
  let root = FileManager.default.temporaryDirectory.appendingPathComponent(
    "SnapRail-tests-\(UUID().uuidString)")
  defer { try? FileManager.default.removeItem(at: root) }
  func expect(_ value: @autoclosure () throws -> Bool, _ message: String) throws {
    if try !value() { throw appError("SelfTest: " + message) }
  }
  try expect(NSColor(hex: "E34B4B").hex == "E34B4B", "sRGB palette round-trip drifted")
  let image = demoImage(size: NSSize(width: 800, height: 500))
  let canvas = AnnotationView(image: image)
  canvas.checkpoint()
  canvas.marks = [
    Mark(
      tool: 0, points: [NSPoint(x: 0.1, y: 0.1), NSPoint(x: 0.3, y: 0.3)], color: .red, width: 8)
  ]
  guard let rendered = canvas.rendered(),
    let result = NSBitmapImageRep(data: try rendered.pngData())
  else { throw appError("Render failed") }
  try expect(
    result.pixelsWide == 800 && result.pixelsHigh == 500, "Export changed pixel dimensions")
  canvas.undo(nil)
  try expect(canvas.marks.isEmpty, "Undo failed")
  canvas.redo(nil)
  try expect(canvas.marks.count == 1, "Redo failed")
  canvas.selected = 0
  canvas.style(ink: "2675D8", width: 6)
  try expect(
    canvas.marks[0].ink == "2675D8" && canvas.marks[0].width == 6, "Object restyling failed")
  canvas.deleteSelection()
  try expect(canvas.marks.isEmpty, "Object delete failed")
  canvas.undo(nil)
  try expect(canvas.marks.count == 1, "Object delete undo failed")
  canvas.marks = [
    Mark(
      tool: 5, points: [NSPoint(x: 0.52, y: 0.45), NSPoint(x: 0.95, y: 0.7)], color: .red, width: 4)
  ]
  let mosaic = try PixelBuffer(image: canvas.rendered()!)
  let original = try PixelBuffer(image: image)
  try expect(mosaic.data != original.data, "Mosaic did not change pixels")
  let outside = (20 * 800 + 20) * 4
  try expect(
    Array(mosaic.data[outside..<outside + 3]) == Array(original.data[outside..<outside + 3]),
    "Mosaic changed pixels outside selection")
  canvas.selection = NSRect(x: 0.1, y: 0.1, width: 0.5, height: 0.6)
  guard let cropped = canvas.result() else { throw appError("Crop failed") }
  try expect(
    cropped.image.pixels == NSSize(width: 400, height: 300), "Selection crop dimensions failed")
  let library = Library(root: root)
  let first = try library.add(cropped.image, original: cropped.original, document: cropped.document)
  let second = try library.add(image)
  let third = try library.add(image)
  let fourth = try library.add(image)
  try expect(FileManager.default.fileExists(atPath: library.url(first).path), "PNG not saved")
  let persisted = Library(root: root)
  try expect(persisted.shots.count == 4, "Library reload failed")
  let editable = persisted.editable(first)!
  try expect(
    editable.1.marks == cropped.document.marks && editable.0.pixels == cropped.original.pixels,
    "Editable original/document not preserved")
  let editor = AnnotationView(image: editable.0)
  editor.marks = editable.1.marks
  try expect(
    editor.rendered()?.pixels == cropped.image.pixels, "Editable document render dimensions changed"
  )
  library.toggleFavorite(first)
  library.toggleFavorite(second)
  library.toggleFavorite(third)
  library.toggleFavorite(fourth)
  try expect(library.shots.filter(\.favorite).count == 3, "Favorite limit failed")
  library.update(first) { $0.onRail = false }
  try expect(!library.railShots.contains { $0.id == first.id }, "Rail hide failed")
  library.move(third.id, before: second.id)
  let reloaded = Library(root: root)
  try expect(
    reloaded.shots.firstIndex { $0.id == third.id }! < reloaded.shots.firstIndex {
      $0.id == second.id
    }!, "Reorder persistence failed")
  library.update(second) {
    $0.isPinned = true
    $0.placement = PinPlacement(x: 10, y: 20, width: 400, height: 300, opacity: 0.7)
  }
  try expect(!library.railShots.contains { $0.id == second.id }, "Pinned image remained on rope")
  try expect(
    Library(root: root).shots.first { $0.id == second.id }?.placement?.opacity == 0.7,
    "Pin placement persistence failed")
  library.recycle([first])
  try expect(!library.shots.contains { $0.id == first.id }, "Recycle failed")
  let afterDelete = Library(root: root)
  try expect(afterDelete.recovery?.shots.first?.id == first.id, "Delete recovery not durable")
  afterDelete.undoDelete()
  try expect(
    afterDelete.shots.contains { $0.id == first.id }
      && afterDelete.editable(first)?.1.marks == cropped.document.marks,
    "Undo delete lost original or marks")
  let request = VNRecognizeTextRequest()
  request.recognitionLevel = .accurate
  try VNImageRequestHandler(url: library.url(third)).perform([request])
  let text = (request.results ?? []).compactMap { $0.topCandidates(1).first?.string }.joined(
    separator: " ")
  try expect(text.localizedCaseInsensitiveContains("SnapRail"), "Vision OCR failed: \(text)")
  let destination = root.deletingLastPathComponent().appendingPathComponent(
    "SnapRail-migration-\(UUID().uuidString)")
  defer { try? FileManager.default.removeItem(at: destination) }
  try SettingsStore.migrate(from: root, to: destination)
  try expect(Library(root: destination).shots.count == 4, "Migration failed")
  do {
    try SettingsStore.migrate(from: root, to: root.appendingPathComponent("child"))
    throw appError("Nested migration accepted")
  } catch {
    try expect(
      error.localizedDescription != "Nested migration accepted", "Nested migration accepted")
  }
  var invalid = Configuration()
  invalid.toggle = invalid.capture
  do {
    try invalid.validate()
    throw appError("Duplicate shortcuts accepted")
  } catch {
    try expect(
      error.localizedDescription != "Duplicate shortcuts accepted", "Duplicate shortcuts accepted")
  }
  let welcome = Library(root: root.appendingPathComponent("welcome"))
  welcome.seedWelcome()
  try expect(welcome.shots.count == 10, "Welcome cards missing")
  welcome.shots = []
  welcome.save()
  welcome.seedWelcome()
  try expect(welcome.shots.isEmpty, "Deleted welcome cards regenerated")
  // Unique deterministic row texture verifies overlap orientation and exact output bytes.
  let width = 96
  let height = 300
  let frameHeight = 120
  let shift = 47
  var bytes = [UInt8](repeating: 255, count: width * height * 4)
  var random: UInt64 = 123456
  for i in 0..<(width * height) {
    for c in 0..<3 {
      random = random &* 6_364_136_223_846_793_005 &+ 1
      bytes[i * 4 + c] = UInt8((random >> 32) & 255)
    }
  }
  func frame(_ start: Int) -> NSImage {
    PixelBuffer(
      width: width, height: frameHeight,
      data: Array(bytes[(start * width * 4)..<((start + frameHeight) * width * 4)])
    ).image()!
  }
  let stitcher = try ScrollStitcher(frame(0))
  try expect(try stitcher.append(frame(0)) == .unchanged, "Identical scroll frame not detected")
  try expect(try stitcher.append(frame(shift)) == .added, "Reliable overlap rejected")
  let stitched = try PixelBuffer(image: stitcher.render()!)
  try expect(
    stitched.height == frameHeight + shift
      && stitched.data == Array(bytes.prefix(width * (frameHeight + shift) * 4)),
    "Scroll stitch lost or duplicated rows")
  try expect(
    try stitcher.append(demoImage(size: NSSize(width: 96, height: 120))) == .noMatch,
    "Unrelated scroll frame accepted")
  print(
    "Verified annotations, object edits, crop, pixel mosaic, undo/redo, editable copies, durable deletion recovery, pin state, favorites, ordering, migration, welcome lifecycle, local OCR and exact scroll stitching"
  )
}
