import AppKit
import SwiftUI

struct OCRView: View {
  @State var text: String
  var body: some View {
    VStack(alignment: .leading, spacing: 12) {
      Text("识别文字").font(.system(size: 22, weight: .semibold))
      Text("使用本机文字识别。可修改结果后复制，小字与标点请校对。").font(.system(size: 12)).foregroundStyle(.secondary)
      TextEditor(text: $text).font(.system(size: 15)).border(Color(hex: "D0DEDA"))
      HStack {
        Text("\(text.count) 个字符").font(.caption).foregroundStyle(.secondary)
        Spacer()
        Button("复制文字") {
          NSPasteboard.general.clearContents()
          NSPasteboard.general.setString(text, forType: .string)
        }
        Button("关闭") { NSApp.keyWindow?.close() }
      }
    }.padding(20).frame(minWidth: 400, minHeight: 260).background(Color(hex: "F5FAF8")).buttonStyle(
      RailButtonStyle()
    ).onExitCommand { NSApp.keyWindow?.close() }
  }
}
