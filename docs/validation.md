# Validation

Validated on Windows 11 build 26200, x64, .NET SDK 10.0.100, on November 7, 2026.

## Automated checks

The core suite has 14 passing tests covering text normalization, stable confirmation, empty/error results, deduplication, pixel comparison, interval validation, cancellation before writing, retaining confirmed text across pause/resume, and a bounded latest-frame pipeline.

The Windows integration suite exercises an isolated application-owned fixture. It checks the fixture process identity before any write, and does not write to existing user applications.

Version 0.1.1 extends the suite to 18 passing checks: writable `ComboBox` and `Custom` accessibility roles are accepted, while pointing at a plain text label is rejected with a diagnostic instead of selecting a nearby input. The hotkey fixture uses Ctrl+Alt+Shift+F12 so it can run alongside the production utility, whose stop shortcut remains Ctrl+Alt+Shift+S. A real F8 selection through the original desktop interface was also verified against the native test field.

| Check | Result |
| --- | --- |
| Inspect target without changing its contents | Passed |
| Reject read-only and password fields | Passed |
| Identify a native edit control and preserve its initial value during inspection | Passed |
| Direct WM_SETTEXT preserves the foreground window | Passed |
| Background Unicode and multiline update preserves foreground and keyboard focus | Passed |
| Capture visible source pixels and recognize English text locally | Passed |
| Optional grayscale, median filter, contrast, and scaling | Passed |
| Full capture → two OCR confirmations → background update | Passed |
| Skip repeated text and synchronize a changed source | Passed |
| Native readback and direct MSAA value writing | Passed |
| Detect a provider that activates its window and invalidate its target | Passed |
| Register global stop and target-selection hotkeys | Passed |
| Isolate and terminate a hung automation worker | Passed; approximately 3 seconds |
| Reject a lost target session without choosing a replacement | Passed |

One measured first update took 214 ms at a 200 ms capture interval. This is a local fixture measurement, not a latency guarantee.

## Compatibility finding

On this Windows build, UI Automation ValuePattern writes to the WPF and WinForms fixture could activate their application window. A supported pattern alone therefore does not establish strict background compatibility. Standard native edit controls use direct, bounded WM_SETTEXT instead; this path passed the focus checks. For other providers, the first real update can reveal this behavior. The application detects activation, invalidates the target, and stops; it does not restore focus or continue writing.

The portable Windows OCR engine worked on this machine with the installed en-US recognizer. Other Windows installations need an installed OCR language and an available Windows.Media.Ocr runtime. Microsoft documents package-identity restrictions for some desktop OCR scenarios; the portable build has not been validated across all Windows 10/11 versions.

## Visual checks and remaining manual coverage

The real WPF layout was rendered and inspected in its default, settings-expanded, and minimum-size states. The smaller layout scrolls to keep controls accessible. No user screen images are included in these renders. A separate smoke check verifies application startup and clean window shutdown without an unhandled UI exception.

Manual acceptance still needed on additional hardware: dragging regions across multiple monitors with different DPI settings, actual global-hotkey delivery while minimized, display reconnects, and compatibility with the user's intended third-party applications. Browser-controlled app state and complex editors may require dedicated adapters.

Run `scripts/test-windows.ps1` on an unlocked interactive desktop to reproduce the integration checks. The fixture briefly displays its own test windows; avoid interacting with them during the focus checks.
