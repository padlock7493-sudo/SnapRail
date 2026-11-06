import AppKit
import ApplicationServices
import SwiftUI

final class ScrollSession: NSObject, ObservableObject, NSWindowDelegate {
  @Published var status = "准备首帧…"
  @Published var running = false
  @Published var sampling = false
  var stitcher: ScrollStitcher?
  let region: NSRect
  var window: NSWindow?
  var finished: ((NSImage) -> Void)?
  var cancelled: (() -> Void)?
  var task: Task<Void, Never>?
  var closing = false
  let captureFrame: (@MainActor (NSRect) async throws -> NSImage)
  init(
    region: NSRect,
    captureFrame: @escaping @MainActor (NSRect) async throws -> NSImage = ScreenGrabber.region
  ) {
    self.region = region
    self.captureFrame = captureFrame
    super.init()
  }
  func show() {
    let panel = FloatingPanel(
      contentRect: NSRect(x: 0, y: 0, width: 610, height: 245),
      styleMask: [.titled, .closable, .resizable, .nonactivatingPanel], backing: .buffered,
      defer: false)
    panel.delegate = self
    panel.title = "随手挂 · 滚动长截图"
    panel.isReleasedWhenClosed = false
    panel.level = .floating
    panel.minSize = NSSize(width: 440, height: 240)
    panel.contentView = NSHostingView(rootView: ScrollCaptureView(session: self))
    let work =
      (NSScreen.screens.first(where: { $0.frame.contains(NSEvent.mouseLocation) }) ?? NSScreen.main!)
      .visibleFrame
    panel.setFrameOrigin(
      NSPoint(x: work.midX - panel.frame.width / 2, y: work.maxY - panel.frame.height - 20))
    window = panel
    panel.orderFrontRegardless()
    task = Task { @MainActor in _ = await sample() }
  }
  @MainActor func sample() async -> StitchStatus {
    guard !sampling, !closing else { return .noMatch }
    sampling = true
    window?.orderOut(nil)
    defer {
      sampling = false
      if !closing {
        if running { window?.orderFrontRegardless() } else { window?.makeKeyAndOrderFront(nil) }
      }
    }
    do {
      try await Task.sleep(nanoseconds: 160_000_000)
      let image = try await captureFrame(region)
      guard !closing else { return .noMatch }
      let result: StitchStatus
      if let stitcher {
        result = try stitcher.append(image)
      } else {
        stitcher = try ScrollStitcher(image)
        result = .added
      }
      let detail: [StitchStatus: String] = [
        .added: "已追加", .unchanged: "画面未变化 / 已到底", .noMatch: "无法可靠对齐，已保留之前内容；请少滚动一些并避开固定栏",
        .limit: "已达到长度或内存上限，请预览保存",
      ]
      status =
        "\(stitcher!.frames) 帧 · \(stitcher!.width)×\(stitcher!.height) 像素 · \(detail[result]!)"
      return result
    } catch is CancellationError {
      status = "已停止"
      return .unchanged
    } catch {
      status = "长截图未完成：\(error.localizedDescription)"
      return .noMatch
    }
  }
  func append() { task = Task { @MainActor in _ = await sample() } }
  func start() {
    guard stitcher != nil, !running, !sampling else { return }
    guard AXIsProcessTrusted() else {
      status = "自动滚动需要辅助功能权限；可先手动滚动，再点击“追加当前画面”。"
      let alert = NSAlert()
      alert.messageText = "允许自动滚动？"
      alert.informativeText = "请在系统设置 → 隐私与安全性 → 辅助功能中允许 SnapRail 控制滚动。手动追加不需要此权限。"
      alert.addButton(withTitle: "打开设置")
      alert.addButton(withTitle: "使用手动追加")
      if alert.runModal() == .alertFirstButtonReturn {
        NSWorkspace.shared.open(
          URL(
            string: "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility")!
        )
      }
      return
    }
    running = true
    task = Task { @MainActor in
      defer {
        running = false
        status += " · 已停止"
      }
      let center = scrollTarget()
      let info =
        CGWindowListCopyWindowInfo([.optionOnScreenOnly, .excludeDesktopElements], kCGNullWindowID)
        as? [[String: Any]] ?? []
      for item in info {
        guard let dict = item[kCGWindowBounds as String] as? [String: Any],
          let box = CGRect(dictionaryRepresentation: dict as CFDictionary), box.contains(center),
          let pid = item[kCGWindowOwnerPID as String] as? Int32,
          pid != ProcessInfo.processInfo.processIdentifier,
          (item[kCGWindowLayer as String] as? Int ?? 0) == 0
        else { continue }
        NSRunningApplication(processIdentifier: pid)?.activate(options: .activateIgnoringOtherApps)
        break
      }
      while !Task.isCancelled && !closing {
        let event = CGEvent(
          scrollWheelEvent2Source: nil, units: .pixel, wheelCount: 1,
          wheel1: -Int32(region.height * 0.55), wheel2: 0, wheel3: 0)
        event?.location = center
        event?.post(tap: .cghidEventTap)
        do { try await Task.sleep(nanoseconds: 650_000_000) } catch { break }
        if Task.isCancelled { break }
        if await sample() != .added { break }
      }
    }
  }
  func scrollTarget() -> CGPoint {
    let top = NSScreen.screens.first?.frame.maxY ?? 0
    let panel =
      window.map {
        NSRect(
          x: $0.frame.minX, y: top - $0.frame.maxY, width: $0.frame.width, height: $0.frame.height)
      } ?? .zero
    for point in [
      CGPoint(x: region.midX, y: region.midY), CGPoint(x: region.midX, y: region.maxY - 20),
      CGPoint(x: region.minX + 20, y: region.midY), CGPoint(x: region.maxX - 20, y: region.midY),
    ] {
      if !panel.contains(point) { return point }
    }
    return CGPoint(x: region.midX, y: region.midY)
  }
  func windowWillClose(_ notification: Notification) {
    if !closing {
      closing = true
      stop()
      cancelled?()
    }
  }
  func stop() {
    task?.cancel()
    running = false
  }
  func finish() {
    guard !sampling, let image = stitcher?.render() else { return }
    closing = true
    stop()
    window?.close()
    finished?(image)
  }
  func cancel() {
    closing = true
    stop()
    window?.close()
    cancelled?()
  }
}
struct ScrollCaptureView: View {
  @ObservedObject var session: ScrollSession
  var body: some View {
    VStack(alignment: .leading, spacing: 12) {
      Text("滚动长截图").font(.system(size: 21, weight: .semibold))
      Text("框选滚动正文并避开固定栏。自动向下滚动；也可手动滚动后追加。").font(.system(size: 13))
      Text(session.status).font(.system(size: 12)).fixedSize(horizontal: false, vertical: true)
        .frame(maxWidth: .infinity, alignment: .leading)
      ViewThatFits(in: .horizontal) {
        HStack(spacing: 6) { controls }
        VStack(alignment: .leading, spacing: 6) { controls }
      }
    }.padding(18).frame(minWidth: 400, minHeight: 180, alignment: .topLeading).background(
      Color(hex: "F5FAF8")
    ).buttonStyle(RailButtonStyle())
      .onExitCommand { session.running ? session.stop() : session.cancel() }
  }
  @ViewBuilder var controls: some View {
    Button("自动向下滚动", action: session.start).disabled(
      session.running || session.sampling || session.stitcher == nil)
    Button("追加当前画面", action: session.append).disabled(session.running || session.sampling)
    Button("停止", action: session.stop).disabled(!session.running)
    Button("预览并编辑", action: session.finish).disabled(
      session.running || session.sampling || session.stitcher == nil)
    Button("取消", action: session.cancel)
  }
}
