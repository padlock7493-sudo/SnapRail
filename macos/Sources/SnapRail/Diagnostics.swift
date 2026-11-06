import AppKit
import ApplicationServices

/// Read-only runtime information for support and release verification. No prompt or permission change.
func printDiagnostics() {
  let info: [String: Any] = [
    "version": Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String
      ?? "development",
    "os": ProcessInfo.processInfo.operatingSystemVersionString,
    "screenCaptureAuthorized": CGPreflightScreenCaptureAccess(),
    "accessibilityAuthorized": AXIsProcessTrusted(),
    "displays": NSScreen.screens.map { screen in
      [
        "id": screen.displayID, "width": screen.frame.width, "height": screen.frame.height,
        "scale": screen.backingScaleFactor,
      ] as [String: Any]
    },
  ]
  if let data = try? JSONSerialization.data(
    withJSONObject: info, options: [.prettyPrinted, .sortedKeys]),
    let text = String(data: data, encoding: .utf8)
  {
    print(text)
  }
}
