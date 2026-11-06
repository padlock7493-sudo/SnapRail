import AppKit
import SwiftUI

struct PreviewView: View {
  let image: NSImage
  var app: AppDelegate? = nil
  var shot: Shot? = nil
  @State var fitting = true
  @State var zoom = 1.0
  @Environment(\.dismiss) var dismiss
  var body: some View {
    VStack(spacing: 10) {
      FlowLayout(spacing: 6) {
        Button("适应窗口") {
          fitting = true
          zoom = 1
        }
        Button("实际大小") {
          fitting = false
          zoom = 1
        }
        Button("−") {
          fitting = false
          zoom = max(0.01, zoom / 1.25)
        }
        Button("＋") {
          fitting = false
          zoom = min(8, zoom * 1.25)
        }
        Button("复制") {
          NSPasteboard.general.clearContents()
          NSPasteboard.general.writeObjects([image])
        }
        Button("另存为") { exportImage(image) }
        Button("编辑副本") { if let shot { app?.edit(shot) } else { app?.edit(image) } }
        Button("关闭 Esc") { NSApp.keyWindow?.close() }
      }
      GeometryReader { geometry in
        let pixels = image.pixels
        let scale = NSScreen.main?.backingScaleFactor ?? 1
        let fit = min(
          1,
          min(
            (geometry.size.width - 20) * scale / pixels.width,
            (geometry.size.height - 20) * scale / pixels.height))
        let actualZoom = fitting ? fit : zoom
        ZoomScroll(
          image: image,
          size: NSSize(
            width: pixels.width * actualZoom / scale, height: pixels.height * actualZoom / scale)
        ) { factor in
          zoom = min(8, max(0.01, actualZoom * factor))
          fitting = false
        }
      }
      Text(
        "\(Int(image.pixels.width))×\(Int(image.pixels.height)) 像素 · \(fitting ? "适应窗口":String(Int(zoom*100))+"%") · Control+滚轮缩放，滚轮滚动查看长图"
      ).font(.system(size: 12)).foregroundStyle(Color(hex: "D6E5E0")).frame(
        maxWidth: .infinity, alignment: .leading)
    }.padding(12).frame(minWidth: 480, minHeight: 320).background(Color(hex: "203238")).buttonStyle(
      RailButtonStyle()
    ).onExitCommand { NSApp.keyWindow?.close() }
  }
}
struct ZoomScroll: NSViewRepresentable {
  let image: NSImage
  var size: NSSize
  var zoomAction: (Double) -> Void
  init(image: NSImage, size: NSSize, zoomAction: @escaping (Double) -> Void) {
    self.image = image
    self.size = size
    self.zoomAction = zoomAction
  }
  func makeNSView(context: Context) -> ZoomScrollView {
    let view = ZoomScrollView()
    view.drawsBackground = false
    view.contentView.drawsBackground = false
    view.hasVerticalScroller = true
    view.hasHorizontalScroller = true
    let content = PreviewImageView(frame: NSRect(origin: .zero, size: size))
    content.image = image
    content.imageScaling = .scaleAxesIndependently
    view.documentView = content
    view.zoomAction = zoomAction
    return view
  }
  func updateNSView(_ view: ZoomScrollView, context: Context) {
    view.documentView?.setFrameSize(size)
    view.zoomAction = zoomAction
  }
}
final class PreviewImageView: NSImageView { override var isFlipped: Bool { true } }
final class ZoomScrollView: NSScrollView {
  var zoomAction: ((Double) -> Void)?
  override func scrollWheel(with event: NSEvent) {
    if event.modifierFlags.contains(.control) || event.modifierFlags.contains(.command) {
      zoomAction?(event.scrollingDeltaY > 0 ? 1.1 : 1 / 1.1)
    } else {
      super.scrollWheel(with: event)
    }
  }
}
