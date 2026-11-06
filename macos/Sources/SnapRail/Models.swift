import AppKit

struct PinPlacement: Codable, Equatable {
  var x: Double
  var y: Double
  var width: Double
  var height: Double
  var opacity: Double = 1
  var rect: NSRect { NSRect(x: x, y: y, width: width, height: height) }
}
struct Shot: Codable, Identifiable, Equatable {
  let id: UUID
  let filename: String
  let created: Date
  var favorite = false
  var onRail = true
  var isPinned = false
  var placement: PinPlacement?
  enum CodingKeys: String, CodingKey {
    case id, filename, created, favorite, onRail, isPinned, placement
  }
  init(id: UUID, filename: String, created: Date) {
    self.id = id
    self.filename = filename
    self.created = created
  }
  init(from decoder: Decoder) throws {
    let c = try decoder.container(keyedBy: CodingKeys.self)
    id = try c.decode(UUID.self, forKey: .id)
    filename = try c.decode(String.self, forKey: .filename)
    created = try c.decode(Date.self, forKey: .created)
    favorite = try c.decodeIfPresent(Bool.self, forKey: .favorite) ?? false
    onRail = try c.decodeIfPresent(Bool.self, forKey: .onRail) ?? true
    isPinned = try c.decodeIfPresent(Bool.self, forKey: .isPinned) ?? false
    placement = try c.decodeIfPresent(PinPlacement.self, forKey: .placement)
    if let p = placement,
      ![p.x, p.y, p.width, p.height, p.opacity].allSatisfy(\.isFinite)
        || p.width <= 0 || p.height <= 0 || !(0.2...1).contains(p.opacity)
    {
      placement = nil
    }
    guard !filename.isEmpty, !filename.contains("\0"), filename.lowercased().hasSuffix(".png"),
      filename == URL(fileURLWithPath: filename).lastPathComponent
    else {
      throw appError("资料库包含无效图片路径")
    }
  }
}
struct DeletedFile: Codable {
  let source: URL
  let trash: URL
}
struct DeleteGroup: Codable {
  var shots: [Shot]
  var files: [DeletedFile]
}

extension DeleteGroup {
  func rebased(from sourceRoot: URL, to destinationRoot: URL) throws -> DeleteGroup {
    let source = sourceRoot.standardizedFileURL.resolvingSymlinksInPath()
    let destination = destinationRoot.standardizedFileURL.resolvingSymlinksInPath()
    let moved = try files.map { file -> DeletedFile in
      let path = file.source.standardizedFileURL.resolvingSymlinksInPath()
      guard path.path.hasPrefix(source.path + "/") else { throw appError("删除恢复记录包含资料目录以外的路径") }
      let relative = path.pathComponents.dropFirst(source.pathComponents.count).joined(
        separator: "/")
      return DeletedFile(source: destination.appendingPathComponent(relative), trash: file.trash)
    }
    return DeleteGroup(shots: shots, files: moved)
  }
}
