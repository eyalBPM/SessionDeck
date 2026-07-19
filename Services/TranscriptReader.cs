using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SessionDeck.Services;

/// <summary>Titles derived from a Claude Code transcript (.jsonl).</summary>
/// <param name="TabTitle">The exact label VSCode shows on the session's tab: the last
/// "custom-title" entry (/rename) when present, else the last "ai-title" entry.
/// Primary display title and the session↔tab correlation key.</param>
/// <param name="AutoTitle">Heuristic session title: last summary entry, else the first
/// real user prompt. Secondary display title.</param>
public sealed record TranscriptInfo(string? TabTitle, string? AutoTitle);

/// <summary>
/// Single-pass transcript scanner. Best-effort: any parse failure yields nulls and the
/// card keeps its "session xxxxxxxx" title.
/// </summary>
public static class TranscriptReader
{
    private const int MaxTitleLength = 80;

    public static TranscriptInfo ReadInfo(string path)
    {
        try
        {
            string? customTitle = null, aiTitle = null, summary = null, firstUserText = null;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            while (reader.ReadLine() is { } line)
            {
                if (line.Length == 0) continue;
                if (line.Contains("\"custom-title\""))
                {
                    // /rename. An empty value (rename cleared) falls back to the ai-title.
                    string? t = TryGetString(line, "custom-title", "customTitle");
                    if (t != null) customTitle = t.Length > 0 ? t : null;
                }
                else if (line.Contains("\"ai-title\""))
                    aiTitle = TryGetString(line, "ai-title", "aiTitle") ?? aiTitle;
                else if (line.Contains("\"summary\""))
                    summary = TryGetString(line, "summary", "summary") ?? summary;
                else if (firstUserText == null && line.Contains("\"user\""))
                    firstUserText = TryReadUserText(line);
            }
            return new TranscriptInfo(Shorten(customTitle ?? aiTitle), Shorten(summary ?? firstUserText));
        }
        catch
        {
            return new TranscriptInfo(null, null);
        }
    }

    private static string? TryGetString(string line, string expectedType, string property)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.TryGetProperty("type", out var type) && type.GetString() == expectedType &&
                root.TryGetProperty(property, out var value))
                return value.GetString();
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
