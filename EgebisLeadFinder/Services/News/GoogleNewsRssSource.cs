using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace EgebisLeadFinder.Services.News;

/// <summary>
/// Google Haberler RSS: ücretsiz, anahtarsız. Resmî bir API değildir; bu yüzden istekler yavaş atılır
/// ve hata verirse tarama servisi yedek kaynağa geçer. Adres sabittir (istek yalnızca news.google.com'a gider).
/// </summary>
public class GoogleNewsRssSource : INewsSource
{
    private readonly HttpClient _http;

    public GoogleNewsRssSource(HttpClient http) => _http = http;

    public string Name => "Google Haberler";
    public bool IsFallback => false;

    public async Task<List<NewsHit>> SearchAsync(string query, CancellationToken ct = default)
    {
        var url = "https://news.google.com/rss/search?q=" + Uri.EscapeDataString(query) + "&hl=tr&gl=TR&ceid=TR:tr";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (compatible; LeadFinderNews/1.0)");

        using var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return ParseRss(await response.Content.ReadAsStringAsync(ct));
    }

    /// <summary>RSS metnini haberlere çevirir. Başlık "Haber başlığı - Kaynak" biçimindeyse kaynak ayrılır.</summary>
    public static List<NewsHit> ParseRss(string xml)
    {
        var hits = new List<NewsHit>();
        XDocument doc;
        try { doc = XDocument.Parse(xml); }
        catch { return hits; }

        foreach (var item in doc.Descendants("item"))
        {
            var rawTitle = WebUtility.HtmlDecode((string?)item.Element("title") ?? string.Empty).Trim();
            var link = ((string?)item.Element("link"))?.Trim();
            if (rawTitle.Length == 0 || string.IsNullOrWhiteSpace(link)) continue;

            var source = (string?)item.Element("source");
            var title = rawTitle;
            var dash = rawTitle.LastIndexOf(" - ", StringComparison.Ordinal);
            if (dash > 10)
            {
                source ??= rawTitle[(dash + 3)..].Trim();
                title = rawTitle[..dash].Trim();
            }

            DateTime? published = null;
            if (DateTime.TryParse((string?)item.Element("pubDate"), CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var p))
                published = DateTime.SpecifyKind(p, DateTimeKind.Utc);

            var description = Regex.Replace(WebUtility.HtmlDecode((string?)item.Element("description") ?? string.Empty), "<[^>]+>", " ");
            description = Regex.Replace(description, @"\s+", " ").Trim();

            hits.Add(new NewsHit(title, link!, source?.Trim(), published, description.Length == 0 ? null : description));
        }
        return hits;
    }
}
