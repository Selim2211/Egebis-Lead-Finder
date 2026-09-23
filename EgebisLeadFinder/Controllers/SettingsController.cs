using EgebisLeadFinder.Data;
using EgebisLeadFinder.Configuration;
using EgebisLeadFinder.Models;
using EgebisLeadFinder.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace EgebisLeadFinder.Controllers;

/// <summary>
/// API anahtarlari ve lead arama unvanlari gibi calisma zamani ayarlari.
/// Degerler veritabaninda tutulur; uygulamayi yeniden baslatmaya gerek yok.
/// </summary>
public class SettingsController : Controller
{
    private readonly ISettingsService _settings;
    private readonly IConfiguration _configuration;
    private readonly IApiUsageTracker _usage;
    private readonly IExchangeRateService _exchangeRates;
    private readonly AiOptions _aiOptions;
    private readonly SearchOptions _searchOptions;

    public SettingsController(
        ISettingsService settings,
        IConfiguration configuration,
        IApiUsageTracker usage,
        IExchangeRateService exchangeRates,
        IOptions<AiOptions> aiOptions,
        IOptions<SearchOptions> searchOptions)
    {
        _settings = settings;
        _configuration = configuration;
        _usage = usage;
        _exchangeRates = exchangeRates;
        _aiOptions = aiOptions.Value;
        _searchOptions = searchOptions.Value;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        return View(await BuildModelAsync(ct));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(SettingsViewModel form, CancellationToken ct)
    {
        var values = new Dictionary<string, string?>
        {
            [SettingKeys.LeadTitleKeywords] = form.LeadTitleKeywords?.Trim(),
            [SettingKeys.SerperApiKey] = form.SerperApiKey?.Trim(),
            [SettingKeys.GeminiApiKey] = form.GeminiApiKey?.Trim(),
            // Bos veya varsayilanla ayni secim kaydedilmez: appsettings varsayilani gecerli kalir.
            [SettingKeys.GeminiModel] = GeminiModelCatalog.IsValidModelId(form.GeminiModel?.Trim())
                && form.GeminiModel!.Trim() != _aiOptions.GeminiModel
                    ? form.GeminiModel.Trim()
                    : null,
            [SettingKeys.ApolloApiKey] = form.ApolloApiKey?.Trim(),
            [SettingKeys.SerperCreditLimit] = form.SerperCreditLimit > 0
                ? form.SerperCreditLimit.ToString()
                : null,
            [SettingKeys.SearchMaxCompanies] = form.SearchMaxCompanies > 0
                ? Math.Min(form.SearchMaxCompanies, SettingKeys.SearchMaxCompaniesUpperLimit).ToString()
                : null,
            [SettingKeys.SearchRegion] = SearchRegions.IsKnown(form.SearchRegion) ? form.SearchRegion : SearchRegions.DefaultKey,
            // Ulke adi bolgeden turetilir; eski ayar geriye donuk uyum icin guncel tutulur.
            [SettingKeys.SearchDefaultCountry] = SearchRegions.Get(form.SearchRegion).Country,
            [SettingKeys.FollowUpAfterDays] = form.FollowUpAfterDays is > 0 and < 365
                ? form.FollowUpAfterDays.ToString()
                : null,
            [SettingKeys.ExtraBlockedDomains] = SerperSearchService.ParseDomains(form.ExtraBlockedDomains) is { Count: > 0 } d
                ? string.Join(", ", d)
                : null,

            [SettingKeys.SmtpFromAddress] = Clean(form.SmtpFromAddress),
            [SettingKeys.SmtpFromName] = Clean(form.SmtpFromName),
            [SettingKeys.SmtpHost] = Clean(form.SmtpHost),
            [SettingKeys.SmtpPort] = form.SmtpPort is > 0 and < 65536 ? form.SmtpPort.ToString() : null,
            [SettingKeys.SmtpSecurity] = form.SmtpSecurity is "starttls" or "ssl" or "auto" ? form.SmtpSecurity : "starttls",
            [SettingKeys.SmtpUsername] = Clean(form.SmtpUsername)
        };

        // Sifre ekrana hic basilmaz; alan bos gelirse kayitli sifre korunur.
        if (!string.IsNullOrWhiteSpace(form.SmtpPassword))
            values[SettingKeys.SmtpPassword] = form.SmtpPassword.Trim();

        if (!string.IsNullOrWhiteSpace(form.SmtpFromAddress) && !System.Net.Mail.MailAddress.TryCreate(form.SmtpFromAddress.Trim(), out _))
        {
            TempData["SettingsError"] = "Gönderen e-posta adresi geçerli değil; e-posta ayarları kaydedilmedi.";
            foreach (var key in new[] { SettingKeys.SmtpFromAddress, SettingKeys.SmtpFromName, SettingKeys.SmtpHost,
                         SettingKeys.SmtpPort, SettingKeys.SmtpSecurity, SettingKeys.SmtpUsername, SettingKeys.SmtpPassword })
                values.Remove(key);
        }

        await _settings.SetManyAsync(values, ct);

        TempData["SettingsSaved"] = "Ayarlar kaydedildi.";
        return RedirectToAction(nameof(Index));

        static string? Clean(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
    }

    /// <summary>Kayitli SMTP ayarlariyla gonderen adresin KENDISINE deneme e-postasi atar.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendTestEmail([FromServices] IEmailSender sender, CancellationToken ct)
    {
        var s = await sender.GetSettingsAsync(ct);
        if (!s.IsConfigured)
        {
            TempData["SettingsError"] = "Önce gönderen adresi ve SMTP sunucusunu kaydedin.";
            return RedirectToAction(nameof(Index));
        }

        var result = await sender.SendAsync(s.FromAddress!,
            "Egebis Lead Finder — test e-postası",
            "Bu bir deneme e-postasıdır. Bu mesajı görüyorsanız lead'lere e-posta bu adresten gönderilecek.",
            ct: ct);

        if (result.Sent)
            TempData["SettingsSaved"] = $"Test e-postası {s.FromAddress} adresine gönderildi. Gelen kutunuzu kontrol edin.";
        else
            TempData["SettingsError"] = $"Test e-postası gönderilemedi: {result.Error}";

        return RedirectToAction(nameof(Index));
    }

    /// <summary>Yeni Serper API anahtari alindiginda kullanim sayacini sifirlar.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetSerperUsage(CancellationToken ct)
    {
        await _usage.ResetAsync(SerperSearchService.UsageProvider, ct);
        TempData["SettingsSaved"] = "Serper kredi sayacı sıfırlandı.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<SettingsViewModel> BuildModelAsync(CancellationToken ct)
    {
        var stored = await _settings.GetStoredAsync(ct);

        string? Stored(string key) => stored.TryGetValue(key, out var v) ? v : null;

        // Ayar bos ama yapilandirmada (User Secrets/appsettings) deger varsa
        // kullaniciya bunu bildiriyoruz: sistem calisir durumda ama deger
        // ekrandan yonetilmiyor demektir.
        bool FromConfig(string key) =>
            !SettingKeys.IsSettingsOnly(key)
            && string.IsNullOrWhiteSpace(Stored(key)) && !string.IsNullOrWhiteSpace(_configuration[key]);

        var creditLimit = int.TryParse(Stored(SettingKeys.SerperCreditLimit), out var limit) && limit > 0
            ? limit
            : 2500;

        var usage = await _usage.GetAsync(SerperSearchService.UsageProvider, ct);

        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));

        // Gemini cagri basina TL maliyeti kullaniciya sorulmaz: sabit fiyat
        // varsayimlari + canli USD/TRY kuruyla otomatik hesaplanir. Kur
        // alinamazsa son basarili cekimdeki deger (veya makul bir varsayilan) kullanilir.
        var liveRate = await _exchangeRates.GetUsdTryRateAsync(ct);
        var rateIsLive = liveRate is > 0;

        decimal usdTryRate;
        if (rateIsLive)
        {
            usdTryRate = liveRate!.Value;
            await _settings.SetManyAsync(new Dictionary<string, string?>
            {
                [SettingKeys.LastKnownUsdTryRate] = usdTryRate.ToString(System.Globalization.CultureInfo.InvariantCulture)
            }, ct);
        }
        else
        {
            usdTryRate = decimal.TryParse(
                Stored(SettingKeys.LastKnownUsdTryRate),
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                out var cached) && cached > 0 ? cached : 41m;
        }

        var geminiCostPerCall = GeminiCostEstimator.EstimateCostPerCallTry(_aiOptions, usdTryRate);

        var catalog = HttpContext.RequestServices.GetRequiredService<IGeminiModelCatalog>();
        var (geminiModels, geminiModelsError) = await catalog.ListAsync(ct);

        var smtp = await HttpContext.RequestServices.GetRequiredService<IEmailSender>().GetSettingsAsync(ct);

        return new SettingsViewModel
        {
            SmtpFromAddress = smtp.FromAddress,
            SmtpFromName = smtp.FromName,
            SmtpHost = smtp.Host,
            SmtpPort = smtp.Port,
            SmtpSecurity = smtp.Security,
            // Kullanici adi gonderen adresle aynıysa alan bos gosterilir (varsayilan zaten o).
            SmtpUsername = string.Equals(smtp.Username, smtp.FromAddress, StringComparison.OrdinalIgnoreCase)
                ? null : smtp.Username,
            SmtpPasswordSet = !string.IsNullOrWhiteSpace(smtp.Password),
            SmtpConfigured = smtp.IsConfigured,

            LeadTitleKeywords = Stored(SettingKeys.LeadTitleKeywords)
                ?? string.Join(", ", SettingsService.DefaultTitleKeywords),
            SerperApiKey = Stored(SettingKeys.SerperApiKey),
            GeminiApiKey = Stored(SettingKeys.GeminiApiKey),
            ApolloApiKey = Stored(SettingKeys.ApolloApiKey),

            SerperFromConfig = FromConfig(SettingKeys.SerperApiKey),
            GeminiFromConfig = FromConfig(SettingKeys.GeminiApiKey),
            ApolloFromConfig = FromConfig(SettingKeys.ApolloApiKey),

            GeminiModel = Stored(SettingKeys.GeminiModel),
            GeminiCurrentModel = await catalog.GetCurrentModelAsync(ct),
            GeminiDefaultModel = _aiOptions.GeminiModel,
            GeminiModels = geminiModels.ToList(),
            GeminiModelsError = geminiModelsError,

            SearchMaxCompanies = int.TryParse(Stored(SettingKeys.SearchMaxCompanies), out var maxCompanies) && maxCompanies > 0
                ? maxCompanies
                : SettingKeys.DefaultSearchMaxCompanies,
            SearchRegion = (await CompanyController.GetRegionAsync(_settings, ct)).Key,
            SearchDefaultCountry = Stored(SettingKeys.SearchDefaultCountry) ?? SettingKeys.DefaultSearchCountry,
            FollowUpAfterDays = await LeadController.FollowUpDaysAsync(_settings, ct),
            ExtraBlockedDomains = Stored(SettingKeys.ExtraBlockedDomains),

            SerperCreditLimit = creditLimit,
            SerperCallsThisMonth = await _usage.GetRangeCountAsync(
                SerperSearchService.UsageProvider, new DateOnly(today.Year, today.Month, 1), today, ct),
            SerperMonthlyCap = _searchOptions.MonthlyCreditCap,
            SerperCreditsUsed = usage.Count,
            SerperUsageResetAt = usage.ResetAt,

            GeminiCallsToday = await _usage.GetRangeCountAsync(GeminiAiService.UsageProvider, today, today, ct),
            GeminiCallsThisWeek = await _usage.GetRangeCountAsync(
                GeminiAiService.UsageProvider, today.AddDays(-6), today, ct),
            GeminiCallsThisMonth = await _usage.GetRangeCountAsync(
                GeminiAiService.UsageProvider, new DateOnly(today.Year, today.Month, 1), today, ct),
            GeminiCostPerCallTry = geminiCostPerCall,
            UsdTryRate = usdTryRate,
            UsdTryRateIsLive = rateIsLive,

            StatusMessage = TempData["SettingsSaved"] as string
        };
    }
}
