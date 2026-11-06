import AppKit
import SwiftUI

struct HistoryView: View {
  @ObservedObject var library: Library
  let app: AppDelegate
  @State var search = ""
  @State var filter = "全部"
  @State var page = 0
  @State var selected: Set<UUID> = []
  @State var anchor: UUID?
  var filtered: [Shot] {
    library.shots.filter { shot in
      (filter == "全部" || filter == "收藏" && shot.favorite
        || filter == "挂图" && shot.onRail && !shot.isPinned
        || filter == "待挂" && !shot.onRail && !shot.isPinned)
        && (search.isEmpty || shot.filename.localizedCaseInsensitiveContains(search)
          || date(shot).contains(search))
    }
  }
  var current: [Shot] { Array(filtered.dropFirst(page * 24).prefix(24)) }
  var selection: [Shot] { library.shots.filter { selected.contains($0.id) } }
  func detail(_ shot: Shot) -> String {
    let prefix = shot.favorite ? "收藏 · " : ""
    let state = shot.isPinned ? "桌面贴图" : (shot.onRail ? "本地截图" : "已取下")
    return prefix + state + " · " + shot.filename
  }
  func date(_ shot: Shot) -> String {
    let formatter = DateFormatter()
    formatter.dateFormat = "yyyy-MM-dd HH:mm:ss"
    return formatter.string(from: shot.created)
  }
  var body: some View {
    VStack(spacing: 12) {
      ViewThatFits(in: .horizontal) {
        HStack(spacing: 12) { header }
        VStack(alignment: .leading, spacing: 8) { header }
      }.frame(maxWidth: .infinity, alignment: .leading)
      FlowLayout(spacing: 6) {
        Button("重新挂图") {
          act {
            for shot in selection {
              library.update(shot) {
                $0.onRail = true
                $0.isPinned = false
              }
              if let pin = app.pins.removeValue(forKey: shot.id) { pin.closeWithoutReturning() }
            }
            app.showRail()
          }
        }
        Button("复制") { single { library.copy($0) } }
        Button("另存为") { single { library.export($0) } }
        Button("贴到桌面") { act { for shot in selection { app.pin(shot) } } }
        Button("固定/取消收藏") {
          act { library.setFavorites(selection, value: !selection.allSatisfy(\.favorite)) }
        }
        Button("从绳子取下") {
          act { for shot in selection { library.update(shot) { $0.onRail = false } } }
        }
        Button("编辑副本") { single { app.edit($0) } }
        Button("识别文字") { single { app.ocr($0) } }
        Button("删除到回收站") { act { app.recycle(selection) } }
        Button("撤销上次删除", action: library.undoDelete)
      }
      ScrollView {
        LazyVStack(spacing: 0) {
          ForEach(current) { shot in
            HStack(spacing: 16) {
              Group {
                if let image = library.image(shot) {
                  Image(nsImage: image).resizable().scaledToFit()
                } else {
                  Image(systemName: "photo").foregroundStyle(.secondary)
                }
              }.frame(width: 130, height: 80)
              VStack(alignment: .leading, spacing: 8) {
                Text(date(shot)).font(.system(size: 16))
                Text(detail(shot)).font(.system(size: 13)).lineLimit(1).truncationMode(.middle)
              }.frame(maxWidth: .infinity, alignment: .leading)
            }.padding(.horizontal, 12).padding(.vertical, 10).background(
              selected.contains(shot.id) ? Color(hex: "D9ECE5") : Color.white
            ).contentShape(Rectangle())
              .overlay(
                RowInteraction(
                  select: { flags in select(shot, flags: flags) }, preview: { app.preview(shot) },
                  menu: { app.imageMenu(shot) })
              )
              .accessibilityElement(children: .combine)
              .accessibilityAddTraits(.isButton)
              .accessibilityAction { select(shot) }
              .accessibilityValue(selected.contains(shot.id) ? "已选择" : "未选择")
              .accessibilityLabel("\(date(shot)) \(shot.favorite ? "收藏":"") \(shot.filename)")
          }
        }.frame(maxWidth: .infinity)
      }.background(.white).overlay {
        if filtered.isEmpty { Text("暂无符合条件的截图").foregroundStyle(.secondary) }
      }
      HStack(spacing: 16) {
        Spacer()
        Button("上一页") {
          page -= 1
          selected = []
        }.disabled(page == 0)
        Text("第 \(page+1)/\(max(1,(filtered.count+23)/24)) 页 · 共 \(filtered.count) 张").font(
          .system(size: 13))
        Button("下一页") {
          page += 1
          selected = []
        }.disabled((page + 1) * 24 >= filtered.count)
        Spacer()
      }
      Text(library.message).font(.system(size: 12)).foregroundStyle(Color(hex: "53686B")).frame(
        maxWidth: .infinity, alignment: .leading)
    }.padding(14).frame(minWidth: 650, minHeight: 480).background(Color(hex: "F5FAF8"))
      .foregroundStyle(Color(hex: "253A40")).buttonStyle(RailButtonStyle())
      .onChange(of: search) { _ in
        page = 0
        selected = []
      }.onChange(of: filter) { _ in
        page = 0
        selected = []
      }
      .onChange(of: library.shots.count) { _ in
        page = min(page, max(0, (filtered.count - 1) / 24))
        selected.formIntersection(Set(library.shots.map(\.id)))
      }
      .onDeleteCommand { app.recycle(selection) }
  }
  @ViewBuilder var header: some View {
    Text("截图历史").font(.system(size: 22, weight: .semibold)).fixedSize()
    TextField("", text: $search).textFieldStyle(.roundedBorder).frame(minWidth: 180, maxWidth: 260)
      .help("搜索日期、时间或文件名")
    HStack(spacing: 0) {
      ForEach(["全部", "收藏", "挂图", "待挂"], id: \.self) { title in
        Button(title) { filter = title }.buttonStyle(FilterButtonStyle(selected: filter == title))
      }
    }.padding(3).background(Color(hex: "EAF2EE"), in: RoundedRectangle(cornerRadius: 10)).overlay(
      RoundedRectangle(cornerRadius: 10).stroke(Color(hex: "D0DEDA")))
    Button("刷新") {
      library.recoverRestoredImages()
      selected = []
    }
    Button("文件夹", action: app.openFolder)
  }
  func select(_ shot: Shot, flags: NSEvent.ModifierFlags = NSEvent.modifierFlags) {
    if flags.contains(.shift), let anchor,
      let from = current.firstIndex(where: { $0.id == anchor }),
      let to = current.firstIndex(where: { $0.id == shot.id })
    {
      selected.formUnion(current[min(from, to)...max(from, to)].map(\.id))
    } else if flags.contains(.command) || flags.contains(.control) {
      if selected.contains(shot.id) { selected.remove(shot.id) } else { selected.insert(shot.id) }
      anchor = shot.id
    } else {
      selected = [shot.id]
      anchor = shot.id
    }
  }
  func act(_ action: () -> Void) {
    guard !selection.isEmpty else {
      library.message = "请先选择图片"
      return
    }
    action()
  }
  func single(_ action: (Shot) -> Void) {
    guard selection.count == 1 else {
      library.message = "请选择一张图片"
      return
    }
    action(selection[0])
  }
}
struct FilterButtonStyle: ButtonStyle {
  let selected: Bool
  func makeBody(configuration: ButtonStyleConfiguration) -> some View {
    configuration.label.font(.system(size: 13)).padding(.horizontal, 14).padding(.vertical, 9)
      .background(
        selected ? Color(hex: "126B64") : Color.clear, in: RoundedRectangle(cornerRadius: 8)
      ).foregroundStyle(selected ? .white : Color(hex: "53686B"))
  }
}
