using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace EgebisLeadFinder.Services;

/// <summary>HTML'den duz metin, e-posta ve telefon cikarma yardimcilari.</summary>
public static partial class TextExtractor
{
    // Uzanti serbest birakildiginda ("[a-zA-Z]{2,}") HTML'de e-postaya bitisik
    // duran metin de yutuluyordu: "info@firma.com.trbinbirsoft".
    // Bu yuzden uzanti bilinen bir listeyle sinirlandirildi.
    [GeneratedRegex(
        @"[a-zA-Z0-9._%+\-]+@[a-zA-Z0-9\-]+(?:\.[a-zA-Z0-9\-]+)*\.(?:com|net|org|edu|gov|info|biz|name|online|shop|tech|io|co|com\.tr|org\.tr|gov\.tr|edu\.tr|tr|de|uk|fr|it|es|nl|be|at|ch|se|no|dk|fi|pl|cz|ru|ua|us|ca|cn|jp|kr|in|br)(?![a-zA-Z0-9\-])",
        RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex EmailRegex();

    // +90 ile veya 0 ile baslayan Turkiye formatlari; ayirici olarak bosluk/tire/parantez/nokta.
    [GeneratedRegex(@"(?:\+90|0)?[\s\-\.\(]*(?:2\d{2}|3\d{2}|5\d{2})[\s\-\.\)]*\d{3}[\s\-\.]*\d{2}[\s\-\.]*\d{2}", RegexOptions.Compiled)]
    private static partial Regex PhoneRegex();

    [GeneratedRegex(@"\s{2,}", RegexOptions.Compiled)]
    private static partial Regex WhitespaceRegex();

    /// <summary>
    /// mailto: ve tel: baglantilarindaki iletisim bilgilerini toplar.
    /// Bu degerler cogu sitede goruntulenen metinde yer almaz, sadece href icinde bulunur.
    /// </summary>
    public static (List<string> Emails, List<string> Phones) ExtractContactLinks(string html)
    {
        var emails = new List<string>();
        var phones = new List<string>();

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var links = doc.DocumentNode.SelectNodes("//a[@href]");
        if (links is null) return (emails, phones);

        foreach (var link in links)
        {
            var href = HtmlEntity.DeEntitize(link.GetAttributeValue("href", string.Empty))?.Trim();
            if (string.IsNullOrEmpty(href)) continue;

            if (href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
            {
                // "mailto:a@b.com?subject=..." -> "a@b.com"
                var value = href[7..].Split('?')[0].Trim().ToLowerInvariant();
                if (value.Contains('@')) emails.Add(value);
            }
            else if (href.StartsWith("tel:", StringComparison.OrdinalIgnoreCase))
            {
                var value = href[4..].Trim();
                if (value.Count(char.IsDigit) >= 10) phones.Add(value);
            }
        }

        return (emails.Distinct().ToList(), phones.Distinct().ToList());
    }

    /// <summary>Script/style/nav gibi gurultuyu atip okunabilir metin dondurur.</summary>
    public static string ToPlainText(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var noise = doc.DocumentNode.SelectNodes("//script|//style|//noscript|//svg|//iframe");
        if (noise is not null)
            foreach (var node in noise)
                node.Remove();

        var text = HtmlEntity.DeEntitize(doc.DocumentNode.InnerText) ?? string.Empty;

        var sb = new StringBuilder();
        foreach (var line in text.Split('\n'))
        {
            var trimmed = WhitespaceRegex().Replace(line, " ").Trim();
            if (trimmed.Length > 0) sb.AppendLine(trimmed);
        }

        return sb.ToString().Trim();
    }

    public static IEnumerable<string> FindEmails(string text) =>
        EmailRegex().Matches(text)
            .Select(m => m.Value.ToLowerInvariant())
            // Sablon/asset dosyalarindan gelen sahte eslesmeleri ele.
            .Where(e => !e.EndsWith(".png") && !e.EndsWith(".jpg") && !e.EndsWith(".gif")
                     && !e.EndsWith(".webp") && !e.EndsWith(".svg"))
            .Distinct();

    public static IEnumerable<string> FindPhones(string text) =>
        PhoneRegex().Matches(text)
            .Select(m => WhitespaceRegex().Replace(m.Value, " ").Trim())
            .Where(p => p.Count(char.IsDigit) >= 10)
            .Distinct();
}
