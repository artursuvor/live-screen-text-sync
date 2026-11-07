using System.IO;
using System.Text.Json;
using LiveScreenTextSync.App.Automation;
using LiveScreenTextSync.Core;

namespace LiveScreenTextSync.App.Services;

public sealed record UserSettings
{
    public int IntervalMs { get; init; } = 200;
    public bool ConfirmTwice { get; init; } = true;
    public bool Preprocess { get; init; }
    public string? Language { get; init; }
    public ScreenRegion? Source { get; init; }
    public string? DisplayTopology { get; init; }
    public TargetInfo? LastTarget { get; init; }
}

public static class SettingsStore
{
    public static string DataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LiveScreenTextSync");
    private static string FilePath => Path.Combine(DataDirectory, "settings.json");
    public static UserSettings Load()
    {
        try
        {
            var settings = JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(FilePath)) ?? new();
            new SyncOptions(settings.IntervalMs).Validate();
            return settings;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentOutOfRangeException) { return new(); }
    }
    public static void Save(UserSettings settings)
    {
        Directory.CreateDirectory(DataDirectory);
        var temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, FilePath, overwrite: true);
    }
}
