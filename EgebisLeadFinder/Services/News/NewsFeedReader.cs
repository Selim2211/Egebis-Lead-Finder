using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using EgebisLeadFinder.Models;

namespace EgebisLeadFinder.Services.News;

/// <summary>Toptan haber akışı (sorgu almaz; son haberleri liste olarak verir).</summary>
public record NewsFeed(string Name, string Url, NewsFeedKind Kind);

public enum NewsFeedKind { Rss, ResmiGazete }

/// <summary>
/// Ek haber kaynakları: ekonomi gazetelerinin RSS akışları ve Resmî Gazete'nin günlük listesi. Ücretsiz ve anahtarsızdır.
/// Her akış ayrı denenir; biri hata verirse diğerleri etkilenmez. Akıştaki haberlerden yalnızca takip edilen firmaya
/// ya da radar konusuna uyanlar kaydedilir (bkz. <see cref="NewsFeedMatcher"/>).
/// </summary>
public class NewsFeedReader
{
    public static readonly IReadOnlyList<NewsFeed> Feeds = new[]
    {
        new NewsFeed("Anadolu Ajansı", "https://www.aa.com.tr/tr/rss/default?cat=ekonomi", NewsFeedKind.Rss),
        new NewsFeed("Dünya", "https://www.dunya.com/rss", NewsFeedKind.Rss),
        new NewsFeed("Ekonomim", "https://www.ekonomim.com/rss", NewsFeedKind.Rss),
        new NewsFeed("Bloomberg HT", "https://www.bloomberght.com/rss", NewsFeedKind.Rss),
        new NewsFeed("Resmî Gazete", "https://www.resmigazete.gov.tr/", NewsFeedKind.ResmiGazete)
    };

    private readonly IHttpClientFactory _http;
    private readonly ILogger<NewsFeedReader> _logger;

    public NewsFeedReader(IHttpClientFactory http, ILogger<NewsFeedReader> logger)
    {
        _http = http;
        _logger = logger;
    }

    /// <summary>Tüm akışları okur; hata veren akış boş liste döner ve loglanır.</summary>
    public async Task<List<(NewsFeed Feed, List<NewsHit> Hits)>> FetchAllAsync(CancellationToken ct)
    {
        var result = new List<(NewsFeed, List<NewsHit>)>();
        foreach (var feed in Feeds)
        {
            try
            {
                using var client = _http.CreateClient("news-feeds");
                using var request = new HttpRequestMessage(HttpMethod.Get, feed.Url);
                request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (compatible; LeadFinderNews/1.0)");
                using var response = await client.SendAsync(request, ct);
                response.EnsureSuccessStatusCode();
                var body = await response.Content.ReadAsStringAsync(ct);
                var hits = feed.Kind == NewsFeedKind.ResmiGazete
                    ? ParseResmiGazete(body, DateTime.UtcNow)
                    : ParseRss(body, feed.Name);
                result.Add((feed, hits));
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogWarning("Haber akışı okunamadı ({Feed}): {Error}", feed.Name, ex.Message);
                result.Add((feed, new List<NewsHit>()));
            }
        }
        return result;
    }

    /// <summary>Genel RSS: başlıktaki " - " ayrılmaz (Google Haberler'deki gibi kaynak eki yoktur); kaynak akışın adıdır.</summary>
    public static List<NewsHit> ParseRss(string xml, string sourceName) =>
        GoogleNewsRssSource.ParseRss(xml, splitSource: false)
            .Select(h => h with { Source = sourceName })
            .ToList();

    private static readonly Regex GazetteLink = new(
        @"<a[^>]+href=""(?<url>https://www\.resmigazete\.gov\.tr/eskiler/\d{4}/\d{2}/(?<date>\d{8})-\d+[^""]*\.htm)""[^>]*>(?<title>[\s\S]*?)</a>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Resmî Gazete ana sayfasındaki günün karar/yönetmelik/ilan bağlantıları.</summary>
    public static List<NewsHit> ParseResmiGazete(string html, DateTime nowUtc)
    {
        var hits = new List<NewsHit>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in GazetteLink.Matches(html))
        {
            var url = m.Groups["url"].Value;
            if (!seen.Add(url)) continue;

            var title = Regex.Replace(WebUtility.HtmlDecode(m.Groups["title"].Value), "<[^>]+>", " ");
            title = Regex.Replace(title, @"\s+", " ").Trim().TrimStart('–', '-', ' ').Trim();
            if (title.Length < 10) continue;

            DateTime? published = DateTime.TryParseExact(m.Groups["date"].Value, "yyyyMMdd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var d)
                // Resmî Gazete Türkiye saatiyle gece yayımlanır: o günün 00:00'ı (UTC+3).
                ? DateTime.SpecifyKind(d.AddHours(-NewsSlots.TurkeyOffsetHours), DateTimeKind.Utc)
                : nowUtc;
            hits.Add(new NewsHit(title, url, "Resmî Gazete", published, null, NewsRegions.Tr));
        }
        return hits;
    }
}

/// <summary>Toptan akıştaki bir haberi takip edilen firmaya ya da radar konusuna bağlar; ikisine de uymuyorsa alınmaz.</summary>
public static class NewsFeedMatcher
{
    public static (Company? Company, RadarTopic? Topic) Match(NewsHit hit, IReadOnlyList<Company> companies, IReadOnlyList<RadarTopic> topics)
    {
        var text = $"{hit.Title}. {hit.Snippet}";
        foreach (var c in companies)
            if (CompanyIntel.CompanyNameMatch.Key(c.Name).Length >= 3 && CompanyIntel.SnippetRelevance.IsAbout(c, text, hit.Url))
                return (c, null);

        var normalized = " " + TurkishText.Normalize(text) + " ";
        foreach (var t in topics)
            if (TopicMatches(t, normalized)) return (null, t);

        return (null, null);
    }

    /// <summary>Konunun tüm anlamlı kelimeleri (kök: ilk 5 harf) ve varsa şehri haberde geçmeli.</summary>
    public static bool TopicMatches(RadarTopic topic, string normalizedText)
    {
        var words = TurkishText.Normalize(topic.Query)
            .Split(new[] { ' ', ',', '"', '\'', '(', ')' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 3 && w is not ("ve" or "ile" or "veya" or "or" or "and"))
            .ToList();
        if (words.Count == 0) return false;
        if (!words.All(w => normalizedText.Contains(" " + (w.Length > 5 ? w[..5] : w), StringComparison.Ordinal))) return false;
        return string.IsNullOrWhiteSpace(topic.City)
               || normalizedText.Contains(" " + TurkishText.Normalize(topic.City).Trim(), StringComparison.Ordinal);
    }
}
