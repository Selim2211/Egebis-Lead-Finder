using System.Text.Json;
using EgebisLeadFinder.Data;
using EgebisLeadFinder.Models;

namespace EgebisLeadFinder.Services;

/// <summary>AI #6: sirketin kendi sitesinden "Biz ne arıyoruz?" taslagi. GeminiAiService uygular.</summary>
public interface IBusinessProfileAi
{
    Task<BusinessProfileDraft> DraftBusinessProfileAsync(string siteUrl, string siteText, CancellationToken ct = default);
}

/// <summary>
/// "Biz ne arıyoruz?" sirket profilini okur/yazar, sitemizden yapay zeka taslagi cikarir ve
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
            return JsonSerializer.Deserialize<BusinessProfile>(raw, JsonOptions) ?? new BusinessProfile();
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

    /// <summary>
    /// Segmentlerdeki NACE kodlari, sektor kelimeleri ve eleme kelimelerini ICP'ye ekler
    /// (mevcut ICP degerleri korunur). Eklenen yeni oge sayisini doner.
    /// </summary>
    public async Task<int> ApplySegmentsToIcpAsync(BusinessProfile profile, CancellationToken ct = default)
    {
        var icp = await _icp.GetAsync(ct);
        var added = 0;

        added += Merge(icp.NaceCodes, profile.Segments.SelectMany(s => s.NaceCodes).Select(NaceCatalog.Normalize).OfType<string>());
        added += Merge(icp.IndustryKeywords, profile.Segments.SelectMany(s => s.Keywords));
        added += Merge(icp.ExcludeKeywords, profile.Segments.SelectMany(s => s.ExcludeKeywords));

        if (added > 0) await _icp.SaveAsync(icp, ct);
        return added;
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

        p.Segments = p.Segments
            .Where(s => !string.IsNullOrWhiteSpace(s.Name))
            .Take(BusinessProfile.MaxSegments)
            .ToList();

        var ids = new HashSet<string>();
        foreach (var s in p.Segments)
        {
            if (string.IsNullOrWhiteSpace(s.Id) || !ids.Add(s.Id)) { s.Id = Guid.NewGuid().ToString("N")[..8]; ids.Add(s.Id); }
            s.Name = Clip(s.Name, 100)!;
            s.Description = Clip(s.Description, 1000);
            s.SearchTerm = Clip(s.SearchTerm, 150);
            s.RegionKey = SearchRegions.IsKnown(s.RegionKey) ? s.RegionKey : null;
            s.Keywords = CleanList(s.Keywords).Take(30).ToList();
            s.NaceCodes = CleanList(s.NaceCodes.Select(n => NaceCatalog.Normalize(n) ?? string.Empty)).Take(30).ToList();
            s.ExcludeKeywords = CleanList(s.ExcludeKeywords).Take(30).ToList();
            s.TargetTitles = CleanList(s.TargetTitles).Take(30).ToList();
        }
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
