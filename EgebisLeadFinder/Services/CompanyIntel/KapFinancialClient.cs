using System.Globalization;
using System.Text;
using System.Text.Json;
using EgebisLeadFinder.Configuration;
using Microsoft.Extensions.Options;

namespace EgebisLeadFinder.Services.CompanyIntel;

/// <summary>
/// KAP'ta kayitli bir firma icin son finansal raporu bulur ve — mumkunse —
/// temel kalemleri (hasilat, donem kari, ozkaynak, toplam varlik) cikarir.
///
/// KAP'in bildirim/finansal rapor uclari resmi olarak dokumante edilmemistir ve
/// yapisi degisebilir. Bu yuzden istemci iki kademeli calisir:
///   1) Son "Finansal Rapor" bildiriminin basligi + tarihi + linki (her zaman uretilebilir)
///   2) Rapor icinden sayisal kalemler (best-effort; JSON yapisi taninmazsa atlanir)
/// Ikinci kademe basarisiz olursa birinci kademe yine degerli veridir.
/// </summary>
public class KapFinancialClient
{
    private readonly HttpClient _http;
    private readonly ResearchOptions _options;
    private readonly ILogger<KapFinancialClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    // XBRL / KAP taksonomisinde bu kavramlara denk gelen etiket parcalari.
    private static readonly (string Field, string[] Labels)[] ConceptLabels =
    {
        ("Revenue", new[] { "hasılat", "hasilat", "satış gelirleri", "gelirler", "revenue" }),
        ("NetProfit", new[] { "dönem kârı", "dönem karı", "dönem zararı", "net dönem", "net kâr", "net kar", "profit loss", "profitloss" }),
        ("Equity", new[] { "toplam özkaynaklar", "özkaynaklar", "ozkaynaklar", "equity" }),
        ("TotalAssets", new[] { "toplam varlıklar", "toplam varliklar", "aktif toplamı", "assets" }),
    };

    public KapFinancialClient(
        HttpClient http,
        IOptions<ResearchOptions> options,
        ILogger<KapFinancialClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<KapFinancials?> GetLatestAsync(string memberOid, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(memberOid)) return null;

        var disclosure = await FindLatestFinancialDisclosureAsync(memberOid, ct);
        if (disclosure is null) return null;

        var result = new KapFinancials
        {
            Period = disclosure.Value.Period,
            Title = disclosure.Value.Title,
            DisclosureUrl = BuildDisclosureUrl(disclosure.Value.Index)
        };

        await TryFillFiguresAsync(disclosure.Value.Index, result, ct);
        return result;
    }

