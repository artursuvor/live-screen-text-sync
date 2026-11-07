# Architecture and delivery plan

## Product contract

Select a rectangular source within one monitor, select a compatible target, and start synchronization. Capture defaults to 200 ms. OCR preserves lines; two matching successful results confirm an update. Empty results and OCR errors never clear the target. Repeated text is never written again during a session. Stop is also available through a global hotkey.

The source must remain visible. An overlapping window changes what is captured. Windows OCR languages must be installed locally. Elevated, protected, password, and arbitrary custom controls are not guaranteed to work.

## Components

- **Core**: normalization, stability confirmation, frame comparison, settings, and a bounded latest-frame synchronization pipeline. No Windows dependency.
- **App**: WPF selection overlays, physical-pixel capture, Windows.Media.Ocr, settings, local metadata logging, and a dedicated background automation worker.
- **Automation worker**: retains the selected UI Automation element, runtime identity, and owning process creation time. Inspects without writing; revalidates before every write. A parent-enforced timeout kills a stuck worker rather than leaving the UI blocked or accepting another target.
- **Tests**: deterministic pipeline and safety behavior tests, plus an opt-in Windows integration check against a dedicated test window.

## Deliberate MVP decisions

Use Win32 GDI BitBlt for capture of the visible desktop. The product explicitly needs visible screen pixels, and this avoids a D3D device and Windows.Graphics.Capture permission/session lifecycle. Capture is isolated behind an interface so a WGC backend can follow. This is a departure from the recommended capture technology, not from the visible-region behavior.

Background writing tries UI Automation ValuePattern first, then MSAA IAccessible for compatible native edit controls, then bounded WM_SETTEXT for allowlisted native edit controls. Never use SendInput, simulated shortcuts, clipboard replacement, SetFocus, or SetForegroundWindow in synchronization. Do not silently reselect an element after it disappears.

Settings persist source geometry and OCR preferences. A target descriptor is saved for reference, but the target must be explicitly reselected after restarting the app. This prevents stale identifiers from writing into an unintended field.

## Commit milestones

1. Project scaffold, architecture, and repository hygiene.
2. Core pipeline with stability, deduplication, latest-frame behavior, and tests.
3. Screen-region selection, capture, preprocessing, and local OCR.
4. Target inspection and isolated, validated background writing.
5. Finished desktop interface, settings, hotkey, integration checks, and distribution.

## Reference documentation

- [Windows OCR](https://learn.microsoft.com/en-us/uwp/api/windows.media.ocr.ocrengine)
- [UI Automation ValuePattern.SetValue](https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.valuepattern.setvalue)
- [SendMessageTimeout](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendmessagetimeoutw)
- [BitBlt](https://learn.microsoft.com/en-us/windows/win32/api/wingdi/nf-wingdi-bitblt)
