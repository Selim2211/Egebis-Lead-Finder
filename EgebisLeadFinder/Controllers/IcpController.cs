using EgebisLeadFinder.Data;
using EgebisLeadFinder.Models;
using EgebisLeadFinder.Services;
using Microsoft.AspNetCore.Mvc;
using EgebisLeadFinder.Services.Progress;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using EgebisLeadFinder.Services.Auth;

namespace EgebisLeadFinder.Controllers;

/// <summary>Ideal musteri profili (ICP) ekrani.</summary>
[Microsoft.AspNetCore.Authorization.Authorize(Roles = nameof(EgebisLeadFinder.Models.UserRole.Admin))]
public class IcpController : Controller
{
    private readonly IcpService _icp;
    private readonly ApplicationDbContext _db;

    public IcpController(IcpService icp, ApplicationDbContext db)
    {
        _icp = icp;
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Index([FromServices] IMemoryCache cache, [FromServices] BusinessProfileService profiles,
        CancellationToken ct)
    {
        ViewBag.Suggestion = cache.TryGetValue(SuggestionKey(), out IcpSuggestion? suggestion) ? suggestion : null;
        ViewBag.Website = suggestion?.Website ?? (await profiles.GetAsync(ct)).Website;
        ViewBag.MatchCount = await _db.Companies.CountAsync(c => c.IcpMatch, ct);
        ViewBag.MissingNace = await _db.Companies.CountAsync(c => c.NaceCode == null, ct);
        ViewBag.NaceCounts = await _db.Companies.AsNoTracking()
            .Where(c => c.NaceCode != null)
            .GroupBy(c => c.NaceCode!.Substring(0, 2))
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        return View(await _icp.GetAsync(ct));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(
        List<string>? nace, string? industries, string? cities, string? countries, string? exclude,
        int minEmployees, bool requireManufacturer, int locationWeight, bool rescore, CancellationToken ct)
    {
        await _icp.SaveAsync(new IcpProfile
        {
            NaceCodes = nace ?? new List<string>(),
            IndustryKeywords = SplitList(industries),
            Cities = SplitList(cities),
            Countries = SplitList(countries),
            ExcludeKeywords = SplitList(exclude),
            MinEmployees = minEmployees,
            RequireManufacturer = requireManufacturer,
            LocationWeight = locationWeight
        }, ct);

        if (rescore)
        {
            var changed = await _icp.RescoreAllAsync(ct);
            var matches = await _db.Companies.CountAsync(c => c.IcpMatch, ct);
            TempData["SettingsSaved"] = $"ICP kaydedildi. {changed} firmanın puanı güncellendi; {matches} firma ICP'ye uyuyor.";
        }
        else
        {
            TempData["SettingsSaved"] = "ICP kaydedildi. Yeni analiz edilen firmalar bu profile göre puanlanacak.";
        }

        return RedirectToAction(nameof(Index));
    }

    private string SuggestionKey() => $"icp-suggest:{User.UserId()}";

    /// <summary>
    /// Faz-II madde 5: sirketin kendi sitesini okuyup yapay zekaya ICP onerisi cikartir. Oneri
    /// kaydedilmez; ekranda gerekceleriyle gosterilir, kullanici forma uygulayip kaydeder.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Suggest(string? website, [FromServices] IWebScraperService scraper,
        [FromServices] IInsightAi ai, [FromServices] IMemoryCache cache, CancellationToken ct)
    {
        var url = BusinessProfileService.NormalizeUrl(website);
        if (url is null)
        {
            TempData["IcpError"] = "Geçerli bir web sitesi adresi girin (ör. egebis.com).";
            return RedirectToAction(nameof(Index));
        }

        var (suggestion, error) = await SuggestFromSiteAsync(url, scraper, ai, ct);
        if (suggestion is null)
        {
            TempData["IcpError"] = error;
            return RedirectToAction(nameof(Index));
        }

        cache.Set(SuggestionKey(), suggestion, SuggestionLifetime);
        AuditActionFilter.SetAuditSummary(HttpContext, $"Siteden ICP önerisi alındı: {url}");
        return RedirectToAction(nameof(Index), null, "icp-suggestion");
    }

    private static readonly TimeSpan SuggestionLifetime = TimeSpan.FromHours(2);

    /// <summary>Site okuma + yapay zeka onerisi; hata olursa kullaniciya gosterilecek mesaj doner.</summary>
    private static async Task<(IcpSuggestion? Suggestion, string? Error)> SuggestFromSiteAsync(string url, IWebScraperService scraper,
        IInsightAi ai, CancellationToken ct, Action<int, string>? progress = null)
    {
        progress?.Invoke(15, "Siteniz okunuyor");
        var site = await scraper.ScrapeAsync(url, ct);
        if (!site.Success) return (null, $"Site okunamadı: {site.Error ?? "içerik bulunamadı"}");

        progress?.Invoke(45, "Yapay zekâ müşteri profilinizi çıkarıyor");
        var suggestion = await ai.SuggestIcpAsync(url, site.Text, ct);
        return suggestion.Success ? (suggestion, null) : (null, $"ICP önerisi alınamadı: {suggestion.Error}");
    }

    /// <summary>Oneriyi arka planda hazirlar (ilerleme cubugu + iptal); JavaScript kapaliysa form Suggest'e duser.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult StartSuggest(string? website, [FromServices] JobProgressStore progress, [FromServices] IServiceScopeFactory scopes,
        [FromServices] IMemoryCache cache, [FromServices] ILogger<IcpController> logger)
    {
        var url = BusinessProfileService.NormalizeUrl(website);
        if (url is null) return BadRequest(new { error = "Geçerli bir web sitesi adresi girin (ör. egebis.com)." });

        var job = progress.Create();
        var key = SuggestionKey();
        var resultUrl = Url.Action(nameof(Index))! + "#icp-suggestion";
        AuditActionFilter.SetAuditSummary(HttpContext, $"Siteden ICP önerisi istendi: {url}");

        _ = Task.Run(async () =>
        {
            await using var scope = scopes.CreateAsyncScope();
            try
            {
                var (suggestion, error) = await SuggestFromSiteAsync(url,
                    scope.ServiceProvider.GetRequiredService<IWebScraperService>(),
                    scope.ServiceProvider.GetRequiredService<IInsightAi>(), job.Token,
                    (pct, stage) => progress.Report(job.Id, pct, stage));
                if (suggestion is null)
                {
                    progress.Fail(job.Id, error ?? "ICP önerisi alınamadı.");
                    return;
                }
                cache.Set(key, suggestion, SuggestionLifetime);
                progress.Complete(job.Id, resultUrl);
            }
            catch (OperationCanceledException) when (job.CancelRequested)
            {
                progress.MarkCancelled(job.Id, null, detail: "ICP önerisi iptal edildi.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "ICP önerisi başarısız (arka plan).");
                progress.Fail(job.Id, ex.Message);
            }
        });

        return Json(new { jobId = job.Id, progressUrl = Url.Action("JobStatus", "Company", new { jobId = job.Id }) });
    }

    /// <summary>Oneriyi ekrandan kaldirir.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult DismissSuggestion([FromServices] IMemoryCache cache)
    {
        cache.Remove(SuggestionKey());
        return RedirectToAction(nameof(Index));
    }

    /// <summary>NACE kodu bos firmalara kayitli analizden kod atar (arka plan isi, ilerleme cubuklu).</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult StartFillNace([FromServices] JobProgressStore progress, [FromServices] IServiceScopeFactory scopes,
        [FromServices] ILogger<IcpController> logger)
    {
        var job = progress.Create();
        var resultUrl = Url.Action(nameof(Index))!;

        _ = Task.Run(async () =>
        {
            await using var scope = scopes.CreateAsyncScope();
            var icp = scope.ServiceProvider.GetRequiredService<IcpService>();
            var reporter = new Progress<JobStep>(s => progress.Report(job.Id, s.Percent, s.Stage, s.Detail));
            try
            {
                var (filled, total) = await icp.FillMissingNaceAsync(reporter, job.Token);
                progress.Report(job.Id, 100, "Tamamlandı", $"{filled}/{total} firmaya NACE kodu atandı");
                progress.Complete(job.Id, resultUrl);
            }
            catch (OperationCanceledException) when (job.CancelRequested)
            {
                progress.MarkCancelled(job.Id, resultUrl, detail: "İptal edildi; o ana kadar atanan kodlar kaydedildi.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Toplu NACE ataması başarısız.");
                progress.Fail(job.Id, ex is QuotaExceededException ? "Gemini kotası doldu; atanan kodlar kaydedildi, sonra tekrar deneyin." : ex.Message);
            }
        });

        return Json(new { jobId = job.Id, progressUrl = Url.Action("JobStatus", "Company", new { jobId = job.Id }) });
    }

    /// <summary>Virgul veya satir sonu ile ayrilmis liste.</summary>
    public static List<string> SplitList(string? text) =>
        (text ?? string.Empty)
            .Split(new[] { ',', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
}