    /// <summary>Uye icin son "Finansal Rapor" bildirimini bulur.</summary>
    private async Task<(string Index, string Title, string? Period)?> FindLatestFinancialDisclosureAsync(
        string memberOid, CancellationToken ct)
    {
        try
        {
            var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
            var from = DateTime.UtcNow.AddYears(-2).ToString("yyyy-MM-dd");

            // KAP bildirim sorgusu: FR = Finansal Rapor sinifi.
            var body = JsonSerializer.Serialize(new
            {
                fromDate = from,
                toDate = today,
                disclosureClass = "FR",
                mkkMemberOidList = new[] { memberOid },
                memberType = "IGS",
                fromSrc = "N"
            });

            using var request = new HttpRequestMessage(HttpMethod.Post, _options.KapDisclosureQueryUrl)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };

            using var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            var items = EnumerateArray(doc.RootElement, "disclosures", "result", "data");

            (string Index, string Title, string? Period, DateTime When)? best = null;

            foreach (var item in items)
            {
                var index = Str(item, "disclosureIndex") ?? Str(item, "index") ?? Str(item, "id");
                if (string.IsNullOrWhiteSpace(index)) continue;

                var title = Str(item, "kapTitle") ?? Str(item, "title") ?? Str(item, "summary") ?? "Finansal Rapor";
                var when = ParseDate(Str(item, "publishDate") ?? Str(item, "disclosureDate") ?? Str(item, "date"));
                var period = Str(item, "period") ?? Str(item, "ratioPeriod") ?? ExtractPeriod(title);

                if (best is null || when > best.Value.When)
                    best = (index, title, period, when);
            }

            return best is null ? null : (best.Value.Index, best.Value.Title, best.Value.Period);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "KAP bildirim sorgusu başarısız: {Oid}", memberOid);
            return null;
        }
    }

    /// <summary>Rapor JSON'indan taninabilen sayisal kalemleri doldurur (best-effort).</summary>
    private async Task TryFillFiguresAsync(string disclosureIndex, KapFinancials result, CancellationToken ct)
    {
        var url = _options.KapFinancialReportUrlTemplate.Replace("{index}", disclosureIndex);

        try
        {
            using var response = await _http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode) return;

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            // Rapor icindeki tum (etiket, sayi) ciftlerini topla, sonra kavramlara esle.
            var pairs = new List<(string Label, decimal Value)>();
            CollectLabelValuePairs(doc.RootElement, pairs);

            foreach (var (field, labels) in ConceptLabels)
            {
                var hit = pairs.FirstOrDefault(p => labels.Any(l =>
                    p.Label.Contains(l, StringComparison.OrdinalIgnoreCase)));

                if (hit == default || hit.Value == 0) continue;

                switch (field)
                {
                    case "Revenue": result.Revenue ??= hit.Value; break;
                    case "NetProfit": result.NetProfit ??= hit.Value; break;
                    case "Equity": result.Equity ??= hit.Value; break;
                    case "TotalAssets": result.TotalAssets ??= hit.Value; break;
                }
            }
        }
        catch (Exception ex)
        {
            // Ikinci kademe opsiyonel; basarisiz olursa baslik + link yine doner.
            _logger.LogInformation(ex, "KAP finansal rapor ayrıştırılamadı, başlık+link ile devam.");
        }
    }

    /// <summary>
    /// Herhangi bir JSON yapisini gezip {etiket metni + sayi degeri} iceren
    /// objeleri toplar. KAP'in tam sema sekli bilinmedigi icin genel amacli.
    /// </summary>
    private static void CollectLabelValuePairs(JsonElement el, List<(string, decimal)> sink, int depth = 0)
    {
        if (depth > 12) return;

        switch (el.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var child in el.EnumerateArray())
                    CollectLabelValuePairs(child, sink, depth + 1);
                break;

            case JsonValueKind.Object:
                string? label = null;
                decimal? value = null;

                foreach (var prop in el.EnumerateObject())
                {
                    var n = prop.Name.ToLowerInvariant();

                    if (label is null &&
                        (n.Contains("label") || n.Contains("description") || n.Contains("itemname") ||
                         n.Contains("caption") || n == "name" || n == "text") &&
                        prop.Value.ValueKind == JsonValueKind.String)
                        label = prop.Value.GetString();

                    if (value is null &&
                        (n.Contains("value") || n.Contains("amount") || n.Contains("currentperiod") ||
                         n.Contains("current") || n.Contains("tutar")) &&
                        TryNumber(prop.Value, out var num))
                        value = num;
                }

                if (label is not null && value is not null)
                    sink.Add((label, value.Value));

                // Ic objeleri de gez.
                foreach (var prop in el.EnumerateObject())
                    CollectLabelValuePairs(prop.Value, sink, depth + 1);
                break;
        }
    }

    private static bool TryNumber(JsonElement el, out decimal value)
    {
        value = 0;

        if (el.ValueKind == JsonValueKind.Number && el.TryGetDecimal(out value))
            return true;

        if (el.ValueKind == JsonValueKind.String)
        {
            var raw = el.GetString()?.Replace(".", "").Replace(",", ".").Trim();
            return decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out value);
        }

        return false;
    }

    private string BuildDisclosureUrl(string index) =>
        _options.KapDisclosureUrlTemplate.Contains("{index}", StringComparison.Ordinal)
            ? _options.KapDisclosureUrlTemplate.Replace("{index}", index)
            : "https://www.kap.org.tr";

    private static IEnumerable<JsonElement> EnumerateArray(JsonElement root, params string[] wrapperProps)
    {
        if (root.ValueKind == JsonValueKind.Array)
            return root.EnumerateArray();

        foreach (var prop in wrapperProps)
            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty(prop, out var arr) &&
                arr.ValueKind == JsonValueKind.Array)
                return arr.EnumerateArray();

        return Enumerable.Empty<JsonElement>();
    }

    private static string? Str(JsonElement el, string prop) =>
        el.ValueKind == JsonValueKind.Object &&
        el.TryGetProperty(prop, out var v) &&
        v.ValueKind is JsonValueKind.String or JsonValueKind.Number
            ? v.ToString()
            : null;

    private static DateTime ParseDate(string? s) =>
        DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : DateTime.MinValue;

    /// <summary>"... 2024/12 ..." gibi bir donem ifadesini basliktan cikarir.</summary>
    private static string? ExtractPeriod(string title)
    {
        var m = System.Text.RegularExpressions.Regex.Match(title, @"\b(20[0-3]\d)[/\-.\s]*(3|6|9|12)\b");
        return m.Success ? $"{m.Groups[1].Value}/{m.Groups[2].Value}" : null;
    }
}

/// <summary>KAP son finansal raporundan cikarilan ozet. Sayilar TL, bulunamayanlar null.</summary>
public class KapFinancials
{
    public string? Period { get; set; }
    public string Title { get; set; } = "Finansal Rapor";
    public string DisclosureUrl { get; set; } = "https://www.kap.org.tr";

    public decimal? Revenue { get; set; }
    public decimal? NetProfit { get; set; }
    public decimal? Equity { get; set; }
    public decimal? TotalAssets { get; set; }

    public bool HasFigures => Revenue is not null || NetProfit is not null
                              || Equity is not null || TotalAssets is not null;

    /// <summary>Insan tarafindan okunabilir ozet cumle.</summary>
    public string ToSummary()
    {
        var parts = new List<string>();
        if (Revenue is not null) parts.Add($"Hasılat {Money(Revenue.Value)}");
        if (NetProfit is not null) parts.Add($"Net Dönem Kârı/Zararı {Money(NetProfit.Value)}");
        if (Equity is not null) parts.Add($"Özkaynak {Money(Equity.Value)}");
        if (TotalAssets is not null) parts.Add($"Toplam Varlık {Money(TotalAssets.Value)}");

        var period = string.IsNullOrWhiteSpace(Period) ? "" : $" ({Period})";

        return parts.Count > 0
            ? $"KAP finansal raporu{period}: {string.Join(", ", parts)}."
            : $"KAP son finansal rapor{period}: {Title}.";
    }

    private static string Money(decimal v)
    {
        var abs = Math.Abs(v);
        return abs switch
        {
            >= 1_000_000_000 => $"{v / 1_000_000_000:0.##} milyar TL",
            >= 1_000_000 => $"{v / 1_000_000:0.##} milyon TL",
            >= 1_000 => $"{v / 1_000:0.##} bin TL",
            _ => $"{v:0.##} TL"
        };
    }
}
