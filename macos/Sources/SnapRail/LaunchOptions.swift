import Foundation

struct LaunchOptions {
  var profile: URL?
  var diagnostics = false
  var help = false
  var relaunch = false
  var keepRailOpen = false
  var demoCapture = false
  var demoScroll = false

  static func parse(_ arguments: [String]) throws -> LaunchOptions {
    var result = LaunchOptions()
    var index = 0
    while index < arguments.count {
      switch arguments[index] {
      case "--profile":
        index += 1
        guard index < arguments.count, arguments[index].hasPrefix("/"), result.profile == nil else {
          throw appError("--profile 后必须提供一个绝对目录，且不能重复指定")
        }
        result.profile =
          URL(fileURLWithPath: arguments[index], isDirectory: true).standardizedFileURL
      case "--diagnostics": result.diagnostics = true
      case "--help", "-h": result.help = true
      case "--relaunch": result.relaunch = true
      case "--ui-review": result.keepRailOpen = true
      case "--demo-capture": result.demoCapture = true
      case "--demo-scroll": result.demoScroll = true
      default:
        guard arguments[index].hasPrefix("-psn_") else {
          throw appError("未知参数：\(arguments[index])")
        }
      }
      index += 1
    }
    guard !(result.demoCapture && result.demoScroll) else { throw appError("两种演示测试模式不能同时启用") }
    // Test modes must never silently use the user's daily library.
    guard (!result.demoCapture && !result.demoScroll) || result.profile != nil else {
      throw appError("演示测试必须使用 --profile 指定独立资料目录")
    }
    return result
  }
}
