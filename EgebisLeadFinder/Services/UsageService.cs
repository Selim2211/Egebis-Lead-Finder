using System.Net.Http.Json;
using System.Text.Json;
using EgebisLeadFinder.Models;
using Microsoft.Extensions.Caching.Memory;

namespace EgebisLeadFinder.Services;

public enum UsageLevel { Unknown, Normal, Warning, Critical, Exhausted }

/// <summary>Bir API saglayicisinin kullanim ozeti (API Kullanimi ekranindaki kart).</summary>
public class ProviderUsage
{
    public string Key { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Purpose { get; init; } = string.Empty;
    public bool KeyConfigured { get; init; }

    /// <summary>Sayilan birim: "kredi", "çağrı".</summary>
    public string Unit { get; init; } = "çağrı";

    public int Today { get; init; }
    public int ThisWeek { get; init; }
    public int ThisMonth { get; init; }
    public int SinceReset { get; init; }
    public DateTime? ResetAt { get; init; }

    /// <summary>Limit ve limite gore kullanim (0 = limit tanimsiz).</summary>
    public int Limit { get; init; }
    public int Used { get; init; }
    public string LimitLabel { get; init; } = string.Empty;

    public int Remaining => Limit > 0 ? Math.Max(0, Limit - Used) : 0;
    public int Percent => Limit > 0 ? Math.Clamp((int)Math.Round(100.0 * Used / Limit), 0, 100) : 0;
    public UsageLevel Level { get; init; }

    public DateTime? LastErrorAt { get; init; }
    public string? LastError { get; init; }

    public int[] Last30Days { get; init; } = Array.Empty<int>();

    /// <summary>Saglayicidan canli okunan bakiye (yalnizca Apollo destekliyor).</summary>
    public string? LiveInfo { get; init; }
    public string? Note { get; init; }
    public decimal? EstimatedCostTry { get; init; }

    /// <summary>Maliyetin nasil hesaplandigi (kur, gercek/tahmini cagri, model).</summary>
    public string? CostNote { get; init; }
}

/// <summary>Apollo'nun canli kredi durumu (credit_usage_stats).</summary>
public record ApolloCreditStatus(bool Ok, string? Error, int? Limit, int? Consumed, int? LeftOver,
    DateTime? CycleStart, DateTime? CycleEnd, DateTime CheckedAt);

/// <summary>
/// Serper, Gemini ve Apollo icin kullanim, limit ve uyari seviyesi. Serper ve Gemini'nin
/// bakiye sorgulayan bir API'si yok; yerel sayac kullanilir. Apollo kredi durumu, ana
/// (master) anahtar varsa canli okunur.
/// </summary>
public class UsageService
{
    public const int WarningPercent = 80;
    public const int CriticalPercent = 95;
    private const string ApolloCacheKey = "usage:apollo-credits";
    private const string AlertCacheKey = "usage:alert";

    private readonly IApiUsageTracker _usage;
    private readonly ISettingsService _settings;
    private readonly IMemoryCache _cache;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<UsageService> _logger;
    private readonly Configuration.AiOptions _aiOptions;

    public UsageService(IApiUsageTracker usage, ISettingsService settings, IMemoryCache cache,
        IHttpClientFactory httpFactory, ILogger<UsageService> logger, Microsoft.Extensions.Options.IOptions<Configuration.AiOptions> aiOptions)
    {
        _aiOptions = aiOptions.Value;
        _usage = usage;
        _settings = settings;
        _cache = cache;
        _httpFactory = httpFactory;
        _logger = logger;
    }

    /// <summary>Limite gore uyari seviyesi: %80 uyari, %95 kritik, %100 doldu. Limit yoksa bilinmiyor.</summary>
    public static UsageLevel Evaluate(int used, int limit)
    {
        if (limit <= 0) return UsageLevel.Unknown;
        var pct = 100.0 * used / limit;
        return pct >= 100 ? UsageLevel.Exhausted
            : pct >= CriticalPercent ? UsageLevel.Critical
            : pct >= WarningPercent ? UsageLevel.Warning
            : UsageLevel.Normal;
    }

