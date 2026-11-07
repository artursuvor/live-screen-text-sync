using System.Text;

namespace LiveScreenTextSync.Core;

public sealed class TextConfirmation(bool confirmTwice)
{
    private string? candidate;
    private string? committed;
    private int count;

    public bool NeedsConfirmation => candidate is not null && candidate != committed;

    public static string Normalize(string text) => string.Join('\n', text
        .Normalize(NormalizationForm.FormC)
        .Replace("\r\n", "\n").Replace('\r', '\n').Replace('\u00a0', ' ')
        .Split('\n').Select(line => line.TrimEnd())).Trim();

    public string? Observe(string text)
    {
        var normalized = Normalize(text);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            Reject();
            return null;
        }
        count = candidate == normalized ? count + 1 : 1;
        candidate = normalized;
        return normalized != committed && count >= (confirmTwice ? 2 : 1) ? normalized : null;
    }

    public void Commit(string text) => committed = text;

    public void Reject()
    {
        candidate = null;
        count = 0;
    }
}
