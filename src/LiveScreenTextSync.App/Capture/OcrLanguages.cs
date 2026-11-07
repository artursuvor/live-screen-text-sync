using System.IO;
using LiveScreenTextSync.Core;

namespace LiveScreenTextSync.App.Capture;

public sealed record OcrLanguage(string Id, string Display, string? WindowsTag, string Models);

public static class OcrLanguages
{
    private static readonly (string Tag, string Name, string Model)[] Bundled =
    [
        ("en-US", "English", "eng"), ("ru", "Russian", "rus"), ("uk-UA", "Ukrainian", "ukr"),
        ("de-DE", "German", "deu"), ("fr-FR", "French", "fra"), ("es-ES", "Spanish", "spa"),
        ("it-IT", "Italian", "ita"), ("pl-PL", "Polish", "pol"), ("pt-PT", "Portuguese", "por"),
        ("hu-HU", "Hungarian", "hun"), ("cs", "Czech", "ces"), ("tr-TR", "Turkish", "tur")
    ];

    public static IReadOnlyList<OcrLanguage> GetAvailable()
    {
        string[] native;
        try { native = WindowsOcr.Languages.Select(l => l.LanguageTag).ToArray(); }
        catch { native = []; }
        var choices = new List<OcrLanguage>();
        foreach (var (tag, name, model) in Bundled)
        {
            var nativeTag = native.FirstOrDefault(n => n.Split('-')[0].Equals(tag.Split('-')[0], StringComparison.OrdinalIgnoreCase));
            if (nativeTag is not null || HasModels(model)) choices.Add(new(tag, name, nativeTag, model));
        }
        foreach (var tag in native.Where(tag => !choices.Any(c => c.WindowsTag == tag)))
            choices.Add(new(tag, System.Globalization.CultureInfo.GetCultureInfo(tag).EnglishName, tag, ""));
        if (HasModels("rus+eng")) choices.Add(new("ru+en", "Russian + English", null, "rus+eng"));
        if (HasModels("ukr+eng")) choices.Add(new("uk+en", "Ukrainian + English", null, "ukr+eng"));
        return choices.OrderBy(c => c.Display, StringComparer.Ordinal).ToArray();
    }

    public static bool HasModels(string models) => !string.IsNullOrWhiteSpace(models) &&
        models.Split('+').All(model => File.Exists(Path.Combine(AppContext.BaseDirectory, "tessdata", model + ".traineddata")));

    public static ITextRecognizer Create(OcrLanguage language, bool enhance)
    {
        if (language.WindowsTag is not null)
        {
            try { return new WindowsOcr(language.WindowsTag, enhance); }
            catch (InvalidOperationException) when (HasModels(language.Models)) { }
        }
        return new BundledOcr(language.Models, enhance);
    }
}
