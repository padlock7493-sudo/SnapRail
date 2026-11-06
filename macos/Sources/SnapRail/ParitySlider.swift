import AppKit
import SwiftUI

final class ParitySliderCell: NSSliderCell {
  override func drawBar(inside rect: NSRect, flipped: Bool) {
    let bar = NSRect(x: rect.minX + 8, y: rect.midY - 3, width: max(1, rect.width - 16), height: 6)
    NSColor(hex: "DCE8E1").setFill()
    NSBezierPath(roundedRect: bar, xRadius: 3, yRadius: 3).fill()
    let progress = (doubleValue - minValue) / max(1, maxValue - minValue)
    NSColor(hex: "42AE90").setFill()
    NSBezierPath(
      roundedRect: NSRect(
        x: bar.minX, y: bar.minY, width: bar.width * progress, height: bar.height), xRadius: 3,
      yRadius: 3
    ).fill()
  }
  override func drawKnob(_ knobRect: NSRect) {
    let rect = NSRect(x: knobRect.midX - 8, y: knobRect.midY - 8, width: 16, height: 16)
    NSColor.white.setFill()
    NSColor(hex: "269A7D").setStroke()
    let path = NSBezierPath(ovalIn: rect)
    path.lineWidth = 2
    path.fill()
    path.stroke()
  }
}
struct ParitySlider: NSViewRepresentable {
  @Binding var value: Double
  let range: ClosedRange<Double>
  final class Coordinator: NSObject {
    var parent: ParitySlider
    init(_ parent: ParitySlider) { self.parent = parent }
    @objc func changed(_ sender: NSSlider) { parent.value = sender.doubleValue.rounded() }
  }
  func makeCoordinator() -> Coordinator { Coordinator(self) }
  func makeNSView(context: Context) -> NSSlider {
    let slider = NSSlider()
    slider.cell = ParitySliderCell()
    slider.minValue = range.lowerBound
    slider.maxValue = range.upperBound
    slider.doubleValue = value
    slider.isContinuous = true
    slider.numberOfTickMarks = 0
    slider.target = context.coordinator
    slider.action = #selector(Coordinator.changed(_:))
    return slider
  }
  func updateNSView(_ slider: NSSlider, context: Context) {
    context.coordinator.parent = self
    slider.doubleValue = value
  }
}