    /// <summary>Son 24 saatte kota hatasi geldiyse limit tanimsiz olsa da \"doldu\" sayilir.</summary>
    public static UsageLevel Combine(UsageLevel byLimit, DateTime? lastErrorAt, DateTime nowUtc) =>
        lastErrorAt is { } at && nowUtc - at < TimeSpan.FromHours(24) ? UsageLevel.Exhausted : byLimit;

    private static DateOnly TurkeyToday => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));

    public async Task<List<ProviderUsage>> GetAllAsync(CancellationToken ct = default)
    {
        var today = TurkeyToday;
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var now = DateTime.UtcNow;

        async Task<(int Today, int Week, int Month, ApiUsage Row, int[] Series)> Counts(string provider) => (
            await _usage.GetRangeCountAsync(provider, today, today, ct),
            await _usage.GetRangeCountAsync(provider, today.AddDays(-6), today, ct),
            await _usage.GetRangeCountAsync(provider, monthStart, today, ct),
            await _usage.GetAsync(provider, ct),
            await _usage.GetDailySeriesAsync(provider, 30, ct));

        int Setting(string? raw, int fallback) => int.TryParse(raw, out var v) && v > 0 ? v : fallback;

        // --- Serper: toplam kredi (anahtar basina), son sifirlamadan beri sayilir.
        var serper = await Counts(SerperSearchService.UsageProvider);
        var serperLimit = Setting(await _settings.GetAsync(SettingKeys.SerperCreditLimit, ct), 2500);
        var serperItem = new ProviderUsage
        {
            Key = "serper",
            Name = "Serper (Google arama)",
            Purpose = "Firma arama, LinkedIn ve haber taraması",
            KeyConfigured = !string.IsNullOrWhiteSpace(await _settings.GetAsync(SettingKeys.SerperApiKey, ct)),
            Unit = "kredi",
            Today = serper.Today, ThisWeek = serper.Week, ThisMonth = serper.Month,
            SinceReset = serper.Row.Count, ResetAt = serper.Row.ResetAt,
            Limit = serperLimit, Used = serper.Row.Count, LimitLabel = "toplam kredi (anahtar başına)",
            Level = Combine(Evaluate(serper.Row.Count, serperLimit), serper.Row.LastErrorAt, now),
            LastErrorAt = serper.Row.LastErrorAt, LastError = serper.Row.LastError,
            Last30Days = serper.Series,
            Note = "Serper bakiye sorgulama API'si sunmuyor; sayaç uygulamanın yaptığı çağrılardan hesaplanır. Önbellekten dönen aramalar kredi harcamaz."
        };

        // --- Gemini: aylik cagri limiti (Ayarlar'dan); ucretsiz katmanda gunluk kota da vardir.
        var gemini = await Counts(GeminiAiService.UsageProvider);
        var geminiLimit = Setting(await _settings.GetAsync(SettingKeys.GeminiMonthlyLimit, ct), 0);
        var usdTry = GeminiPricing.ParseRate(await _settings.GetAsync(SettingKeys.UsdTryRate, ct));
        var currentModel = await _settings.GetAsync(SettingKeys.GeminiModel, ct) ?? _aiOptions.GeminiModel;
        var monthCost = GeminiCostCalculator.Compute(await _usage.GetGeminiTokensAsync(monthStart, today, ct), gemini.Month, usdTry,
            _aiOptions, currentModel, today);
        var geminiItem = new ProviderUsage
        {
            Key = "gemini",
            Name = "Google Gemini (yapay zekâ)",
            Purpose = "Site analizi, firma analizi, AI ile mail, NACE",
            KeyConfigured = !string.IsNullOrWhiteSpace(await _settings.GetAsync(SettingKeys.GeminiApiKey, ct)),
            Today = gemini.Today, ThisWeek = gemini.Week, ThisMonth = gemini.Month,
            SinceReset = gemini.Row.Count, ResetAt = gemini.Row.ResetAt,
            Limit = geminiLimit, Used = gemini.Month, LimitLabel = "aylık çağrı limiti",
            Level = Combine(Evaluate(gemini.Month, geminiLimit), gemini.Row.LastErrorAt, now),
            LastErrorAt = gemini.Row.LastErrorAt, LastError = gemini.Row.LastError,
            Last30Days = gemini.Series,
            EstimatedCostTry = monthCost.Try,
            CostNote = $"1 USD = {usdTry:0.##} TL · {monthCost.MeasuredCalls} çağrı gerçek token sayısıyla (girdi {monthCost.InputTokens:N0}, çıktı {monthCost.OutputTokens:N0})"
                + (monthCost.EstimatedCalls > 0 ? $", {monthCost.EstimatedCalls} çağrı (token kaydı öncesi) tahmini" : string.Empty)
                + $" · model: {currentModel}",
            Note = geminiLimit == 0
                ? "Aylık limit tanımlı değil; Ayarlar'dan girerseniz yüzde ve uyarı gösterilir. Google bakiye sorgulama API'si sunmuyor."
                : "Google bakiye sorgulama API'si sunmuyor; sayaç uygulamanın yaptığı çağrılardan hesaplanır."
        };

        // --- Apollo: e-posta acma kredisi (aylik), canli bakiye varsa o esas alinir.
        var apollo = await Counts(ApolloPersonEmailFinder.UsageProvider);
        var credits = await Counts(ApolloPersonEmailFinder.CreditUsageProvider);
        var live = _cache.Get<ApolloCreditStatus>(ApolloCacheKey);
        var apolloLimit = live is { Ok: true, Limit: > 0 } ? live.Limit!.Value
            : Setting(await _settings.GetAsync(SettingKeys.ApolloCreditLimit, ct), 0);
        var apolloUsed = live is { Ok: true, Consumed: not null } ? live.Consumed!.Value : credits.Month;
        var apolloLastError = new[] { apollo.Row.LastErrorAt, credits.Row.LastErrorAt }.Max();
        var apolloItem = new ProviderUsage
        {
            Key = "apollo",
            Name = "Apollo.io (kişi ve e-posta)",
            Purpose = "Karar verici arama ve e-posta açma",
            KeyConfigured = !string.IsNullOrWhiteSpace(await _settings.GetAsync(SettingKeys.ApolloApiKey, ct)),
            Unit = "kredi",
            Today = credits.Today, ThisWeek = credits.Week, ThisMonth = credits.Month,
            SinceReset = apollo.Row.Count, ResetAt = apollo.Row.ResetAt,
            Limit = apolloLimit, Used = apolloUsed,
            LimitLabel = live is { Ok: true } ? "dönem kredisi (Apollo'dan canlı)" : "aylık kredi limiti",
            Level = Combine(Evaluate(apolloUsed, apolloLimit), apolloLastError, now),
            LastErrorAt = apolloLastError, LastError = apollo.Row.LastError ?? credits.Row.LastError,
            Last30Days = credits.Series,
            LiveInfo = live switch
            {
                null => null,
                { Ok: true } => $"Apollo'dan okundu ({live.CheckedAt.ToLocalTime():dd.MM HH:mm}): {live.LeftOver} kredi kaldı" +
                                (live.CycleEnd is { } end ? $", dönem sonu {end.ToLocalTime():dd.MM.yyyy}" : ""),
                _ => $"Apollo'dan okunamadı: {live.Error}"
            },
            Note = $"Bu ay {apollo.Month} Apollo çağrısı yapıldı (kişi araması + e-posta açma); kredi sayacı yalnızca e-postası açılan kişileri sayar."
        };

        return new List<ProviderUsage> { serperItem, geminiItem, apolloItem };
    }

    /// <summary>Ust menu ve Panel icin: en kotu seviye (60 sn onbellek).</summary>
    public async Task<(UsageLevel Level, List<ProviderUsage> Items)> GetAlertAsync(CancellationToken ct = default)
    {
        if (_cache.TryGetValue(AlertCacheKey, out (UsageLevel, List<ProviderUsage>) cached)) return cached;

        var items = (await GetAllAsync(ct)).Where(i => i.Level >= UsageLevel.Warning).ToList();
        var level = items.Count == 0 ? UsageLevel.Normal : items.Max(i => i.Level);
        var result = (level, items);
        _cache.Set(AlertCacheKey, result, TimeSpan.FromSeconds(60));
        return result;
    }

    /// <summary>Apollo kredi durumunu canli okur. Ana (master) anahtar gerekir; degilse 403 doner.</summary>
    public async Task<ApolloCreditStatus> RefreshApolloAsync(CancellationToken ct = default)
    {
        var key = await _settings.GetAsync(SettingKeys.ApolloApiKey, ct);
        ApolloCreditStatus status;

        if (string.IsNullOrWhiteSpace(key))
        {
            status = new ApolloCreditStatus(false, "Apollo API anahtarı girilmemiş.", null, null, null, null, null, DateTime.UtcNow);
        }
        else
        {
            try
            {
                using var http = _httpFactory.CreateClient();
                http.Timeout = TimeSpan.FromSeconds(10);
                using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.apollo.io/api/v1/usage_stats/credit_usage_stats");
                request.Headers.Add("x-api-key", key);
                request.Content = JsonContent.Create(new { });

                using var response = await http.SendAsync(request, ct);
                var body = await response.Content.ReadAsStringAsync(ct);
                status = response.IsSuccessStatusCode
                    ? ParseApolloCredits(body, DateTime.UtcNow)
                    : new ApolloCreditStatus(false,
                        (int)response.StatusCode == 403
                            ? "Apollo bu bilgiyi yalnızca ana (master) API anahtarıyla veriyor (HTTP 403)."
                            : $"HTTP {(int)response.StatusCode}", null, null, null, null, null, DateTime.UtcNow);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                _logger.LogWarning(ex, "Apollo kredi durumu okunamadı.");
                status = new ApolloCreditStatus(false, "Apollo'ya ulaşılamadı.", null, null, null, null, null, DateTime.UtcNow);
            }
        }

        _cache.Set(ApolloCacheKey, status, TimeSpan.FromHours(6));
        _cache.Remove(AlertCacheKey);
        return status;
    }

    /// <summary>credit_usage_stats yaniti: e-posta acma \"lead_credit\" (yoksa export_credit) turunden duser.</summary>
    public static ApolloCreditStatus ParseApolloCredits(string body, DateTime nowUtc)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        if (!root.TryGetProperty("credit_usage_stats", out var stats) || stats.ValueKind != JsonValueKind.Object)
            return new ApolloCreditStatus(false, "Beklenmeyen yanıt.", null, null, null, null, null, nowUtc);

        JsonElement credit = default;
        foreach (var name in new[] { "lead_credit", "export_credit" })
            if (stats.TryGetProperty(name, out credit) && credit.ValueKind == JsonValueKind.Object) break;

        if (credit.ValueKind != JsonValueKind.Object)
            return new ApolloCreditStatus(false, "Kredi türü bulunamadı.", null, null, null, null, null, nowUtc);

        static int? Int(JsonElement e, string p) =>
            e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;
        static DateTime? Date(JsonElement e, string p) =>
            e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String && DateTime.TryParse(v.GetString(),
                System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal, out var d) ? d : null;

        DateTime? start = null, end = null;
        if (root.TryGetProperty("current_credit_cycle", out var cycle) && cycle.ValueKind == JsonValueKind.Object)
        {
            start = Date(cycle, "start_date");
            end = Date(cycle, "end_date");
        }

        return new ApolloCreditStatus(true, null, Int(credit, "limit"), Int(credit, "consumed"), Int(credit, "left_over"),
            start, end, nowUtc);
    }
}
