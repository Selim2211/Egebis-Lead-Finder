using EgebisLeadFinder.Models;
using EgebisLeadFinder.Services;
using EgebisLeadFinder.Services.Quality;
using Microsoft.AspNetCore.Mvc;

namespace EgebisLeadFinder.Controllers;

/// <summary>
/// Kalite Olcumu (yalnizca yonetici): bilinen ureticilerden olusan bir "olcu seti" tutulur; secilen NACE kodu icin gercek bir
/// arama calistirilir ve sonucun dogrulugu (donenlerin kaci gercekten uretici) ile kapsamasi (bilinen ureticilerin kaci bulundu)
/// yuzde olarak gosterilir. Boylece bir degisikligin aramayi iyilestirip iyilestirmedigi olculur.
/// </summary>
[Microsoft.AspNetCore.Authorization.Authorize(Roles = nameof(UserRole.Admin))]
public class QualityController : Controller
{
    private readonly ISettingsService _settings;
    private readonly QualityRunner _runner;

    public QualityController(ISettingsService settings, QualityRunner runner)
    {
        _settings = settings;
        _runner = runner;
    }

    private async Task<List<GoldItem>> LoadAsync(CancellationToken ct)
    {
        var items = ProducerEval.Parse(await _settings.GetAsync(SettingKeys.QualityGold, ct));
        return items.Count == 0 ? ProducerEval.Starter() : items;
    }

    private Task SaveAsync(IEnumerable<GoldItem> items, CancellationToken ct) =>
        _settings.SetManyAsync(new Dictionary<string, string?> { [SettingKeys.QualityGold] = ProducerEval.Serialize(items) }, ct);

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var rows = new List<QualityRow>();
        foreach (var item in await LoadAsync(ct))
        {
            var run = _runner.Get(item.Id);
            var eval = run is { Running: false, Error: null } ? ProducerEval.Evaluate(item, run.Firms.Select(f => f.Domain)) : null;
            rows.Add(new QualityRow(item, run, eval));
        }
        return View(rows);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveItem(string? id, string name, string naceCode, int maxCompanies, string? producers, string? notProducers, CancellationToken ct)
    {
        var code = NaceCatalog.Normalize(naceCode);
        if (string.IsNullOrWhiteSpace(name) || code is null || !NaceCatalog.Exists(code))
        {
            TempData["QualityError"] = "Ad ve resmi listede olan geçerli bir 6 haneli NACE kodu (ör. 27.51.08) girin.";
            return RedirectToAction(nameof(Index));
        }

        var items = await LoadAsync(ct);
        var item = items.FirstOrDefault(i => i.Id == id);
        if (item is null)
        {
            item = new GoldItem();
            items.Add(item);
        }
        item.Name = name.Trim();
        item.NaceCode = code;
        item.MaxCompanies = Math.Clamp(maxCompanies, 5, 100);
        item.Producers = ProducerEval.ParseDomains(producers);
        // Bir alan adi hem uretici hem degil olamaz: uretici listesi kazanir.
        item.NotProducers = ProducerEval.ParseDomains(notProducers).Except(item.Producers).ToList();

        await SaveAsync(items, ct);
        TempData["QualityOk"] = $"“{item.Name}” kaydedildi.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteItem(string id, CancellationToken ct)
    {
        var items = await LoadAsync(ct);
        items.RemoveAll(i => i.Id == id);
        await SaveAsync(items, ct);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Start(string id, CancellationToken ct)
    {
        var item = (await LoadAsync(ct)).FirstOrDefault(i => i.Id == id);
        if (item is null) return NotFound();

        // Starter ornegi henuz kaydedilmediyse kaydet ki ayni kimlikle geri bulunabilsin.
        await SaveAsync(await LoadAsync(ct), ct);

        if (!_runner.Start(item)) TempData["QualityError"] = "Bu ölçüm zaten sürüyor.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Sayfa acikken olcum bitti mi? (JSON; sayfa 3 sn'de bir sorar, bitince yenilenir.)</summary>
    [HttpGet]
    public IActionResult Status()
    {
        Response.Headers.CacheControl = "no-store";
        return Json(new { running = _runner.AnyRunning });
    }

    /// <summary>Donen bir firmayi olcu setine ekler: label = producer | not | clear.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Label(string id, string domain, string label, CancellationToken ct)
    {
        var items = await LoadAsync(ct);
        var item = items.FirstOrDefault(i => i.Id == id);
        var d = ProducerEval.NormalizeDomain(domain);
        if (item is null || d.Length == 0) return RedirectToAction(nameof(Index));

        item.Producers.RemoveAll(x => ProducerEval.SameSite(x, d));
        item.NotProducers.RemoveAll(x => ProducerEval.SameSite(x, d));
        if (label == "producer") item.Producers.Add(d);
        else if (label == "not") item.NotProducers.Add(d);

        await SaveAsync(items, ct);
        return Redirect(Url.Action(nameof(Index)) + "#item-" + id);
    }
}
