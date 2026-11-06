import AppKit

do {
  let options = try LaunchOptions.parse(Array(CommandLine.arguments.dropFirst()))
  if options.help {
    print(
      "SnapRail [--profile /absolute/path] [--diagnostics] [--ui-review] [--demo-capture | --demo-scroll]"
    )
    exit(0)
  }
  let application = NSApplication.shared
  if options.diagnostics {
    printDiagnostics()
    exit(0)
  }
  if options.relaunch { Thread.sleep(forTimeInterval: 0.8) }
  let delegate = AppDelegate(profile: options.profile, options: options)
  application.delegate = delegate
  withExtendedLifetime(delegate) { application.run() }
} catch {
  fputs("SnapRail: \(error.localizedDescription)\n", stderr)
  exit(2)
}
