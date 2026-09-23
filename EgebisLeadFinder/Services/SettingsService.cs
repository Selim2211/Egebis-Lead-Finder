using EgebisLeadFinder.Data;
using EgebisLeadFinder.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace EgebisLeadFinder.Services;

public interface ISettingsService
{
    /// <summary>
    /// Ayari once veritabanindan okur; orada bos ise appsettings/User Secrets
    /// degerine duser. Boylece mevcut yapilandirma calismaya devam eder,
    /// Ayarlar ekranindan girilen deger onu ezer.
    /// </summary>
    Task<string?> GetAsync(string key, CancellationToken ct = default);

    /// <summary>Yalnizca veritabanindaki degeri doner (Ayarlar ekrani icin).</summary>
    Task<Dictionary<string, string?>> GetStoredAsync(CancellationToken ct = default);

    Task SetManyAsync(IDictionary<string, string?> values, CancellationToken ct = default);

    /// <summary>Lead ararken kullanilacak unvan anahtar kelimeleri.</summary>
    Task<List<string>> GetTitleKeywordsAsync(CancellationToken ct = default);
}

/// <summary>
/// Ayarlari veritabanindan okur ve onbellekte tutar.
/// Singleton'dir: firma analizleri paralel calisiyor ve DbContext thread-safe
/// degil, bu yuzden her okumada kendi scope'unu acar.
/// </summary>
public class SettingsService : ISettingsService
{
    private const string CacheKey = "app-settings";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly IMemoryCache _cache;
    private readonly ILogger<SettingsService> _logger;

    /// <summary>Ayarlar ekrani bos birakilirsa kullanilacak varsayilan unvanlar.</summary>
    public static readonly string[] DefaultTitleKeywords =
    {
        "IT", "Bilgi İşlem", "Bilgi Teknolojileri", "SAP", "ERP", "CIO", "CTO"
    };

    public SettingsService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        IMemoryCache cache,
        ILogger<SettingsService> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _cache = cache;
        _logger = logger;
    }

    public async Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        var stored = await GetStoredAsync(ct);

        if (stored.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            return value;

        // Gemini anahtari yalnizca Ayarlar ekranindan gelir; User Secrets / appsettings'teki
        // yedek anahtar kullanilmaz (yanlis/ucretsiz projeye ait anahtara sessizce dusmesin).
        if (SettingKeys.IsSettingsOnly(key))
            return null;

        // Veritabaninda yoksa mevcut yapilandirmaya dus: bu sayede User Secrets
        // ile calisan kurulum, Ayarlar ekrani doldurulmadan da bozulmaz.
        return _configuration[key];
    }

    public async Task<Dictionary<string, string?>> GetStoredAsync(CancellationToken ct = default)
    {
        if (_cache.TryGetValue<Dictionary<string, string?>>(CacheKey, out var cached) && cached is not null)
            return cached;

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var settings = await db.AppSettings
            .AsNoTracking()
            .ToDictionaryAsync(s => s.Key, s => s.Value, ct);

        _cache.Set(CacheKey, settings, CacheDuration);
        return settings;
    }

    public async Task SetManyAsync(IDictionary<string, string?> values, CancellationToken ct = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var existing = await db.AppSettings.ToDictionaryAsync(s => s.Key, s => s, ct);

        foreach (var (key, value) in values)
        {
            if (existing.TryGetValue(key, out var row))
            {
                row.Value = value;
                row.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                db.AppSettings.Add(new AppSetting
                {
                    Key = key,
                    Value = value,
                    UpdatedAt = DateTime.UtcNow
                });
            }
        }

        await db.SaveChangesAsync(ct);
        _cache.Remove(CacheKey);

        _logger.LogInformation("{Count} ayar güncellendi.", values.Count);
    }

    public async Task<List<string>> GetTitleKeywordsAsync(CancellationToken ct = default)
    {
        var raw = await GetAsync(SettingKeys.LeadTitleKeywords, ct);

        if (string.IsNullOrWhiteSpace(raw))
            return DefaultTitleKeywords.ToList();

        var keywords = raw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return keywords.Count > 0 ? keywords : DefaultTitleKeywords.ToList();
    }
}

/// <summary>
/// API anahtari eksik oldugunda firlatilir. Kullaniciya "Ayarlardan anahtar
/// giriniz" mesajini gostermek icin diger hatalardan ayirt edilebilir olmali.
/// </summary>
public class MissingApiKeyException : InvalidOperationException
{
    public MissingApiKeyException(string serviceName)
        : base($"{serviceName} API anahtarı tanımlı değil. Lütfen Ayarlar ekranından API Key giriniz.")
    {
    }
}
