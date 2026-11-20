using System.Globalization;
using System.Text.Json;

namespace EgebisLeadFinder.Services.News;

/// <summary>
/// GDELT DOC 2.0 haber araması: ücretsiz, anahtarsız, dünya çapında (Türkçe dahil). Sağlayıcı en fazla
/// "5 saniyede bir istek" ister; istekler arasında 6 sn beklenir, 429 ya da hata gelirse 30 dk ara verilir.
/// Hata asla taramayı durdurmaz: boş liste döner.
/// </summary>
public class GdeltNewsSource
{
    private const string Endpoint = "https://api.gdeltproject.org/api/v2/doc/doc";
    private static readonly TimeSpan Spacing = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan Pause = TimeSpan.FromMinutes(30);
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static DateTime _nextAllowedUtc = DateTime.MinValue;
    private static DateTime _pausedUntilUtc = DateTime.MinValue;

    private readonly HttpClient _http;
    private readonly ILogger<GdeltNewsSource> _logger;

    public GdeltNewsSource(HttpClient http, ILogger<GdeltNewsSource> logger)
    {
        _http = http;
        _logger = logger;
    }

    public static bool IsPaused(DateTime nowUtc) => nowUtc < _pausedUntilUtc;

    /// <summary>Firma adıyla son 3 günün haberleri (en fazla 10).</summary>
    public async Task<List<NewsHit>> SearchCompanyAsync(string companyName, CancellationToken ct)
    {
        var name = companyName.Replace("\"", string.Empty).Trim();
        if (name.Length < 4 || IsPaused(DateTime.UtcNow)) return new List<NewsHit>();

        var url = $"{Endpoint}?query={Uri.EscapeDataString($"\"{name}\"")}&mode=artlist&format=json&maxrecords=10&timespan=3d&sort=datedesc";

        await Gate.WaitAsync(ct);
        try
        {
            var wait = _nextAllowedUtc - DateTime.UtcNow;
            if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (compatible; LeadFinderNews/1.0)");
            using var response = await _http.SendAsync(request, ct);
            _nextAllowedUtc = DateTime.UtcNow + Spacing;

            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                _pausedUntilUtc = DateTime.UtcNow + Pause;
                _logger.LogInformation("GDELT {Status} verdi; 30 dk ara veriliyor.", (int)response.StatusCode);
                return new List<NewsHit>();
            }
            return Parse(body);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _pausedUntilUtc = DateTime.UtcNow + Pause;
            _logger.LogInformation("GDELT okunamadı ({Error}); 30 dk ara veriliyor.", ex.Message);
            return new List<NewsHit>();
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>GDELT artlist JSON'u. Sonuç yoksa GDELT boş gövde ya da düz metin döner: boş liste.</summary>
    public static List<NewsHit> Parse(string json)
    {
        var hits = new List<NewsHit>();
        if (string.IsNullOrWhiteSpace(json) || json.TrimStart()[0] != '{') return hits;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("articles", out var articles) || articles.ValueKind != JsonValueKind.Array) return hits;
            foreach (var a in articles.EnumerateArray())
            {
                string? Str(string n) => a.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                var title = Str("title")?.Trim();
                var link = Str("url")?.Trim();
                if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(link)) continue;

                DateTime? published = DateTime.TryParseExact(Str("seendate"), "yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var p)
                    ? DateTime.SpecifyKind(p, DateTimeKind.Utc)
                    : null;
                // GDELT kaynağın ülkesini ve dilini verir: Türkiye/Türkçe ise TR, değilse Global.
                var turkish = string.Equals(Str("sourcecountry"), "Turkey", StringComparison.OrdinalIgnoreCase)
                              || string.Equals(Str("language"), "Turkish", StringComparison.OrdinalIgnoreCase);
                hits.Add(new NewsHit(title, link, Str("domain"), published, null, turkish ? Models.NewsRegions.Tr : Models.NewsRegions.Global));
            }
        }
        catch (JsonException) { }
        return hits;
    }
}
