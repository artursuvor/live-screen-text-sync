# Architecture

## Current product: local text capture

The app reads a selected visible screen region into its own WPF text field. There is no external target selection or background writing in the main workflow. Copying to the clipboard is an explicit user action.

The path is:

`Region selection → physical-pixel capture → latest-frame channel → Windows OCR → text comparison → own text field`

- **Core** contains frame comparison, text normalization, deduplication, optional stability tracking, and a one-frame bounded channel. Capture and OCR run independently; outdated OCR results are discarded.
- **Capture** uses Win32 GDI BitBlt for visible desktop pixels, validates monitor topology and DPI, and stores frames only in memory.
- **OCR** uses Windows.Media.Ocr with the selected installed recognizer. Small regions are automatically enlarged up to 4× with bilinear interpolation and padded with the local background color. Optional enhancement applies grayscale, contrast, and dark-background normalization. An empty first result is retried with enhancement. Thin strokes are preserved instead of running a median filter before enlargement.
- **Local writer** dispatches nonempty changed text into the main window. Confirmation is disabled for immediate local display. Empty OCR and failures retain the previous useful text.
- **Desktop controls** provide region selection, Start Capture, Pause / Resume, Stop, Copy Text, settings, and a sample source.
- **Diagnostics** exercise actual capture and OCR through the real main window against an application-owned sample source. Diagnostic runs do not change persisted user settings.

Pause retains the confirmed text to avoid duplicate updates on resume. Stop cancels work before further local display updates, releases capture resources, and keeps the text available to copy. The optional global stop shortcut is Ctrl+Alt+Shift+S; a registration collision does not block this local-only mode.

Settings persist source geometry and OCR preferences as JSON. Recognized text is never persisted or logged. The clipboard changes only when the user requests a copy.

## Historical implementation

Versions 0.1.x implemented external target inspection and background writing through an isolated UI Automation worker. That backend and its diagnostic fixture are retained for reference; `MainWindow` does not instantiate it. This preserves the earlier work while removing external compatibility requirements from the current product.

## References

- [Windows OCR](https://learn.microsoft.com/en-us/uwp/api/windows.media.ocr.ocrengine)
- [BitBlt](https://learn.microsoft.com/en-us/windows/win32/api/wingdi/nf-wingdi-bitblt)
