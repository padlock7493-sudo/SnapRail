import AppKit
import ServiceManagement
import SwiftUI

struct SettingsView: View {
  let app: AppDelegate
  @State var draft: Configuration
  @State private var status = "更换目录会复制并校验图片、标注及状态，保留原目录；保存后重启生效。"
  @State private var busy = false
  @State private var screenCaptureAuthorized = CGPreflightScreenCaptureAccess()
  var body: some View {
    VStack(alignment: .leading, spacing: 18) {
      Text("随手挂设置").font(.system(size: 25, weight: .semibold))
      HStack(alignment: .top, spacing: 28) {
        VStack(alignment: .leading, spacing: 9) {
          Text("快捷键与保存").font(.system(size: 18, weight: .semibold)).padding(.bottom, 5)
          Text("截图快捷键（点击后按下组合键）")
          ShortcutRecorder(app: app, shortcut: $draft.capture).frame(height: 36)
            .accessibilityIdentifier("settings-capture")
          Text("显示 / 收起挂图快捷键").padding(.top, 5)
          ShortcutRecorder(app: app, shortcut: $draft.toggle).frame(height: 36)
            .accessibilityIdentifier("settings-toggle")
          Text("⌃ Control   ⌥ Option   ⇧ Shift   ⌘ Command")
            .font(.system(size: 11)).foregroundStyle(.secondary)
          Text("截图与标注保存目录").padding(.top, 5)
          HStack {
            TextField("", text: $draft.dataRoot).textFieldStyle(.roundedBorder)
            Button("选择…") { chooseFolder() }
          }
          Text("挂图显示屏幕")
          Picker("", selection: $draft.railScreen) {
            Text("跟随鼠标 / 截图所在屏幕").tag("cursor")
            ForEach(NSScreen.screens, id: \.displayID) { screen in
              Text(
                "\(screen.localizedName) · \(Int(screen.frame.width))×\(Int(screen.frame.height))"
              ).tag(String(screen.displayID))
            }
          }.labelsHidden()
          Toggle("登录 macOS 后启动随手挂", isOn: $draft.startAtLogin).padding(.top, 7)
          if !screenCaptureAuthorized {
            Text("当前版本没有屏幕录制权限，截图暂不可用。开发版更新后可能需要在系统设置中重新开启 SnapRail，再退出重开。")
              .font(.system(size: 12)).foregroundStyle(.red).fixedSize(
                horizontal: false, vertical: true)
          }
          Button("屏幕录制权限…") {
            NSWorkspace.shared.open(
              URL(
                string:
                  "x-apple.systempreferences:com.apple.preference.security?Privacy_ScreenCapture")!)
          }.padding(.top, 10)
        }.frame(maxWidth: .infinity, alignment: .topLeading)
        VStack(alignment: .leading, spacing: 14) {
          Text("挂图与交互").font(.system(size: 18, weight: .semibold))
          Toggle("鼠标移到屏幕顶部时展开", isOn: $draft.hoverEnabled)
          Toggle("展开挂图栏、新截图挂上时轻摆", isOn: $draft.animationEnabled)
          adjustment("挂图尺寸", value: $draft.photoWidth, range: 150...240, unit: "px")
          adjustment("挂图栏距工作区顶部", value: $draft.railTopMargin, range: 0...100, unit: "px")
          adjustment("顶部悬停多久展开", value: $draft.hoverDelayMs, range: 100...1500, unit: "ms")
          adjustment("离开多久收起", value: $draft.hideDelayMs, range: 300...5000, unit: "ms")
        }.frame(maxWidth: .infinity, alignment: .topLeading)
      }
      Spacer(minLength: 8)
      Text(status).font(.system(size: 12)).foregroundStyle(.secondary)
      HStack {
        Spacer()
        Button("取消") { app.preferences?.close() }
        Button("保存并重启") { save() }.buttonStyle(RailButtonStyle(active: true))
      }
    }.font(.system(size: 13)).padding(24).frame(minWidth: 810, minHeight: 550)
      .background(Color(hex: "F5FAF8")).buttonStyle(RailButtonStyle()).tint(Color(hex: "319E83"))
      .disabled(busy)
      .onAppear {
        draft.startAtLogin = [.enabled, .requiresApproval].contains(SMAppService.mainApp.status)
        screenCaptureAuthorized = CGPreflightScreenCaptureAccess()
      }
  }
  func adjustment(_ label: String, value: Binding<Double>, range: ClosedRange<Double>, unit: String)
    -> some View
  {
    VStack(alignment: .leading, spacing: 7) {
      Text("\(label) · \(Int(value.wrappedValue)) \(unit)")
      ParitySlider(value: value, range: range).frame(height: 22)
    }
  }
  func chooseFolder() {
    let panel = NSOpenPanel()
    panel.canChooseDirectories = true
    panel.canChooseFiles = false
    panel.canCreateDirectories = true
    panel.message = "选择空目录，原目录会保留。"
    if panel.runModal() == .OK, let url = panel.url { draft.dataRoot = url.path }
  }
  func save() {
    do {
      try draft.validate()
      try app.testShortcuts(draft)
      try app.beginMaintenance()
      busy = true
      status = "正在复制并校验资料…"
      let configuration = draft
      let source = app.library.root
      DispatchQueue.global(qos: .userInitiated).async {
        do {
          try SettingsStore.migrate(
            from: source, to: URL(fileURLWithPath: configuration.dataRoot, isDirectory: true))
          DispatchQueue.main.async {
            do {
              if self.app.profile == nil && configuration.startAtLogin {
                if SMAppService.mainApp.status != .enabled { try SMAppService.mainApp.register() }
              } else if self.app.profile == nil && SMAppService.mainApp.status == .enabled {
                try SMAppService.mainApp.unregister()
              }
              try app.saveConfiguration(configuration)
              try app.relaunch()
            } catch {
              app.endMaintenance()
              status = "未应用：\(error.localizedDescription)"
              busy = false
            }
          }
        } catch {
          DispatchQueue.main.async {
            app.endMaintenance()
            status = "未应用：\(error.localizedDescription)"
            busy = false
          }
        }
      }
    } catch { status = "未应用：\(error.localizedDescription)" }
  }
}
