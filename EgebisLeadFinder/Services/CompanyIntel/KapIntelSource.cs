using System.Text.Json;
using EgebisLeadFinder.Configuration;
using EgebisLeadFinder.Models;
using Microsoft.Extensions.Options;

namespace EgebisLeadFinder.Services.CompanyIntel;

/// <summary>
/// KAP (Kamuyu Aydinlatma Platformu) kontrolu. Firma halka acik veya bir halka
/// acik sirketin istiraki ise KAP'ta yapisal finansal veri bulunur.
///
/// Uc asamali:
///   1) KAP uye listesinde isim eslesmesi -> memberOid
///   2) memberOid ile son finansal raporun ozeti (KapFinancialClient: hasilat, kar, ozkaynak)
///   3) site:kap.org.tr aramasiyla son bildirim sayfalarini yakalama (uye listesi tutmazsa)
/// Hepsi bos donerse firma KAP kapsaminda degildir (hedeflerin cogu boyledir) ve bu bir hata degildir.
/// </summary>
public class KapIntelSource : ICompanyIntelSource
{
    private readonly HttpClient _http;
    private readonly ISearchService _search;
    private readonly KapFinancialClient _financials;
    private readonly ResearchOptions _options;
    private readonly ILogger<KapIntelSource> _logger;

    public KapIntelSource(
        HttpClient http,
        ISearchService search,
        KapFinancialClient financials,
        IOptions<ResearchOptions> options,
        ILogger<KapIntelSource> logger)
    {
        _http = http;
        _search = search;
        _financials = financials;
        _options = options.Value;
        _logger = logger;
    }

    public string Name => "KAP (Kamuyu Aydınlatma Platformu)";

    public async Task<SourceIntel> CollectAsync(Company company, CancellationToken ct = default)
    {
        var intel = new SourceIntel { SourceName = Name };
        var name = (company.Name ?? string.Empty).Trim();

        if (name.Length == 0)
        {
            intel.Error = "Firma adı yok.";
            return intel;
        }

        var oid = await TryMemberListAsync(name, intel, ct);

        if (oid is not null)
            await TryFinancialsAsync(oid, intel, ct);
        else
            await TryDisclosureSearchAsync(name, intel, ct);

        if (!intel.FoundSomething)
            _logger.LogInformation("{Company}: KAP kapsamında değil.", name);

        return intel;
    }

    /// <summary>KAP uye listesinde firma adiyla eslesme ara; bulursa memberOid doner.</summary>
    private async Task<string?> TryMemberListAsync(string name, SourceIntel intel, CancellationToken ct)
    {
        // Uc adresi yapilandirilmamis: dogrudan eslesmeyi atla, site: aramasina birak.
        if (string.IsNullOrWhiteSpace(_options.KapMemberListUrl))
            return null;

        try
        {
            using var response = await _http.GetAsync(_options.KapMemberListUrl, ct);
            if (!response.IsSuccessStatusCode) return null;

            var body = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);

            foreach (var el in EnumerateMembers(doc.RootElement))
            {
                var title = Text(el, "kapMemberTitle") ?? Text(el, "title") ?? Text(el, "name");
                if (string.IsNullOrWhiteSpace(title)) continue;
                if (!CompanyNameMatch.IsMatch(name, title)) continue;

                var stock = Text(el, "stockCode");
                var oid = Text(el, "memberOid") ?? Text(el, "oid");

                var url = oid is not null && _options.KapCompanyUrlTemplate.Contains("{id}", StringComparison.Ordinal)
                    ? _options.KapCompanyUrlTemplate.Replace("{id}", oid)
                    : "https://www.kap.org.tr";

                intel.Snippets.Add(new IntelSnippet
                {
                    Text = $"Firma KAP'a kayıtlı: {title}" +
                           (string.IsNullOrWhiteSpace(stock) ? "" : $" (BIST: {stock})") +
                           ". Halka açık şirket veya iştiraki; finansal tablolar KAP'ta yayınlanıyor.",
                    SourceUrl = url,
                    Kind = IntelKind.Finansal
                });

                intel.Links.Add(new IntelLink { Label = "KAP şirket sayfası", Url = url });
                return oid;
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "KAP üye listesi okunamadı.");
            return null;
        }
    }

    /// <summary>Son finansal raporun ozetini (hasilat, kar, ozkaynak) snippet olarak ekle.</summary>
    private async Task TryFinancialsAsync(string oid, SourceIntel intel, CancellationToken ct)
    {
        try
        {
            var fin = await _financials.GetLatestAsync(oid, ct);
            if (fin is null) return;

            intel.Snippets.Add(new IntelSnippet
            {
                Text = fin.ToSummary(),
                SourceUrl = fin.DisclosureUrl,
                Kind = IntelKind.Finansal
            });

            intel.Links.Add(new IntelLink { Label = "KAP son finansal rapor", Url = fin.DisclosureUrl });
        }
        catch (Exception ex)
        {
            _logger.LogInformation(ex, "KAP finansal rapor alınamadı: {Oid}", oid);
        }
    }

    /// <summary>site:kap.org.tr aramasiyla son bildirim sayfalarini yakala.</summary>
    private async Task TryDisclosureSearchAsync(string name, SourceIntel intel, CancellationToken ct)
    {
        try
        {
            var results = await _search.SearchAsync($"site:kap.org.tr \"{name}\"", 5, ct);

            foreach (var r in results.Take(3))
            {
                var text = $"{r.Title}. {r.Snippet}".Trim();
                if (text.Length < 15) continue;

                intel.Snippets.Add(new IntelSnippet
                {
                    Text = "KAP bildirimi: " + text,
                    SourceUrl = r.Url,
                    Kind = IntelKind.Finansal
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "KAP site araması başarısız: {Company}", name);
        }
    }

    private static IEnumerable<JsonElement> EnumerateMembers(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
            return root.EnumerateArray();

        // Bazi cevaplar { "members": [...] } sarmalayabilir.
        foreach (var prop in new[] { "members", "data", "result" })
            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty(prop, out var arr) &&
                arr.ValueKind == JsonValueKind.Array)
                return arr.EnumerateArray();

        return Enumerable.Empty<JsonElement>();
    }

    private static string? Text(JsonElement el, string prop) =>
        el.ValueKind == JsonValueKind.Object &&
        el.TryGetProperty(prop, out var v) &&
        v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;
}
