using System.Text;
using EgebisLeadFinder.Configuration;
using EgebisLeadFinder.Models;
using Microsoft.Extensions.Options;

namespace EgebisLeadFinder.Services;

/// <summary>
/// Firma sitesinden sinirli sayida sayfa okur. Butun interneti crawl etmiyoruz;
/// ana sayfa + hakkimizda/urunler/iletisim gibi birkac aday sayfa yeterli (dokuman bolum 5).
/// </summary>
public class WebScraperService : IWebScraperService
{
    private readonly HttpClient _http;
    private readonly ScraperOptions _options;
    private readonly ILogger<WebScraperService> _logger;

    public WebScraperService(HttpClient http, IOptions<ScraperOptions> options, ILogger<WebScraperService> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ScrapedSite> ScrapeAsync(string siteUrl, CancellationToken ct = default)
    {
        var result = new ScrapedSite { Url = siteUrl };

        if (!Uri.TryCreate(siteUrl, UriKind.Absolute, out var baseUri))
        {
            result.Error = $"Geçersiz URL: {siteUrl}";
            return result;
        }

        // Kucuk/eski uretici sitelerinin buyuk kisminda SSL sertifikasi yok:
        // https istegi baglanti kurulamadan dusuyor, http calisiyor. Arama
        // sonuclarindan gelen adresi her zaman https olarak kurdugumuz icin bu
        // firmalar tamamen kaybediliyordu (olculen: 50 firmada 29 kayip).
        baseUri = await ResolveReachableBaseUriAsync(baseUri, ct);
        result.Url = baseUri.ToString();

        var disallowed = _options.RespectRobotsTxt
            ? await GetDisallowedPathsAsync(baseUri, ct)
            : new List<string>();

        var textBuilder = new StringBuilder();
        var pagesRead = 0;

        // Cogu site olmayan yol icin 404 yerine ana sayfayi 200 ile dondurur.
        // Ayni icerigi ikinci kez okumak sayfa kotasini bosa harcar, o yuzden eleriz.
        var seenContent = new HashSet<string>();
        var linkEmails = new List<string>();
        var linkPhones = new List<string>();

        // Bir sunucu bizi engellemeye baslarsa (ilk istekten sonra sessizce timeout
        // vermeye baslamasi tipik bot korumasi belirtisidir) kalan tum aday yollari
        // tek tek denemek yerine siteden vazgeciyoruz. Aksi halde 8 yol x 10 sn
        // zaman asimi = tek firma icin 80+ saniye, kuyruktaki digerlerini bekletir.
        const int maxConsecutiveTimeouts = 2;
        var consecutiveTimeouts = 0;

        // Site basina toplam okuma suresi de sinirli: yollar tek tek basarili
        // olsa bile cok yavas bir sunucu tum bunlari toplayip pipeline'i kilitlemesin.
        using var siteBudget = new CancellationTokenSource(TimeSpan.FromSeconds(_options.TimeoutSeconds * 3));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, siteBudget.Token);

        foreach (var path in _options.CandidatePaths)
        {
            if (pagesRead >= _options.MaxPagesPerSite) break;
            if (textBuilder.Length >= _options.MaxTextChars) break;
            if (consecutiveTimeouts >= maxConsecutiveTimeouts)
            {
                _logger.LogDebug("Ardışık zaman aşımı nedeniyle siteden vazgeçildi: {Url}", baseUri);
                break;
            }
            ct.ThrowIfCancellationRequested();

            if (IsDisallowed(path, disallowed))
            {
                _logger.LogDebug("robots.txt engelledi: {Url}{Path}", baseUri, path);
                continue;
            }

            var pageUrl = new Uri(baseUri, path).ToString();
            var (html, timedOut) = await TryGetWithStatusAsync(pageUrl, linkedCts.Token, ct);
            consecutiveTimeouts = timedOut ? consecutiveTimeouts + 1 : 0;
            if (html is null) continue;

            var text = TextExtractor.ToPlainText(html);
            if (text.Length < 50) continue; // bos veya anlamsiz sayfa

            // Iletisim bilgileri sayfa elense bile degerli, once onlari topla.
            var (hrefEmails, hrefPhones) = TextExtractor.ExtractContactLinks(html);
            linkEmails.AddRange(hrefEmails);
            linkPhones.AddRange(hrefPhones);

            if (!seenContent.Add(ContentFingerprint(text)))
            {
                _logger.LogDebug("Yinelenen içerik atlandı: {Url}", pageUrl);
                continue;
            }

            result.VisitedUrls.Add(pageUrl);
            pagesRead++;

            textBuilder.AppendLine($"--- {pageUrl} ---");
            textBuilder.AppendLine(text);
            textBuilder.AppendLine();

            foreach (var person in PersonExtractor.Extract(text))
            {
                result.People.Add(new ScrapedPerson
                {
                    Name = person.Name,
                    Title = person.Title,
                    SourceUrl = pageUrl
                });
            }

            // Hedef siteyi yormamak icin istekler arasi kisa bekleme.
            if (_options.DelayBetweenRequestsMs > 0)
                await Task.Delay(_options.DelayBetweenRequestsMs, ct);
        }

        if (pagesRead == 0)
        {
            result.Error = "Sitede okunabilir sayfa bulunamadı.";
            return result;
        }

        var full = textBuilder.ToString();

        // E-posta/telefon aramasi kirpilmamis metin uzerinde yapilir; iletisim bilgisi
        // genelde sayfanin altinda yer alir ve kirpma sirasinda kaybolabilir.
        // href'ten gelenler once gelsin: mailto: degerleri regex tahmininden daha guvenilir.
        result.Emails = linkEmails
            .Concat(TextExtractor.FindEmails(full))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToList();

        result.Phones = linkPhones
            .Concat(TextExtractor.FindPhones(full))
            .Distinct()
            .Take(10)
            .ToList();

        // AI'a gonderilecek metin token maliyeti icin kirpilir.
        result.Text = full.Length > _options.MaxTextChars
            ? full[.._options.MaxTextChars]
            : full;

        AttachEmailsToPeople(result);

        return result;
    }

