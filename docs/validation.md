# Validation

Version 0.3.0 was validated on Windows 11 build 26200, x64, with .NET SDK 10.0.100, Windows OCR, and the bundled Tesseract engine on November 7, 2026.

## Automated checks

All 23 core tests passed. They cover normalization, optional confirmation, empty/error results, deduplication, pixel comparison, interval validation, cancellation before an output update, pause/resume state, stale-frame handling, preserving thin strokes, background normalization, image-size limits, and invalid buffers. Clipboard tests cover opt-in behavior, latest-only retries while busy, no duplicate retry after success, cancellation on stop/disable, and ignoring empty results.

Fifteen integration checks run the real main window and isolated sample sources:

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
| All twelve bundled models load and mixed-language choices are present | Passed |
| Ukrainian Cyrillic is recognized using bundled data | Passed |
| German accented text is preserved | Passed |
| Auto-copy can be enabled during capture and copies the current result | Passed |
| Changed recognition automatically reaches the clipboard writer | Passed |
| Disabling auto-copy stops writes while capture continues | Passed |
| Russian + English recognizes both scripts in the same line | Passed |

A separate startup/shutdown smoke check passed without an unhandled UI exception. Diagnostic runs preserve user settings. Tests do not write to other user applications or modify the clipboard.

## Visual and manual coverage

The WPF layout is rendered and inspected in normal, settings-expanded, and minimum-size states. The minimum-size layout can scroll when needed. Rendered images contain only this app's visual content.

Clipboard copying uses the Windows Forms Unicode clipboard API with one OS attempt at a time. Tests exercise the actual main-window copy flow through an isolated recording clipboard writer, leaving the user's clipboard unchanged. Native clipboard paste and global-key delivery remain manual checks.

Additional hardware coverage is needed for multiple monitors with different DPI settings and display reconnects. Source pixels must remain visible. Windows.Media.Ocr availability on other Windows 10/11 installations depends on their OCR runtime and installed language features.

## Reproduce

Run `scripts/test-windows.ps1` on an unlocked desktop. It builds, runs core tests, checks the local capture flow, and verifies UI startup and shutdown. The optional legacy `--self-test` diagnostic remains available for the retired external-target backend; it is not part of the current capture flow.
