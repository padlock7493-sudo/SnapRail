import AppKit

_ = NSApplication.shared
var failures = 0
var total = 0
func check(_ name: String, _ test: () throws -> Void) {
  total += 1
  do {
    try test()
    print("PASS \(name)")
  } catch {
    failures += 1
    fputs("FAIL \(name): \(error.localizedDescription)\n", stderr)
  }
}
check("Core workflow / rendering / storage / OCR / stitching", runLegacyRegression)
runRegressionSuite()
print("RESULT: \(total - failures)/\(total) passed; \(failures) failed")
exit(failures == 0 ? 0 : 1)
