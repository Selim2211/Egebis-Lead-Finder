using EgebisLeadFinder.Data;
using EgebisLeadFinder.Models;
using Microsoft.EntityFrameworkCore;

namespace EgebisLeadFinder.Services;

public interface IApiUsageTracker
{
    /// <summary>Sağlayıcının çağrı sayacını bir artırır. Satır yoksa oluşturur.</summary>
    Task IncrementAsync(string provider, CancellationToken ct = default);

    /// <summary>Mevcut sayacı okur. Hiç kayıt yoksa Count=0, ResetAt=null döner.</summary>
    Task<ApiUsage> GetAsync(string provider, CancellationToken ct = default);

    /// <summary>Yeni API anahtarı alındığında sayacı sıfırlar.</summary>
    Task ResetAsync(string provider, CancellationToken ct = default);

    /// <summary>Belirtilen tarih araligindaki (dahil) gunluk cagri toplami.</summary>
    Task<int> GetRangeCountAsync(string provider, DateOnly from, DateOnly to, CancellationToken ct = default);

    /// <summary>Kota doldu / anahtar gecersiz gibi hatayi kaydeder (API Kullanimi ekrani).</summary>
    Task RecordErrorAsync(string provider, string message, CancellationToken ct = default) => Task.CompletedTask;

    /// <summary>Bir Gemini cagrisinin gercek token sayilarini (gun + model bazinda) toplar.</summary>
    Task RecordGeminiTokensAsync(string model, long inputTokens, long outputTokens, CancellationToken ct = default) => Task.CompletedTask;

    /// <summary>Tarih araligindaki (dahil) Gemini token kayitlari.</summary>
    Task<List<GeminiTokenDaily>> GetGeminiTokensAsync(DateOnly from, DateOnly to, CancellationToken ct = default) => Task.FromResult(new List<GeminiTokenDaily>());

    /// <summary>Son 'days' gunun gunluk sayilari (bugun dahil, eskiden yeniye).</summary>
    Task<int[]> GetDailySeriesAsync(string provider, int days, CancellationToken ct = default) => Task.FromResult(new int[days]);
}

