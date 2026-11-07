using System.Text.RegularExpressions;

namespace EgebisLeadFinder.Services.Inbox;

/// <summary>
/// Gelen cevabin yalnizca yeni yazilan kismini cikarir: e-posta istemcilerinin altina ekledigi alintiyi
/// ("… tarihinde … yazdı:", "On … wrote:", "-----Original Message-----", "Kimden:" blogu, "&gt;" ile baslayan satirlar) keser.
/// </summary>
public static partial class ReplyText
{
    public const int MaxLength = 20000;

    public static string Clean(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return string.Empty;
        var lines = body.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var kept = new List<string>();
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var t = line.Trim();
            if (t.StartsWith('>')) break;
            if (QuoteHeader().IsMatch(t)) break;
            // Outlook: "Kimden: …" / "From: …" ardindan "Gönderildi:/Sent:" gelen blok alintinin basidir.
            if (FromLine().IsMatch(t) && lines.Skip(i + 1).Take(3).Any(l => SentLine().IsMatch(l.Trim()))) break;
            // "… tarihinde … şunu yazdı:" iki satira bolunebilir.
            if (i + 1 < lines.Length && QuoteHeader().IsMatch(t + " " + lines[i + 1].Trim()) && t.Length > 10) break;
            kept.Add(line.TrimEnd());
        }

        var text = string.Join('\n', kept).Trim();
        text = BlankLines().Replace(text, "\n\n");
        if (text.Length == 0) text = body.Trim();   // hepsi alinti sayildiysa (ör. ust bilgi yok) govdeyi aynen goster
        return text.Length > MaxLength ? text[..MaxLength] + "…" : text;
    }

    /// <summary>Bildirim onizlemesi: ilk 160 karakter, tek satir.</summary>
    public static string Preview(string? text)
    {
        var t = Whitespace().Replace(text ?? string.Empty, " ").Trim();
        return t.Length > 160 ? t[..160].TrimEnd() + "…" : t;
    }

    [GeneratedRegex(@"(^On .{4,200} wrote:$)|(^.{4,200} tarihinde .{0,200}(yazdı|şunu yazdı):$)|(^Am .{4,200} schrieb .{0,200}:$)|(^-{2,}\s*(original message|özgün ileti|orijinal ileti|ursprüngliche nachricht|forwarded message|iletilen ileti)\s*-{2,})|(^_{8,}$)", RegexOptions.IgnoreCase)]
    private static partial Regex QuoteHeader();

    [GeneratedRegex(@"^(from|kimden|gönderen)\s*:", RegexOptions.IgnoreCase)]
    private static partial Regex FromLine();

    [GeneratedRegex(@"^(sent|gönderildi|gönderilme|tarih|date|to|kime)\s*:", RegexOptions.IgnoreCase)]
    private static partial Regex SentLine();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex BlankLines();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
