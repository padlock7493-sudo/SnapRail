import CryptoKit
import Foundation

extension SettingsStore {
  /// Copy into a staging directory; publish only after all file hashes match.
  /// A failed migration leaves the source and the chosen destination intact.
  static func migrate(from source: URL, to destination: URL) throws {
    let source = source.standardizedFileURL.resolvingSymlinksInPath()
    let destination = destination.standardizedFileURL.resolvingSymlinksInPath()
    guard source != destination else { return }
    guard !destination.path.hasPrefix(source.path + "/"),
      !source.path.hasPrefix(destination.path + "/")
    else {
      throw appError("新目录不能包含当前目录，也不能位于当前目录内部。")
    }
    let fm = FileManager.default
    guard (try? source.resourceValues(forKeys: [.isDirectoryKey]).isDirectory) == true else {
      throw appError("原资料目录不存在")
    }
    if fm.fileExists(atPath: destination.path),
      !(try fm.contentsOfDirectory(atPath: destination.path)).isEmpty
    {
      throw appError("请选择空目录，避免覆盖已有文件。")
    }
    let parent = destination.deletingLastPathComponent()
    try fm.createDirectory(at: parent, withIntermediateDirectories: true)
    let staging = parent.appendingPathComponent(
      ".SnapRail-migration-\(UUID().uuidString)", isDirectory: true)
    try fm.createDirectory(at: staging, withIntermediateDirectories: true)
    defer { try? fm.removeItem(at: staging) }
    for name in [
      "images", "documents", "library.json", "library.json.bak", "recovery.json", "welcome-seeded",
    ] {
      let input = source.appendingPathComponent(name)
      let output = staging.appendingPathComponent(name)
      guard fm.fileExists(atPath: input.path) else { continue }
      var files = [input]
      if let enumerator = fm.enumerator(
        at: input, includingPropertiesForKeys: [.isSymbolicLinkKey], options: [])
      {
        files += enumerator.compactMap { $0 as? URL }
      }
      for file in files {
        if try file.resourceValues(forKeys: [.isSymbolicLinkKey]).isSymbolicLink == true {
          throw appError("资料目录包含符号链接，请先移除链接后再迁移")
        }
      }
      try fm.copyItem(at: input, to: output)
      for file in files
      where (try file.resourceValues(forKeys: [.isRegularFileKey]).isRegularFile) == true {
        let canonical = file.standardizedFileURL.resolvingSymlinksInPath()
        let relative = canonical.pathComponents.dropFirst(source.pathComponents.count).joined(
          separator: "/")
        guard try digest(file) == digest(staging.appendingPathComponent(relative)) else {
          throw appError("目录复制校验失败，原数据仍保留。")
        }
      }
    }
    let journal = staging.appendingPathComponent("recovery.json")
    if fm.fileExists(atPath: journal.path) {
      let group = try JSONDecoder().decode(DeleteGroup.self, from: Data(contentsOf: journal))
      try JSONEncoder().encode(group.rebased(from: source, to: destination)).write(
        to: journal, options: .atomic)
    }
    // Check again before committing; never replace a directory that gained files.
    if fm.fileExists(atPath: destination.path) {
      guard try fm.contentsOfDirectory(atPath: destination.path).isEmpty else {
        throw appError("目标目录不再为空，已停止迁移")
      }
      try fm.removeItem(at: destination)
    }
    try fm.moveItem(at: staging, to: destination)
  }

  private static func digest(_ file: URL) throws -> SHA256.Digest {
    let handle = try FileHandle(forReadingFrom: file)
    defer { try? handle.close() }
    var hash = SHA256()
    while let chunk = try handle.read(upToCount: 1_048_576), !chunk.isEmpty {
      hash.update(data: chunk)
    }
    return hash.finalize()
  }
}
