using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SessionDeck.Services;

/// <summary>
/// Derives a display title for a session from its Claude Code transcript (.jsonl):
/// the last summary entry wins (that's also what VSCode shows as the tab label);
/// fallback — the first real user prompt. Best-effort heuristic: any parse failure
/// yields null and the card keeps its "session xxxxxxxx" title.
/// </summary>
public static class TranscriptReader
{
    private const int MaxTitleLength = 60;

    public static string? ReadTitle(string path)
    {
        try
        {
            string? summary = null, firstUserText = null;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            while (reader.ReadLine() is { } line)
            {
                if (line.Length == 0) continue;
                if (line.Contains("\"summary\""))
                    summary = TryReadSummary(line) ?? summary;
                else if (firstUserText == null && line.Contains("\"user\""))
                    firstUserText = TryReadUserText(line);
            }
            return Shorten(summary ?? firstUserText);
        }
        catch
        {
            return null;
        }
    }

    private static string? TryReadSummary(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.TryGetProperty("type", out var type) && type.GetString() == "summary" &&
                root.TryGetProperty("summary", out var summary))
                return summary.GetString();
        }
        catch { }
        return null;
    }

    private static string? TryReadUserText(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var type) || type.GetString() != "user") return null;
            if (root.TryGetProperty("isMeta", out var meta) && meta.ValueKind == JsonValueKind.True) return null;
            if (!root.TryGetProperty("message", out var message) ||
                !message.TryGetProperty("content", out var content)) return null;

            string? text = content.ValueKind switch
            {
                JsonValueKind.String => content.GetString(),
                JsonValueKind.Array => content.EnumerateArray()
                    .Where(e => e.TryGetProperty("type", out var t) && t.GetString() == "text")
                    .Select(e => e.TryGetProperty("text", out var txt) ? txt.GetString() : null)
                    .FirstOrDefault(t => t != null),
                _ => null,
            };
            // Command wrappers (<command-name>, <system-reminder>, caveats) aren't real prompts.
            if (text == null || text.StartsWith('<') || text.StartsWith("Caveat:")) return null;
            return text;
        }
        catch
        {
            return null;
        }
    }

    private static string? Shorten(string? title)
    {
        if (title == null) return null;
        title = Regex.Replace(title, @"\s+", " ").Trim();
        if (title.Length == 0) return null;
        return title.Length <= MaxTitleLength ? title : title[..(MaxTitleLength - 1)] + "…";
    }
}