    private async Task<string?> TryGetAsync(string url, CancellationToken ct)
    {
        var (html, _) = await TryGetWithStatusAsync(url, ct, ct);
        return html;
    }

    /// <summary>
    /// https ile ana sayfaya ulasilamiyorsa ayni adresi http ile dener.
    /// SSL sertifikasi olmayan eski uretici sitelerini kurtarir; https zaten
    /// calisiyorsa fazladan istek atilmaz.
    /// </summary>
    private async Task<Uri> ResolveReachableBaseUriAsync(Uri baseUri, CancellationToken ct)
    {
        if (!baseUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return baseUri;

        if (await IsReachableAsync(baseUri, ct))
            return baseUri;

        var httpUri = new UriBuilder(baseUri) { Scheme = Uri.UriSchemeHttp, Port = -1 }.Uri;

        if (await IsReachableAsync(httpUri, ct))
        {
            _logger.LogDebug("https başarısız, http kullanılıyor: {Host}", baseUri.Host);
            return httpUri;
        }

        // Ikisi de basarisiz: orijinal adresle devam edilir, hata akisin
        // ilerisinde "okunabilir sayfa bulunamadi" olarak raporlanir.
        return baseUri;
    }

    /// <summary>Ana sayfaya HEAD/GET ile ulasilabiliyor mu? Govde indirilmez.</summary>
    private async Task<bool> IsReachableAsync(Uri uri, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd(_options.UserAgent);

            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// TryGetAsync ile ayni is, ama zaman asimi olup olmadigini da dondurur.
    /// Ardisik zaman asimlarini saymak icin gerekli: bir sunucu bizi engellemeye
    /// basladiginda kalan aday yollari denemeden vazgecebiliyoruz.
    /// </summary>
    /// <param name="requestToken">Istekte kullanilan token (site butcesiyle birlesik).</param>
    /// <param name="outerToken">Gercek dis iptal mi yoksa sadece butce/zaman asimi mi ayirt etmek icin.</param>
    private async Task<(string? Html, bool TimedOut)> TryGetWithStatusAsync(
        string url, CancellationToken requestToken, CancellationToken outerToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd(_options.UserAgent);

            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, requestToken);
            if (!response.IsSuccessStatusCode) return (null, false);

            var contentType = response.Content.Headers.ContentType?.MediaType;
            if (contentType is not null && !contentType.Contains("html", StringComparison.OrdinalIgnoreCase))
                return (null, false);

            return (await response.Content.ReadAsStringAsync(requestToken), false);
        }
        catch (OperationCanceledException) when (!outerToken.IsCancellationRequested)
        {
            _logger.LogDebug("Zaman aşımı: {Url}", url);
            return (null, true);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Sayfa okunamadı: {Url}", url);
            return (null, false);
        }
    }

    /// <summary>robots.txt icindeki "*" bloguna ait Disallow yollarini toplar.</summary>
    private async Task<List<string>> GetDisallowedPathsAsync(Uri baseUri, CancellationToken ct)
    {
        var disallowed = new List<string>();
        var robots = await TryGetRawAsync(new Uri(baseUri, "/robots.txt").ToString(), ct);
        if (robots is null) return disallowed;

        var appliesToUs = false;
        foreach (var raw in robots.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            if (line.StartsWith("User-agent:", StringComparison.OrdinalIgnoreCase))
            {
                appliesToUs = line[11..].Trim() == "*";
                continue;
            }

            if (appliesToUs && line.StartsWith("Disallow:", StringComparison.OrdinalIgnoreCase))
            {
                var path = line[9..].Trim();
                if (path.Length > 0) disallowed.Add(path);
            }
        }

        return disallowed;
    }

    private async Task<string?> TryGetRawAsync(string url, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd(_options.UserAgent);
            using var response = await _http.SendAsync(request, ct);
            return response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync(ct) : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Sayfa icerigini tekillestirmek icin kisa bir imza uretir.
    /// Tam metin karsilastirmasi yerine bas kismi yeterli; menu/footer farkliliklari
    /// zaten ayni sablondan geldigi icin sonucu degistirmez.
    /// </summary>
    private static string ContentFingerprint(string text)
    {
        var normalized = text.Length > 1500 ? text[..1500] : text;
        return normalized.Replace(" ", string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty);
    }

    private static bool IsDisallowed(string path, List<string> disallowed)
    {
        if (disallowed.Count == 0) return false;
        // "/" tek basina tum siteyi kapatir.
        if (disallowed.Contains("/")) return true;
        return disallowed.Any(d => d != "/" && path.StartsWith(d, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Metinde bulunan e-postalari isim benzerligine gore kisilerle eslestirir.</summary>
    private static void AttachEmailsToPeople(ScrapedSite site)
    {
        foreach (var person in site.People)
        {
            if (string.IsNullOrWhiteSpace(person.Name)) continue;

            var parts = person.Name.ToLowerInvariant()
                .Replace('ı', 'i').Replace('ğ', 'g').Replace('ü', 'u')
                .Replace('ş', 's').Replace('ö', 'o').Replace('ç', 'c')
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length < 2) continue;

            person.Email = site.Emails.FirstOrDefault(e =>
            {
                var local = e.Split('@')[0];
                return local.Contains(parts[0]) && local.Contains(parts[^1]);
            });
        }
    }
}
