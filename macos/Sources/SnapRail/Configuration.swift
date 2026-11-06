import AppKit
import Carbon

struct Shortcut: Codable, Equatable {
  var key: UInt32
  var modifiers: UInt32 = UInt32(controlKey | optionKey)
  var label: String
  var description: String {
    ((modifiers & UInt32(controlKey)) != 0 ? "Control+" : "")
      + ((modifiers & UInt32(optionKey)) != 0 ? "Option+" : "")
      + ((modifiers & UInt32(shiftKey)) != 0 ? "Shift+" : "")
      + ((modifiers & UInt32(cmdKey)) != 0 ? "Command+" : "")
      + (key == UInt32(kVK_Return) ? "Return" : label)
  }
  func validate() throws {
    let functions: Set<UInt32> = [
      122, 120, 99, 118, 96, 97, 98, 100, 101, 109, 103, 111, 105, 107, 113, 106, 64, 79, 80, 90,
    ]
    let allowed = UInt32(controlKey | optionKey | shiftKey | cmdKey)
    let modifierKeys: Set<UInt32> = [54, 55, 56, 57, 58, 59, 60, 61, 62, 63]
    guard key <= 126, modifiers & ~allowed == 0, !modifierKeys.contains(key),
      modifiers != 0 || functions.contains(key),
      !(modifiers == UInt32(cmdKey) && [UInt32(kVK_ANSI_Q), UInt32(kVK_ANSI_W)].contains(key))
    else {
      throw appError("请使用修饰键与普通按键组合；Command+Q / Command+W 保留给系统。")
    }
  }
}
func appError(_ message: String) -> NSError {
  NSError(domain: "SnapRail", code: 1, userInfo: [NSLocalizedDescriptionKey: message])
}

struct Configuration: Codable, Equatable {
  var capture = Shortcut(key: UInt32(kVK_ANSI_A), label: "A")
  var toggle = Shortcut(key: UInt32(kVK_ANSI_T), label: "T")
  var dataRoot = SettingsStore.defaultDataRoot().path
  var railScreen = "cursor"
  var hoverEnabled = true
  var animationEnabled = true
  var startAtLogin = false
  var photoWidth = 150.0
  var railTopMargin = 10.0
  var hoverDelayMs = 250.0
  var hideDelayMs = 700.0
  func validate() throws {
    try capture.validate()
    try toggle.validate()
    guard capture.key != toggle.key || capture.modifiers != toggle.modifiers else {
      throw appError("截图与显示挂图不能使用相同快捷键。")
    }
    guard (150...240).contains(photoWidth), (0...100).contains(railTopMargin),
      (100...1500).contains(hoverDelayMs), (300...5000).contains(hideDelayMs)
    else { throw appError("挂图尺寸或延迟超出允许范围。") }
    guard dataRoot.hasPrefix("/"), !dataRoot.contains("\0") else { throw appError("请选择有效的绝对目录。") }
  }
}
enum SettingsStore {
  static let base = FileManager.default.urls(
    for: .applicationSupportDirectory, in: .userDomainMask)[0].appendingPathComponent(
      "SnapRail", isDirectory: true)
  // Keep user settings stable when the app is moved or updated. Image data
  // defaults beside an installed app, outside its signed bundle.
  static func defaultDataRoot(
    appURL: URL = Bundle.main.bundleURL,
    isWritable: (String) -> Bool = FileManager.default.isWritableFile(atPath:)
  ) -> URL {
    let parent = appURL.deletingLastPathComponent().standardizedFileURL
    guard appURL.pathExtension.lowercased() == "app",
      !appURL.path.contains("/AppTranslocation/"), isWritable(parent.path)
    else { return base }
    return parent.appendingPathComponent("SnapRail-data", isDirectory: true)
  }
  static var file: URL { base.appendingPathComponent("settings.json") }
  static func read(_ file: URL) throws -> Configuration {
    let configuration = try JSONDecoder().decode(Configuration.self, from: Data(contentsOf: file))
    try configuration.validate()
    return configuration
  }
  static func load() -> Configuration { (try? read(file)) ?? Configuration() }
  static func save(_ configuration: Configuration) throws {
    try configuration.validate()
    try FileManager.default.createDirectory(at: base, withIntermediateDirectories: true)
    try JSONEncoder().encode(configuration).write(to: file, options: .atomic)
  }
}
