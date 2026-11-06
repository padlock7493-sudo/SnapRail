import AppKit
import SwiftUI

struct RailButtonStyle: ButtonStyle {
  var active = false
  func makeBody(configuration: ButtonStyleConfiguration) -> some View {
    configuration.label.font(.system(size: 12)).padding(.horizontal, 11).padding(.vertical, 7)
      .foregroundStyle(Color(hex: "253A40"))
      .background(
        Color(hex: active ? "CDE8DE" : "F3F7F6").opacity(configuration.isPressed ? 0.65 : 1),
        in: RoundedRectangle(cornerRadius: 8)
      )
      .overlay(
        RoundedRectangle(cornerRadius: 8).stroke(
          Color(hex: active ? "7CB4A1" : "D0DEDA"), lineWidth: 1))
  }
}
extension Color {
  init(hex: String) { self.init(nsColor: NSColor(hex: hex)) }
}
extension NSColor {
  convenience init(hex: String) {
    let value = UInt64(hex.trimmingCharacters(in: CharacterSet(charactersIn: "#")), radix: 16) ?? 0
    self.init(
      srgbRed: CGFloat((value >> 16) & 255) / 255, green: CGFloat((value >> 8) & 255) / 255,
      blue: CGFloat(value & 255) / 255, alpha: 1)
  }
  var hex: String {
    let rgb = usingColorSpace(.sRGB) ?? .systemRed
    return String(
      format: "%02X%02X%02X", Int((rgb.redComponent * 255).rounded()),
      Int((rgb.greenComponent * 255).rounded()), Int((rgb.blueComponent * 255).rounded()))
  }
}

extension NSScreen {
  var displayID: CGDirectDisplayID {
    (deviceDescription[NSDeviceDescriptionKey("NSScreenNumber")] as? NSNumber)?.uint32Value ?? 0
  }
}
