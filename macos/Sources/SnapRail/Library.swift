import AppKit
import SwiftUI
import Vision

final class Library: ObservableObject {
  @Published var shots: [Shot] = []
  @Published var message = "截一张，随手挂一下！"
  @Published var capturing = false
  @Published var recovery: DeleteGroup?
  let root: URL
  var images: URL { root.appendingPathComponent("images", isDirectory: true) }
  var documents: URL { root.appendingPathComponent("documents", isDirectory: true) }
  var manifest: URL { root.appendingPathComponent("library.json") }
  var recoveryURL: URL { root.appendingPathComponent("recovery.json") }
  var railShots: [Shot] {
    shots.filter { $0.onRail && !$0.isPinned }.sorted { $0.favorite && !$1.favorite }
  }
  var changed: (() -> Void)?
  @Published var maintenance = false
  private(set) var readOnlyReason: String?
  var backupManifest: URL { root.appendingPathComponent("library.json.bak") }
  var welcomeOnly: Bool {
    !railShots.isEmpty && railShots.allSatisfy { $0.filename.hasPrefix("welcome-") }
  }

  init(root: URL? = nil) {
    self.root = root ?? URL(fileURLWithPath: SettingsStore.load().dataRoot, isDirectory: true)
    do {
      try FileManager.default.createDirectory(at: images, withIntermediateDirectories: true)
      try FileManager.default.createDirectory(at: documents, withIntermediateDirectories: true)
      if FileManager.default.fileExists(atPath: manifest.path) {
        do { shots = try Self.decodeShots(Data(contentsOf: manifest)) } catch {
          if let backup = try? Self.decodeShots(Data(contentsOf: backupManifest)) {
            shots = backup
            message = "资料库索引损坏，已从上一份备份恢复；原图片保留"
          } else {
            readOnlyReason = "资料库索引无法读取。已停止写入，避免覆盖原数据：\(error.localizedDescription)"
            message = readOnlyReason!
          }
        }
      }
      if let data = try? Data(contentsOf: recoveryURL) {
        recovery = try JSONDecoder().decode(DeleteGroup.self, from: data)
      }
    } catch {
      readOnlyReason = "读取资料库失败：\(error.localizedDescription)"
      message = readOnlyReason!
    }
  }
  static func decodeShots(_ data: Data) throws -> [Shot] {
    let shots = try JSONDecoder().decode([Shot].self, from: data)
    guard Set(shots.map(\.id)).count == shots.count,
      Set(shots.map(\.filename)).count == shots.count
    else {
      throw appError("资料库存在重复的图片标识或文件名")
    }
    return shots
  }
  func ensureWritable() throws {
    if let readOnlyReason { throw appError(readOnlyReason) }
    if maintenance { throw appError("正在迁移资料，暂不能修改资料库") }
  }
  func url(_ shot: Shot) -> URL { images.appendingPathComponent(shot.filename) }
  func originalURL(_ shot: Shot) -> URL {
    documents.appendingPathComponent(shot.id.uuidString + ".png")
  }
  func documentURL(_ shot: Shot) -> URL {
    documents.appendingPathComponent(shot.id.uuidString + ".json")
  }
  func image(_ shot: Shot) -> NSImage? { NSImage(contentsOf: url(shot)) }
  func editable(_ shot: Shot) -> (NSImage, AnnotationDocument)? {
    if let image = NSImage(contentsOf: originalURL(shot)),
      let data = try? Data(contentsOf: documentURL(shot)),
      let document = try? JSONDecoder().decode(AnnotationDocument.self, from: data),
      (try? document.validate()) != nil
    {
      return (image, document)
    }
    return image(shot).map { ($0, AnnotationDocument()) }
  }
  func persist() throws {
    try ensureWritable()
    let data = try JSONEncoder().encode(shots)
    // Preserve a known-good generation. A damaged index never replaces its backup.
    if let previous = try? Data(contentsOf: manifest), (try? Self.decodeShots(previous)) != nil {
      try previous.write(to: backupManifest, options: .atomic)
    }
    try data.write(to: manifest, options: .atomic)
  }
  @discardableResult func transaction(_ action: (inout [Shot]) -> Void) -> Bool {
    let previous = shots
    do {
      try ensureWritable()
      action(&shots)
      try persist()
      changed?()
      return true
    } catch {
      shots = previous
      message = "操作未保存：\(error.localizedDescription)"
      return false
    }
  }
  func save() {
    do { try persist() } catch { message = "保存资料库失败：\(error.localizedDescription)" }
    changed?()
  }
  @discardableResult func add(
    _ image: NSImage, original: NSImage? = nil, document: AnnotationDocument? = nil,
    copyToClipboard: Bool = true
  ) throws -> Shot {
    try ensureWritable()
    try document?.validate()
    let id = UUID()
    let formatter = DateFormatter()
    formatter.dateFormat = "yyyyMMdd-HHmmss-SSS"
    let shot = Shot(
      id: id, filename: "\(formatter.string(from: Date()))-\(id.uuidString).png", created: Date())
    do {
      try image.pngData().write(to: url(shot), options: .atomic)
      if let original, let document {
        try original.pngData().write(to: originalURL(shot), options: .atomic)
        try JSONEncoder().encode(document).write(to: documentURL(shot), options: .atomic)
      }
      shots.insert(shot, at: 0)
      do { try persist() } catch {
        shots.removeAll { $0.id == id }
        throw error
      }
    } catch {
      for file in [url(shot), originalURL(shot), documentURL(shot)] {
        try? FileManager.default.removeItem(at: file)
      }
      throw error
    }
    changed?()
    if copyToClipboard { copy(shot) }
    message = "已保存、复制并挂上"
    return shot
  }
  func update(_ shot: Shot, _ action: (inout Shot) -> Void) {
    transaction { items in
      guard let index = items.firstIndex(where: { $0.id == shot.id }) else { return }
      action(&items[index])
    }
  }
  func setFavorites(_ selected: [Shot], value: Bool) {
    let ids = Set(selected.map(\.id))
    guard !value || shots.filter({ $0.favorite || ids.contains($0.id) }).count <= 3 else {
      message = "收藏最多 3 张，请先取消其他收藏"
      return
    }
    transaction { items in
      for index in items.indices where ids.contains(items[index].id) {
        items[index].favorite = value
      }
    }
  }
  func toggleFavorite(_ shot: Shot) { setFavorites([shot], value: !shot.favorite) }
  func copy(_ shot: Shot) {
    guard let image = image(shot) else {
      message = "图片文件已不存在"
      return
    }
    NSPasteboard.general.clearContents()
    NSPasteboard.general.writeObjects([image])
    message = "已复制完整图片"
  }
  func delete(_ shot: Shot) { recycle([shot]) }
  func recycle(_ selected: [Shot]) {
    guard !selected.isEmpty else { return }
    let before = shots
    let previousRecovery = recovery
    var group = DeleteGroup(shots: selected, files: [])
    do {
      try ensureWritable()
      // Journal successful moves for recovery after an interrupted operation.
      for shot in selected {
        for file in [url(shot), originalURL(shot), documentURL(shot)]
        where FileManager.default.fileExists(atPath: file.path) {
          var trashed: NSURL?
          try FileManager.default.trashItem(at: file, resultingItemURL: &trashed)
          guard let trash = trashed as URL? else { throw appError("无法记录废纸篓路径") }
          group.files.append(DeletedFile(source: file, trash: trash))
          try JSONEncoder().encode(group).write(to: recoveryURL, options: .atomic)
        }
      }
      let ids = Set(selected.map(\.id))
      shots.removeAll { ids.contains($0.id) }
      try persist()
      recovery = group
      changed?()
      message = "已移到系统废纸篓，可撤销上次删除"
    } catch {
      for file in group.files.reversed() {
        try? FileManager.default.moveItem(at: file.trash, to: file.source)
      }
      shots = before
      recovery = previousRecovery
      if let previousRecovery {
        try? JSONEncoder().encode(previousRecovery).write(to: recoveryURL, options: .atomic)
      } else {
        try? FileManager.default.removeItem(at: recoveryURL)
      }
      message = "删除未完成：\(error.localizedDescription)"
    }
  }
  func undoDelete() {
    guard let group = recovery else {
      message = "没有可撤销的删除"
      return
    }
    var restored: [DeletedFile] = []
    do {
      try ensureWritable()
      for file in group.files {
        guard FileManager.default.fileExists(atPath: file.trash.path),
          !FileManager.default.fileExists(atPath: file.source.path)
        else { throw appError("废纸篓文件已清理、移动，或原位置已有同名文件。") }
      }
      for file in group.files {
        try FileManager.default.moveItem(at: file.trash, to: file.source)
        restored.append(file)
      }
      let previous = shots
      for var shot in group.shots where !shots.contains(where: { $0.id == shot.id }) {
        shot.isPinned = false
        shots.append(shot)
      }
      shots.sort { $0.created > $1.created }
      do { try persist() } catch {
        shots = previous
        throw error
      }
      recovery = nil
      try? FileManager.default.removeItem(at: recoveryURL)
      changed?()
      message = "已恢复上次删除的图片与标注"
    } catch {
      for file in restored.reversed() {
        try? FileManager.default.moveItem(at: file.source, to: file.trash)
      }
      message = "恢复未完成：\(error.localizedDescription)"
    }
  }
  func recoverRestoredImages() {
    guard let group = recovery else { return }
    transaction { items in
      for var shot in group.shots
      where !items.contains(where: { $0.id == shot.id })
        && FileManager.default.fileExists(atPath: url(shot).path)
      {
        shot.isPinned = false
        items.append(shot)
      }
    }
  }
  func move(_ source: UUID, before target: UUID) {
    guard source != target,
      let sourceShot = shots.first(where: { $0.id == source }),
      let targetShot = shots.first(where: { $0.id == target }),
      sourceShot.favorite == targetShot.favorite
    else { return }
    transaction { items in
      guard let from = items.firstIndex(where: { $0.id == source }) else { return }
      let item = items.remove(at: from)
      guard let to = items.firstIndex(where: { $0.id == target }) else { return }
      items.insert(item, at: to)
    }
  }
  func export(_ shot: Shot) {
    let panel = NSSavePanel()
    panel.allowedContentTypes = [.png]
    panel.nameFieldStringValue = shot.filename
    panel.begin { response in
      guard response == .OK, let destination = panel.url else { return }
      do { try Data(contentsOf: self.url(shot)).write(to: destination, options: .atomic) } catch {
        self.message = "导出失败：\(error.localizedDescription)"
      }
    }
  }
  func recognize(_ image: NSImage, completion: @escaping (String) -> Void) {
    guard let data = try? image.pngData() else {
      message = "无法读取图片"
      return
    }
    message = "正在本机识别文字…"
    DispatchQueue.global(qos: .userInitiated).async {
      do {
        let request = VNRecognizeTextRequest()
        request.recognitionLevel = .accurate
        request.recognitionLanguages = ["zh-Hans", "en-US"]
        request.usesLanguageCorrection = true
        try VNImageRequestHandler(data: data).perform([request])
        let text = (request.results ?? []).compactMap { $0.topCandidates(1).first?.string }.joined(
          separator: "\n")
        DispatchQueue.main.async {
          self.message = text.isEmpty ? "没有识别到文字" : "文字识别完成"
          completion(text)
        }
      } catch { DispatchQueue.main.async { self.message = "识别失败：\(error.localizedDescription)" } }
    }
  }
  func recognize(_ shot: Shot, completion: @escaping (String) -> Void) {
    if let image = image(shot) { recognize(image, completion: completion) }
  }
  func seedWelcome() {
    let marker = root.appendingPathComponent("welcome-seeded")
    guard readOnlyReason == nil, !maintenance, shots.isEmpty,
      !FileManager.default.fileExists(atPath: marker.path)
    else { return }
    let colors = [
      "D96563", "CE9438", "4B95B0", "9974B3", "559C79", "DA778E", "6E83C1", "D48A53", "A279BD",
      "DD7961",
    ]
    do {
      for (index, letter) in Array("截一张，随手挂一下！").enumerated() {
        let image = NSImage(size: NSSize(width: 196, height: 158))
        image.lockFocus()
        NSColor(hex: "FFF8E7").setFill()
        NSBezierPath(
          roundedRect: NSRect(x: 0, y: 0, width: 196, height: 158), xRadius: 13, yRadius: 13
        ).fill()
        let text = String(letter) as NSString
        let attributes: [NSAttributedString.Key: Any] = [
          .font: NSFont(name: "Yuanti SC", size: 82)
            ?? NSFont.systemFont(ofSize: 82, weight: .light),
          .foregroundColor: NSColor(hex: colors[index]),
        ]
        let size = text.size(withAttributes: attributes)
        text.draw(
          at: NSPoint(x: (196 - size.width) / 2, y: (158 - size.height) / 2),
          withAttributes: attributes)
        image.unlockFocus()
        let id = UUID()
        let shot = Shot(
          id: id, filename: String(format: "welcome-%02d.png", index + 1),
          created: Date().addingTimeInterval(Double(-index)))
        try image.pngData().write(to: url(shot), options: .atomic)
        shots.append(shot)
      }
      try persist()
      try Data("Created once".utf8).write(to: marker, options: .atomic)
    } catch { message = "欢迎挂图初始化失败：\(error.localizedDescription)" }
  }
}

extension Library {
  func applyOrder(_ order: [UUID]) {
    var seen = Set<UUID>()
    let unique = order.filter { seen.insert($0).inserted }
    let ids = Set(unique)
    transaction { items in
      let ordered = unique.compactMap { id in items.first { $0.id == id } }
      items = ordered + items.filter { !ids.contains($0.id) }
    }
  }
}
