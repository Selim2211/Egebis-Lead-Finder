using System.Collections.Concurrent;
using EgebisLeadFinder.Data;
using EgebisLeadFinder.Models;

namespace EgebisLeadFinder.Services.Quality;

/// <summary>Kalite Olcumu ekranindaki bir satir: olcu seti kalemi, son aramasi ve hesaplanan sonuc.</summary>
public record QualityRow(GoldItem Item, QualityRun? Run, EvalResult? Eval);

/// <summary>Olcum aramasinda donen firma.</summary>
public record QualityFirm(string Domain, string Name, string? Kind, string? Quote);

/// <summary>Bir olcu seti kaleminin son olcum aramasi (bellekte tutulur; uygulama yeniden baslarsa silinir).</summary>
public class QualityRun
{
    public string ItemId { get; init; } = string.Empty;
    public DateTime StartedAt { get; init; } = DateTime.UtcNow;
    public DateTime? FinishedAt { get; set; }
    public bool Running { get; set; } = true;
    public string? Error { get; set; }
    public string? Warning { get; set; }
    public List<QualityFirm> Firms { get; set; } = new();

    /// <summary>Yapay zeka / kurallarla "uretici degil" diye listeden cikarilan firma sayisi.</summary>
    public int Dropped { get; set; }
}

/// <summary>
/// Kalite Olcumu: olcu seti kalemindeki NACE kodu icin GERCEK bir urun aramasi calistirir (normal aramayla ayni akis; Serper ve
/// Gemini kredisi harcar, bulunan firmalar firma havuzuna eklenir) ve donen firmalari bellekte saklar. Dogruluk/kapsama
/// hesabi ProducerEval'dadir. Arama arka planda calisir; sayfa durumu sorgular.
/// </summary>
public class QualityRunner
{
    private static readonly TimeSpan MaxDuration = TimeSpan.FromMinutes(15);

    private readonly ConcurrentDictionary<string, QualityRun> _runs = new();
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<QualityRunner> _logger;

    public QualityRunner(IServiceScopeFactory scopeFactory, ILogger<QualityRunner> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public QualityRun? Get(string itemId) => _runs.GetValueOrDefault(itemId);

    public bool AnyRunning => _runs.Values.Any(r => r.Running);

    /// <summary>Aramayi arka planda baslatir. Ayni kalem zaten calisiyorsa false.</summary>
    public bool Start(GoldItem item)
    {
        var run = new QualityRun { ItemId = item.Id };
        while (true)
        {
            if (_runs.TryGetValue(item.Id, out var current))
            {
                if (current.Running) return false;
                if (_runs.TryUpdate(item.Id, run, current)) break;
            }
            else if (_runs.TryAdd(item.Id, run)) break;
        }

        _ = Task.Run(() => ExecuteAsync(item, run));
        return true;
    }

    private async Task ExecuteAsync(GoldItem item, QualityRun run)
    {
        using var timeout = new CancellationTokenSource(MaxDuration);
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var nace = scope.ServiceProvider.GetRequiredService<NaceLookupService>();
            var discovery = scope.ServiceProvider.GetRequiredService<LeadDiscoveryService>();

            var info = await nace.LookupAsync(item.NaceCode, timeout.Token);
            if (!info.Ok) { run.Error = info.Error; return; }

            // Normal "Firma Ara > NACE" aramasiyla ayni kriterler (bolge Turkiye, tum urunler).
            var region = SearchRegions.Default;
            var products = NaceProducts.Resolve(null, info.Products.Count > 0 ? info.Products : info.SearchTerms);
            var sector = info.ShortName ?? info.Name ?? info.Code;
            var criteria = new SearchCriteria
            {
                NaceCode = info.Code,
                ProductList = products,
                NaceProducts = string.Join(", ", products),
                NaceContext = string.IsNullOrWhiteSpace(info.Description)
                    ? $"NACE {info.Code}: {info.Name}"
                    : $"NACE {info.Code}: {info.Name} — {info.Description}",
                Industry = sector.Length > 150 ? sector[..150] : sector,
                RegionKey = region.Key,
                Country = region.Country,
                MaxCompanies = Math.Clamp(item.MaxCompanies, 5, 100)
            };

            var result = await discovery.RunAsync(criteria, timeout.Token);

            run.Warning = result.Warning ?? result.AbortReason;
            run.Dropped = result.NaceFiltered;
            run.Firms = result.Companies
                .Where(c => !string.IsNullOrWhiteSpace(c.Domain))
                .Select(c => new QualityFirm(
                    c.Domain!, c.Name,
                    result.FirmKinds.TryGetValue(c.Id, out var kind) ? kind.ToString() : null,
                    result.ProducerProof.GetValueOrDefault(c.Id)))
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Kalite ölçümü başarısız: {Item}", item.Name);
            run.Error = ex is OperationCanceledException ? "Ölçüm zaman aşımına uğradı." : ex.Message;
        }
        finally
        {
            run.FinishedAt = DateTime.UtcNow;
            run.Running = false;
        }
    }
}
