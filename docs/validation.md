# Validation

Version 0.2.1 was validated on Windows 11 build 26200, x64, with .NET SDK 10.0.100 and the installed en-US Windows OCR recognizer on November 7, 2026.

## Automated checks

All 18 core tests passed. They cover normalization, optional confirmation, empty/error results, deduplication, pixel comparison, interval validation, cancellation before an output update, pause/resume state, stale-frame handling, preserving one-pixel stems during enlargement, light-background normalization, image-size limits, and invalid buffers.

Eight integration checks run the real main window and isolated sample sources:

| Check | Result |
| --- | --- |
| Main window has one local capture workflow, with no target selector | Passed |
| Visible source is recognized into the app without selecting a destination | Passed |
| Changed source refreshes the app's text field | Passed |
| Blank OCR preserves the last useful text | Passed |
| Stop freezes the result; Copy Text and Start Capture remain available | Passed |
| 11-pixel text in a 512 × 16 px single-line region is recognized with enhancement off | Passed |
| Changed text in the small region refreshes the local result | Passed |
| Small light text on a dark background is recognized | Passed |

A separate startup/shutdown smoke check passed without an unhandled UI exception. Diagnostic runs preserve user settings. Tests do not write to other user applications or modify the clipboard.

## Visual and manual coverage

The WPF layout is rendered and inspected in normal, settings-expanded, and minimum-size states. The minimum-size layout can scroll when needed. Rendered images contain only this app's visual content.

Clipboard copying uses WPF Clipboard.SetText on the explicit Copy Text button, with a visible retry message if the clipboard is busy. Automated tests leave the user's clipboard unchanged; actual clipboard paste and global-key delivery remain manual checks.

Additional hardware coverage is needed for multiple monitors with different DPI settings and display reconnects. Source pixels must remain visible. Windows.Media.Ocr availability on other Windows 10/11 installations depends on their OCR runtime and installed language features.

## Reproduce

Run `scripts/test-windows.ps1` on an unlocked desktop. It builds, runs core tests, checks the local capture flow, and verifies UI startup and shutdown. The optional legacy `--self-test` diagnostic remains available for the retired external-target backend; it is not part of the current capture flow.
