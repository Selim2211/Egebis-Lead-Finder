using EgebisLeadFinder.Data;
using EgebisLeadFinder.Models;
using Microsoft.EntityFrameworkCore;

namespace EgebisLeadFinder.Services;

/// <summary>
/// Firma/lead'i Salesforce'a gonderip sonucu kayda yazar. Hem "Salesforce'a Aktar"
/// butonu hem arka plan senkronu bunu kullanir.
///
/// Durum alanlari ExecuteUpdate ile yazilir: SaveChanges'teki "degisti" isaretlemesini
/// tetiklemez ve gonderim surerken kullanicinin yaptigi degisikligin isaretini ezmez
/// (isaret gonderimden ONCE temizlenir; arada degisen kayit tekrar isaretli kalir).
/// </summary>
public class SalesforceSyncRunner
{
    /// <summary>Hata alan kayit bu sure dolmadan tekrar denenmez.</summary>
    public static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(10);

    private readonly ApplicationDbContext _db;
    private readonly ISalesforceConnector _salesforce;
    private readonly ILogger<SalesforceSyncRunner> _logger;

    public SalesforceSyncRunner(ApplicationDbContext db, ISalesforceConnector salesforce, ILogger<SalesforceSyncRunner> logger)
    {
        _db = db;
        _salesforce = salesforce;
        _logger = logger;
    }

    public async Task<SalesforceSyncResult?> SyncCompanyAsync(int id, CancellationToken ct = default)
    {
        await _db.Companies.Where(c => c.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.SalesforceDirty, false), ct);

        var company = await _db.Companies.AsNoTracking().Include(c => c.Contacts)
            .FirstOrDefaultAsync(c => c.Id == id, ct);
        if (company is null) return null;

        var result = await _salesforce.SyncCompanyAsync(company, ct);
        var now = DateTime.UtcNow;
        var error = Truncate(result.Error, 500);

        if (result.Success)
            await _db.Companies.Where(c => c.Id == id).ExecuteUpdateAsync(s => s
                .SetProperty(c => c.SalesforceId, result.SalesforceId)
                .SetProperty(c => c.SalesforceSyncedAt, now)
                .SetProperty(c => c.SalesforceAttemptAt, now)
                .SetProperty(c => c.SalesforceSyncError, (string?)null), ct);
        else
            await _db.Companies.Where(c => c.Id == id).ExecuteUpdateAsync(s => s
                .SetProperty(c => c.SalesforceDirty, true)
                .SetProperty(c => c.SalesforceAttemptAt, now)
                .SetProperty(c => c.SalesforceSyncError, error), ct);

        return result;
    }

    public async Task<SalesforceSyncResult?> SyncLeadAsync(int id, CancellationToken ct = default)
    {
        await _db.Leads.Where(l => l.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.SalesforceDirty, false), ct);

        var lead = await _db.Leads.AsNoTracking()
            .Include(l => l.Company).Include(l => l.Contact).Include(l => l.SentEmails)
            .FirstOrDefaultAsync(l => l.Id == id, ct);
        if (lead is null) return null;

        var result = await _salesforce.SyncLeadAsync(lead, ct);
        var now = DateTime.UtcNow;
        var error = Truncate(result.Error, 500);

        if (result.Success)
            await _db.Leads.Where(l => l.Id == id).ExecuteUpdateAsync(s => s
                .SetProperty(l => l.SalesforceId, result.SalesforceId)
                .SetProperty(l => l.SalesforceSyncedAt, now)
                .SetProperty(l => l.SalesforceAttemptAt, now)
                .SetProperty(l => l.SalesforceSyncError, (string?)null), ct);
        else
            await _db.Leads.Where(l => l.Id == id).ExecuteUpdateAsync(s => s
                .SetProperty(l => l.SalesforceDirty, true)
                .SetProperty(l => l.SalesforceAttemptAt, now)
                .SetProperty(l => l.SalesforceSyncError, error), ct);

        return result;
    }

    /// <summary>
    /// Degisip henuz gonderilmemis kayitlari gonderir. Kapsam: lead'i olan ya da daha once
    /// elle gonderilmis firmalar ve tum lead'ler. Donen deger: gonderim denenen kayit sayisi.
    /// </summary>
    public async Task<int> SyncPendingAsync(int batchSize, CancellationToken ct = default)
    {
        var retryBefore = DateTime.UtcNow - RetryAfter;

        var companyIds = await _db.Companies
            .Where(c => c.SalesforceDirty
                        && (c.SalesforceId != null || c.Leads.Any())
                        && (c.SalesforceSyncError == null || c.SalesforceAttemptAt == null || c.SalesforceAttemptAt < retryBefore))
            .OrderBy(c => c.SalesforceAttemptAt)
            .Select(c => c.Id)
            .Take(batchSize)
            .ToListAsync(ct);

        var leadIds = await _db.Leads
            .Where(l => l.SalesforceDirty
                        && (l.SalesforceSyncError == null || l.SalesforceAttemptAt == null || l.SalesforceAttemptAt < retryBefore))
            .OrderBy(l => l.SalesforceAttemptAt)
            .Select(l => l.Id)
            .Take(batchSize)
            .ToListAsync(ct);

        // Firma once: lead'in Salesforce'ta gorunen firma bilgisi ayni turda guncel olsun.
        foreach (var id in companyIds)
        {
            var r = await SyncCompanyAsync(id, ct);
            if (r is { Success: false }) _logger.LogWarning("Otomatik Salesforce senkronu (firma {Id}) başarısız: {Error}", id, r.Error);
        }

        foreach (var id in leadIds)
        {
            var r = await SyncLeadAsync(id, ct);
            if (r is { Success: false }) _logger.LogWarning("Otomatik Salesforce senkronu (lead {Id}) başarısız: {Error}", id, r.Error);
        }

        if (companyIds.Count + leadIds.Count > 0)
            _logger.LogInformation("Salesforce otomatik senkron: {Companies} firma, {Leads} lead gönderildi.",
                companyIds.Count, leadIds.Count);

        return companyIds.Count + leadIds.Count;
    }

    private static string? Truncate(string? value, int max) =>
        string.IsNullOrEmpty(value) || value.Length <= max ? value : value[..max];
}

/// <summary>
/// Salesforce bagliyken degisen kayitlari periyodik olarak gonderir. Bagli degilse
/// hicbir sey yapmaz; isaretler birikir ve baglaninca gonderilir.
/// </summary>
public class SalesforceAutoSyncService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);
    private const int BatchSize = 25;

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<SalesforceAutoSyncService> _logger;

    public SalesforceAutoSyncService(IServiceScopeFactory scopes, ILogger<SalesforceAutoSyncService> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        do
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var connector = scope.ServiceProvider.GetRequiredService<ISalesforceConnector>();
                if (!await connector.IsConnectedAsync(stoppingToken)) continue;

                var runner = scope.ServiceProvider.GetRequiredService<SalesforceSyncRunner>();
                await runner.SyncPendingAsync(BatchSize, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Tek tur patlarsa servis durmasin; bir sonraki turda tekrar denenir.
                _logger.LogWarning(ex, "Salesforce otomatik senkron turu başarısız.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
