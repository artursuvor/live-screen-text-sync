# Live Screen Text Sync

A Windows desktop utility that reads a visible screen region with local OCR and synchronizes confirmed text to a compatible field in another application without moving keyboard focus.

Built with C# / .NET 10 and WPF. All recognition runs locally. Captured images stay in memory; recognized text is excluded from logs. The running utility makes no network requests.

![Application interface](docs/images/app.png)

## Quick start

1. Extract the complete portable ZIP and run `LiveScreenTextSync.App.exe`. Keep all extracted files together; the package includes .NET.
2. Click **Select Source**, then drag a rectangle within one monitor. Press **Esc** to cancel. A mint outline marks the selected area during setup.
3. Click **Select Target**, hover over an editable field in another application, and press **F8**. Inspection does not clear or change the field.
4. Check the compatibility result, then click **Start Sync**. The source must remain visible and unobstructed.
5. Use **Pause / Resume**, **Stop**, or the global **Ctrl + Alt + Shift + S** stop shortcut. **Preview Only** runs OCR without writing to another app.

Use **Test Window** to open an isolated sample application. Select its `SCREEN SYNC READY` source and its **standard native edit target** for the verified background path. **Change Source** changes the sample text.

**Settings** lets you choose an installed OCR language, a 100–5000 ms capture interval (default 200), two-result confirmation, and optional image enhancement. With confirmation enabled, unchanged pixels are still recognized a second time to establish stability. OCR and capture run independently; old frames are discarded instead of queued.

## Compatibility and limits

- Standard Win32 / WinForms edit fields use bounded `WM_SETTEXT`, with process/element validation and mandatory readback. This path passed foreground and keyboard-focus preservation checks.
- Other editable fields may expose UI Automation `ValuePattern` or `LegacyIAccessible`. API support does not guarantee that an application remains in the background. On the tested Windows build, WPF UIA writes activated the target window. The first real update can expose this behavior; the app then invalidates the target and stops. Inspection never performs a probe write.
- Password, protected, disabled, and read-only fields are rejected. Elevated processes, browser app state, rich editors, and custom controls may need a dedicated adapter. There is no keyboard or clipboard fallback.
- Capture reads visible desktop pixels through GDI `BitBlt`. If another window covers the source, its pixels become the input. Sources must fit within one monitor and within the Windows OCR image-size limit. Display or DPI changes invalidate the selection.
- The portable build was tested with Windows OCR on this machine. Availability on other Windows 10/11 builds depends on their OCR runtime and installed language features. Some desktop OCR scenarios documented by Microsoft require package identity; MSIX distribution is not included in this MVP.
- A hung target is bounded by a 3-second worker timeout. Stop cancels pending work and kills a worker if an automation request is in flight; already-completed target updates cannot be undone. Reselect the target if its worker was invalidated.

## Target selection troubleshooting

Version 0.1.1 recognizes writable `ComboBox`, `Custom`, and focusable `Pane` roles as well as ordinary `Edit` and `Document` fields. It follows the raw accessibility path and only considers descendants under the actual pointer, so a label or container does not silently select a different nearby input.

Click inside the destination input, keep the pointer inside it, and press **F8** while selection is active. The target card displays **Selecting a target…** during selection. If a field is rejected, its diagnostic includes the detected process, accessibility role, framework, and available value-writing patterns. A text-reading pattern alone does not provide a background setter. Password and read-only fields remain blocked.

Close an older running copy before launching an update, since only one instance can own the global emergency-stop shortcut. The window title displays the running version.

## Privacy and local data

Settings and metadata-only rolling logs live in `%LOCALAPPDATA%\LiveScreenTextSync`. Logs contain status events and exception types, never recognized text, target contents, or screenshots. The log retains up to seven daily files.

Source geometry and a target descriptor are saved in JSON. Targets must be selected again after restarting; the utility never silently reconnects to another field. Screen images and OCR results are held only in memory.

## Development

Requires Windows 10 version 2004 or later / Windows 11, the .NET 10 SDK, and at least one installed Windows OCR language. The portable package targets x64.

```powershell
dotnet build LiveScreenTextSync.sln
dotnet test LiveScreenTextSync.sln
dotnet run --project src/LiveScreenTextSync.App
```

Run the integration suite on an unlocked desktop (it briefly opens application-owned test windows):

```powershell
.\scripts\test-windows.ps1
```

Build a portable self-contained ZIP:

```powershell
.\scripts\package.ps1
```

The output is `artifacts/LiveScreenTextSync-win-x64.zip`. GitHub Actions builds, runs core tests, and publishes a downloadable build artifact. Interactive desktop checks run locally rather than in CI.

See [architecture](docs/architecture.md) and [validation results](docs/validation.md) for the implementation decisions, measured behavior, and remaining manual coverage.
