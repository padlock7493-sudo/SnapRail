// swift-tools-version: 5.9
import PackageDescription

let package = Package(
  name: "SnapRail",
  platforms: [.macOS(.v13)],
  products: [.executable(name: "SnapRail", targets: ["SnapRail"])],
  targets: [
    .executableTarget(
      name: "SnapRail",
      linkerSettings: [
        .linkedFramework("AppKit"), .linkedFramework("SwiftUI"),
        .linkedFramework("Carbon"), .linkedFramework("Vision"),
        .linkedFramework("ScreenCaptureKit"), .linkedFramework("ServiceManagement"),
      ])
  ]
)
