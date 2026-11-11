using System.Text.Json;
using EgebisLeadFinder.Data;
using EgebisLeadFinder.Models;

namespace EgebisLeadFinder.Services;

/// <summary>AI #6: sirketin kendi sitesinden "Şirketimiz" taslagi. GeminiAiService uygular.</summary>
public interface IBusinessProfileAi
{
    Task<BusinessProfileDraft> DraftBusinessProfileAsync(string siteUrl, string siteText, CancellationToken ct = default);
}

/// <summary>
/// "Şirketimiz" sirket profilini okur/yazar, sitemizden yapay zeka taslagi cikarir ve
/// segmentleri ICP'ye aktarir.
/// </summary>
public class BusinessProfileService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly ISettingsService _settings;
    private readonly IcpService _icp;
    private readonly IWebScraperService _scraper;
    private readonly IBusinessProfileAi _ai;

    public BusinessProfileService(ISettingsService settings, IcpService icp, IWebScraperService scraper, IBusinessProfileAi ai)
    {
        _settings = settings;
        _icp = icp;
        _scraper = scraper;
        _ai = ai;
    }

    public async Task<BusinessProfile> GetAsync(CancellationToken ct = default) =>
        Parse(await _settings.GetAsync(SettingKeys.BusinessProfile, ct));

    /// <summary>Ayar degerini cozer; bos veya bozuksa bos profil (eski sabit tanimlar gecerli kalir).</summary>
    public static BusinessProfile Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new BusinessProfile();
        try
        {
            var profile = JsonSerializer.Deserialize<BusinessProfile>(raw, JsonOptions) ?? new BusinessProfile();
            // Hedef segmentler kaldirildi: eski kayitlarda kalanlar hicbir yerde kullanilmaz.
            profile.Segments = new List<TargetSegment>();
            return profile;
        }
        catch (JsonException)
        {
            return new BusinessProfile();
        }
    }

    /// <summary>Profili temizleyip kaydeder; unvanlar verildiyse Ayarlar'daki lead unvanlarini da gunceller.</summary>
    public async Task SaveAsync(BusinessProfile profile, IEnumerable<string>? targetTitles, string? updatedBy, CancellationToken ct = default)
    {
        Normalize(profile);
        profile.UpdatedAt = DateTime.UtcNow;
        profile.UpdatedBy = updatedBy;

        var values = new Dictionary<string, string?>
        {
            [SettingKeys.BusinessProfile] = JsonSerializer.Serialize(profile)
        };

        if (targetTitles is not null)
        {
            var titles = CleanList(targetTitles);
            // Bos birakilirsa unvan filtresi uygulanmaz: firmadaki tum kisiler listelenir.
            values[SettingKeys.LeadTitleKeywords] = titles.Count == 0 ? SettingsService.AllPeopleMarker : string.Join(", ", titles);
        }

        await _settings.SetManyAsync(values, ct);
    }

    /// <summary>Sirket sitesini okuyup yapay zekaya taslak profil cikartir (kaydetmez).</summary>
    public async Task<BusinessProfileDraft> DraftFromWebsiteAsync(string website, CancellationToken ct = default)
    {
        var url = NormalizeUrl(website);
        if (url is null) return new BusinessProfileDraft { Error = "Geçerli bir web sitesi adresi girin (ör. egebis.com)." };

        var site = await _scraper.ScrapeAsync(url, ct);
        if (!site.Success)
            return new BusinessProfileDraft { Error = $"Site okunamadı: {site.Error ?? "içerik bulunamadı"}" };

        var draft = await _ai.DraftBusinessProfileAsync(url, site.Text, ct);
        if (draft.Success)
        {
            draft.Profile.Website = url;
            Normalize(draft.Profile);
        }
        return draft;
    }

    public static string? NormalizeUrl(string? website)
    {
        var value = website?.Trim();
        if (string.IsNullOrEmpty(value)) return null;
        if (!value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            value = "https://" + value;

        return Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Host.Contains('.')
            ? uri.GetLeftPart(UriPartial.Path).TrimEnd('/')
            : null;
    }

    public static void Normalize(BusinessProfile p)
    {
        p.CompanyName = Clip(p.CompanyName, 150) ?? string.Empty;
        p.Website = Clip(p.Website, 300);
        p.Offering = Clip(p.Offering, 2000) ?? string.Empty;
        p.ProblemsWeSolve = Clip(p.ProblemsWeSolve, 2000);
        p.IdealCustomer = Clip(p.IdealCustomer, 2000) ?? string.Empty;
        p.NotCustomers = Clip(p.NotCustomers, 2000);
        p.Competitors = Clip(p.Competitors, 2000);
        p.ExampleCustomers = CleanList(p.ExampleCustomers).Take(20).ToList();
        p.CustomerKind = p.CustomerKind is BusinessProfile.KindManufacturer or BusinessProfile.KindAny ? p.CustomerKind : null;
        p.BuyingSignals = CleanList(p.BuyingSignals).Take(20).ToList();
        p.ScoringSignals = ScoringSignal.Clean(p.ScoringSignals);

        // Hedef segmentler kaldirildi: eski kayitlardan gelenler arama, puan ve maillerde kullanilmaz.
        p.Segments = new List<TargetSegment>();
    }

    public static List<string> CleanList(IEnumerable<string?> items) =>
        items.Select(i => i?.Trim() ?? string.Empty)
            .Where(i => i.Length > 0)
            .Select(i => i.Length > 150 ? i[..150] : i)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static int Merge(List<string> target, IEnumerable<string> additions)
    {
        var added = 0;
        foreach (var item in CleanList(additions))
        {
            if (target.Contains(item, StringComparer.OrdinalIgnoreCase)) continue;
            target.Add(item);
            added++;
        }
        return added;
    }

    private static string? Clip(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim();
        return value.Length > max ? value[..max] : value;
    }
}