/// <summary>
/// API kullanım sayacı. Serper gibi tipli HttpClient'lar DbContext'e dogrudan
/// bagli olamaz (kisa omurlu, DbContext thread-safe degil); SettingsService
/// deseniyle her cagrida kendi scope'unu acar.
/// </summary>
public class ApiUsageTracker : IApiUsageTracker
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ApiUsageTracker> _logger;

    public ApiUsageTracker(IServiceScopeFactory scopeFactory, ILogger<ApiUsageTracker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>Turkiye sabit UTC+3 (DST yok) gore bugunun tarihi.</summary>
    private static DateOnly TurkeyToday => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));

    public async Task IncrementAsync(string provider, CancellationToken ct = default)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            // Once atomik UPDATE dene (yaris durumunda kayip artis olmasin).
            var updated = await db.ApiUsages
                .Where(x => x.Provider == provider)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.Count, x => x.Count + 1)
                    .SetProperty(x => x.UpdatedAt, DateTime.UtcNow), ct);

            if (updated == 0)
            {
                // Satir yok: olustur. Iki istek ayni anda buraya duserse benzersiz
                // indeks ikincisini reddeder; o durumda sessizce yut, sayim
                // bir sonraki cagrida yine artacaktir (kritik olmayan sayac).
                try
                {
                    db.ApiUsages.Add(new ApiUsage { Provider = provider, Count = 1, UpdatedAt = DateTime.UtcNow });
                    await db.SaveChangesAsync(ct);
                }
                catch (DbUpdateException)
                {
                    // Cakisma: baska bir istek satiri az once olusturdu.
                }
            }

            var today = TurkeyToday;
            var updatedDaily = await db.ApiUsageDailies
                .Where(x => x.Provider == provider && x.Date == today)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.Count, x => x.Count + 1)
                    .SetProperty(x => x.UpdatedAt, DateTime.UtcNow), ct);

            if (updatedDaily == 0)
            {
                try
                {
                    db.ApiUsageDailies.Add(new ApiUsageDaily
                    {
                        Provider = provider,
                        Date = today,
                        Count = 1,
                        UpdatedAt = DateTime.UtcNow
                    });
                    await db.SaveChangesAsync(ct);
                }
                catch (DbUpdateException)
                {
                    // Cakisma: baska bir istek satiri az once olusturdu.
                }
            }
        }
        catch (Exception ex)
        {
            // Sayac kritik degil; API cagrisinin kendisini engellememeli.
            _logger.LogWarning(ex, "API kullanım sayacı güncellenemedi: {Provider}", provider);
        }
    }

    public async Task RecordGeminiTokensAsync(string model, long inputTokens, long outputTokens, CancellationToken ct = default)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var today = TurkeyToday;
            model = model.Length > 100 ? model[..100] : model;

            var updated = await db.GeminiTokenDailies
                .Where(x => x.Date == today && x.Model == model)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.Calls, x => x.Calls + 1)
                    .SetProperty(x => x.InputTokens, x => x.InputTokens + inputTokens)
                    .SetProperty(x => x.OutputTokens, x => x.OutputTokens + outputTokens)
                    .SetProperty(x => x.UpdatedAt, DateTime.UtcNow), ct);

            if (updated == 0)
            {
                try
                {
                    db.GeminiTokenDailies.Add(new GeminiTokenDaily
                    {
                        Date = today, Model = model, Calls = 1, InputTokens = inputTokens, OutputTokens = outputTokens
                    });
                    await db.SaveChangesAsync(ct);
                }
                catch (DbUpdateException)
                {
                    // Cakisma: ayni anda baska bir istek satiri olusturdu; bu cagri sayilmaz (kritik degil).
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Gemini token kaydı yazılamadı.");
        }
    }

    public async Task<List<GeminiTokenDaily>> GetGeminiTokensAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.GeminiTokenDailies.AsNoTracking().Where(x => x.Date >= from && x.Date <= to).ToListAsync(ct);
    }

    public async Task<int> GetRangeCountAsync(string provider, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await db.ApiUsageDailies.AsNoTracking()
            .Where(x => x.Provider == provider && x.Date >= from && x.Date <= to)
            .SumAsync(x => x.Count, ct);
    }

    public async Task<ApiUsage> GetAsync(string provider, CancellationToken ct = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var row = await db.ApiUsages.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Provider == provider, ct);

        return row ?? new ApiUsage { Provider = provider, Count = 0 };
    }

    public async Task ResetAsync(string provider, CancellationToken ct = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var updated = await db.ApiUsages
            .Where(x => x.Provider == provider)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Count, 0)
                .SetProperty(x => x.ResetAt, DateTime.UtcNow)
                .SetProperty(x => x.UpdatedAt, DateTime.UtcNow), ct);

        if (updated == 0)
        {
            db.ApiUsages.Add(new ApiUsage
            {
                Provider = provider,
                Count = 0,
                ResetAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync(ct);
        }

        _logger.LogInformation("{Provider} kullanım sayacı sıfırlandı.", provider);
    }

    public async Task RecordErrorAsync(string provider, string message, CancellationToken ct = default)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var text = message.Length > 500 ? message[..500] : message;

            var updated = await db.ApiUsages.Where(x => x.Provider == provider)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.LastErrorAt, DateTime.UtcNow)
                    .SetProperty(x => x.LastError, text), CancellationToken.None);

            if (updated == 0)
            {
                db.ApiUsages.Add(new ApiUsage { Provider = provider, LastErrorAt = DateTime.UtcNow, LastError = text });
                await db.SaveChangesAsync(CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "API hata kaydı yazılamadı: {Provider}", provider);
        }
    }

    public async Task<int[]> GetDailySeriesAsync(string provider, int days, CancellationToken ct = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var today = TurkeyToday;
        var from = today.AddDays(-(days - 1));
        var rows = await db.ApiUsageDailies.AsNoTracking()
            .Where(x => x.Provider == provider && x.Date >= from && x.Date <= today)
            .ToDictionaryAsync(x => x.Date, x => x.Count, ct);

        return Enumerable.Range(0, days).Select(i => rows.GetValueOrDefault(from.AddDays(i))).ToArray();
    }
}
