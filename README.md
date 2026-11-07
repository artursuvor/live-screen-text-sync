# Live Screen Text Sync

A Windows desktop utility that reads a visible screen region with local OCR and synchronizes confirmed text to a compatible field in another application without moving keyboard focus.

Built with C# / .NET 10 and WPF. All recognition runs locally. Captured images stay in memory; recognized text is excluded from logs.

## Development

Requires Windows 10 version 2004 or later, the .NET 10 SDK, and at least one installed Windows OCR language.

```powershell
dotnet build LiveScreenTextSync.sln
dotnet test LiveScreenTextSync.sln
dotnet run --project src/LiveScreenTextSync.App
```

See [the implementation plan](docs/architecture.md) for architecture, safety boundaries, and delivery milestones.
