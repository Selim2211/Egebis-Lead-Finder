using EgebisLeadFinder.Data;
using EgebisLeadFinder.Models;
using EgebisLeadFinder.Services.Auth;
using EgebisLeadFinder.Services.News;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EgebisLeadFinder.Controllers;

/// <summary>
/// Haber Akışı: borsa terminali gibi modüllü ekran (Özet, Haberler, Şirketler, Sektörler, Radar).
/// Akış herkese açık; radar konusu ekleme ve "Şimdi tara" yöneticiye özeldir. Firma haberleri kullanıcının göremediği firmaları göstermez.
/// </summary>
public class NewsController : Controller
{
    private const string NoSector = "Sektör belirtilmemiş";

    private readonly ApplicationDbContext _db;
    private readonly NewsAgentStatus _agent;
    private readonly Microsoft.Extensions.Options.IOptions<EgebisLeadFinder.Configuration.NewsOptions> _options;

    public NewsController(ApplicationDbContext db, NewsAgentStatus agent, Microsoft.Extensions.Options.IOptions<EgebisLeadFinder.Configuration.NewsOptions> options)
    {
        _db = db;
        _agent = agent;
        _options = options;
    }

    private int Me => User.UserId() ?? 0;
    private bool Admin => User.IsAdmin();

    /// <summary>Haber Akışı'ndaki TR / Global seçimi (çerezde saklanır); null = tümü.</summary>
    public const string RegionCookie = "news-region";
    private string? Region => NewsRegions.Normalize(Request.Cookies[RegionCookie]);

    private IQueryable<NewsFeedItem> VisibleItems(int me, bool admin)
    {
        var visible = _db.Companies.VisibleTo(_db, me, admin).Select(c => c.Id);
        var query = _db.NewsItems.AsNoTracking().Where(n => n.CompanyId == null || visible.Contains(n.CompanyId.Value));
        return InRegion(query);
    }

    /// <summary>TR / Global seçimi tüm bölümlere (akış, liderlik tablosu, şirketler, sektörler) uygulanır.</summary>
    private IQueryable<NewsFeedItem> InRegion(IQueryable<NewsFeedItem> query)
    {
        var region = Region;
        return region is null ? query : query.Where(n => n.Region == region);
    }

    /// <summary>TR / Global / Tümü seçimini kaydeder ve geldiği sayfaya döner.</summary>
    [HttpGet]
    public IActionResult SetRegion(string? value, string? returnUrl)
    {
        var region = NewsRegions.Normalize(value);
        if (region is null) Response.Cookies.Delete(RegionCookie);
        else Response.Cookies.Append(RegionCookie, region, new CookieOptions
        {
            Expires = DateTimeOffset.UtcNow.AddYears(1), HttpOnly = true, SameSite = SameSiteMode.Lax, IsEssential = true
        });
        return Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl!) : RedirectToAction(nameof(Index));
    }

    private IQueryable<int> WatchedIds() =>
        _db.CompanyWatches.Select(w => w.CompanyId).Union(_db.FavoriteCompanies.Select(f => f.CompanyId));

    private static IQueryable<NewsRow> ToRows(IQueryable<NewsFeedItem> q) =>
        q.Select(n => new NewsRow(n, n.Company != null ? n.Company.Name : null, n.Company != null ? n.Company.NewsBoost : 0));

    // ---------------- Ortak kabuk ----------------

    private async Task<List<CompanyPulse>> LoadPulsesAsync(CancellationToken ct)
    {
        var me = Me;
        var now = DateTime.UtcNow;
        var watched = WatchedIds();
        var companies = await _db.Companies.VisibleTo(_db, me, Admin).Where(c => watched.Contains(c.Id))
            .Select(c => new { c.Id, c.Name, c.Industry, c.Score, c.NewsBoost }).ToListAsync(ct);
        if (companies.Count == 0) return new List<CompanyPulse>();

        var ids = companies.Select(c => c.Id).ToList();
        var since30 = now.AddDays(-30);
        var items = await InRegion(_db.NewsItems.AsNoTracking())
            .Where(n => n.CompanyId != null && ids.Contains(n.CompanyId.Value) && n.PublishedAt >= since30)
            .Select(n => new { CompanyId = n.CompanyId!.Value, n.PublishedAt, n.Category, n.Title, n.Url, n.Importance })
            .ToListAsync(ct);
        var mine = (await _db.CompanyWatches.Where(w => w.UserId == me).Select(w => w.CompanyId).ToListAsync(ct)).ToHashSet();
        var byCompany = items.ToLookup(i => i.CompanyId);
        var since7 = now.AddDays(-7);

        return companies.Select(c =>
        {
            var list = byCompany[c.Id].ToList();
            return new CompanyPulse(c.Id, c.Name, c.Industry, c.Score, c.NewsBoost,
                NewsSpark.Build(list.Select(i => i.PublishedAt), now),
                list.Count(i => i.PublishedAt >= since7), list.Count,
                list.Count(i => i.Category == NewsCategories.Investment), list.Count(i => i.Category == NewsCategories.Risk),
                list.OrderByDescending(i => i.Importance).ThenByDescending(i => i.PublishedAt).Take(3).Select(i => new NewsTop(i.Title, i.Url, i.Importance)).ToList(),
                list.Count == 0 ? null : list.Max(i => i.PublishedAt), mine.Contains(c.Id));
        })
        .OrderByDescending(p => p.Boost).ThenByDescending(p => p.Count7).ThenByDescending(p => p.Score).ToList();
    }

    /// <summary>Son 7 günde haberi çıkan firmalar (takip + radarın adını geçirdiği); azsa takip/puanlı firmalarla tamamlanır.</summary>
    private async Task<List<CompanyMover>> LoadMoversAsync(CancellationToken ct)
    {
        var me = Me;
        var now = DateTime.UtcNow;
        var since = NewsSlots.TodayStartUtc(now).AddDays(-6);
        var visible = _db.Companies.VisibleTo(_db, me, Admin);
        var visibleIds = visible.Select(c => c.Id);
        var news = await InRegion(_db.NewsItems.AsNoTracking())
            .Where(n => n.PublishedAt >= since &&
                ((n.CompanyId != null && visibleIds.Contains(n.CompanyId.Value)) ||
                 (n.CompanyId == null && n.MentionedCompany != null && n.MentionedCompany != "")))
            .OrderByDescending(n => n.PublishedAt).Take(1500)
            .Select(n => new MoverNews(n.CompanyId,
                n.Company != null ? n.Company.Name : n.MentionedCompany!,
                n.Company != null ? n.Company.Industry : null,
                n.Company != null ? (int?)n.Company.Score : null,
                n.Company != null ? n.Company.NewsBoost : 0,
                n.City, n.PublishedAt, n.Category, n.Importance, n.Title, n.Url, n.Summary))
            .ToListAsync(ct);
        var movers = NewsImpact.Movers(news, now);

        if (movers.Count < 6)
        {
            var have = movers.Where(m => m.CompanyId != null).Select(m => m.CompanyId!.Value).ToList();
            var watched = WatchedIds();
            var extra = await visible.Where(c => !have.Contains(c.Id))
                .OrderByDescending(c => watched.Contains(c.Id)).ThenByDescending(c => c.Score)
                .Take(6 - movers.Count)
                .Select(c => new { c.Id, c.Name, c.Industry, c.Score, c.NewsBoost }).ToListAsync(ct);
            movers.AddRange(extra.Select(c => new CompanyMover(c.Id, c.Name, c.Industry, c.Score, c.NewsBoost,
                0, 0, new int[7], 0, 0, null, null, null, null)));
        }
        return movers;
    }

    private async Task<NewsShellModel> BuildShellAsync(string active, string? q, bool markSeen, CancellationToken ct, List<CompanyMover>? movers = null)
    {
        var me = Me;
        movers ??= await LoadMoversAsync(ct);
        var strip = movers.Where(m => m.Count7 > 0).OrderByDescending(m => m.Count7).ThenByDescending(m => Math.Abs(m.WeekImpact)).Take(16).ToList();
        if (strip.Count < 4) strip = movers.Take(16).ToList();
        var headlines = strip.Count > 0
            ? new List<NewsRow>()
            : await ToRows(VisibleItems(me, Admin).Where(n => n.Importance >= 3 && n.PublishedAt >= DateTime.UtcNow.AddDays(-3))
                .OrderByDescending(n => n.PublishedAt).Take(10)).ToListAsync(ct);

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == me, ct);
        var seenAt = user?.NewsSeenAt;
        var model = new NewsShellModel
        {
            Active = active, Q = q, IsAdmin = Admin, SeenAt = seenAt,
            MaxId = await _db.NewsItems.AsNoTracking().MaxAsync(n => (long?)n.Id, ct) ?? 0,
            Agent = _agent.Snapshot(),
            Counters = await CountersAsync(ct),
            TickMinutes = _options.Value.TickMinutes,
            Strip = strip, Headlines = headlines,
            LastRun = await _db.NewsScanRuns.AsNoTracking().OrderByDescending(r => r.StartedAt).FirstOrDefaultAsync(ct)
        };

        // Sayfa açıldı: bundan sonra gelenler "yeni" olur (ilk açılışta hepsi yeni sayılmasın).
        if (markSeen && user is not null)
        {
            user.NewsSeenAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
        Response.Headers.CacheControl = "no-store";
        return model;
    }

    private async Task<NewsCounters> CountersAsync(CancellationToken ct)
    {
        var todayStart = NewsSlots.TodayStartUtc(DateTime.UtcNow);
        return new NewsCounters(
            Queries: await _db.NewsScanRuns.AsNoTracking().Where(r => r.StartedAt >= todayStart).SumAsync(r => r.Queries, ct),
            NewToday: await _db.NewsItems.AsNoTracking().CountAsync(n => n.FetchedAt >= todayStart, ct),
            HotToday: await _db.NewsItems.AsNoTracking().CountAsync(n => n.FetchedAt >= todayStart && n.Importance >= 4, ct),
            Watched: await WatchedIds().CountAsync(ct));
    }

    private async Task<List<TopicStat>> TopicStatsAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var since = now.AddDays(-7);
        var topics = await _db.RadarTopics.AsNoTracking().OrderBy(t => t.Id).ToListAsync(ct);
        var hits = await InRegion(_db.NewsItems.AsNoTracking()).Where(n => n.RadarTopicId != null && n.PublishedAt >= since)
            .Select(n => new { TopicId = n.RadarTopicId!.Value, n.PublishedAt }).ToListAsync(ct);
        var scans = await _db.NewsScanCursors.AsNoTracking().Where(c => c.Kind == NewsCursorKinds.Topic)
            .ToDictionaryAsync(c => c.RefId, c => c.LastScannedAt, ct);
        return topics.Select(t =>
        {
            var mine = hits.Where(h => h.TopicId == t.Id).Select(h => h.PublishedAt).ToList();
            return new TopicStat(t, mine.Count, scans.TryGetValue(t.Id, out var at) ? at : null, NewsSpark.Build(mine, now));
        }).ToList();
    }

    // ---------------- Modüller ----------------

    /// <summary>Özet: canlı akış, dönen manşet, itibar liderlik tablosu, seçili şirket analizi.</summary>
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var movers = await LoadMoversAsync(ct);
        var shell = await BuildShellAsync("overview", null, true, ct, movers);
        shell.InjectTarget = "nxLiveList";
        shell.Compact = true;
        shell.CanInject = true;

        var since = DateTime.UtcNow.AddDays(-7);
        var recent = await ToRows(VisibleItems(Me, Admin).Where(n => n.PublishedAt >= since)
            .OrderByDescending(n => n.PublishedAt).ThenByDescending(n => n.Id).Take(300)).ToListAsync(ct);
        var dayAgo = DateTime.UtcNow.AddDays(-1);
        var hero = recent.OrderByDescending(r => r.Item.PublishedAt >= dayAgo).ThenByDescending(r => r.Item.Importance)
            .ThenByDescending(r => r.Item.PublishedAt).Take(5).ToList();

        return View(new NewsOverviewViewModel
        {
            Shell = shell,
            Live = recent.Take(25).ToList(),
            Hero = hero,
            Weekly = movers.Take(5).ToList(),
            Daily = movers.OrderByDescending(m => m.DayImpact).ThenByDescending(m => m.CountToday).ThenByDescending(m => m.Score ?? 0).Take(5).ToList()
        });
    }

    /// <summary>Haberler: filtreli, sayfalı tam liste.</summary>
    [HttpGet]
    public async Task<IActionResult> List(string? q, string? scope, string? category, int importance = 0, int days = 7,
        int? company = null, int? topic = null, int page = 1, CancellationToken ct = default)
    {
        var me = Me;
        days = days is 1 or 3 or 7 or 30 ? days : 7;
        importance = Math.Clamp(importance, 0, 5);

        var since = DateTime.UtcNow.AddDays(-days);
        var query = VisibleItems(me, Admin).Where(n => n.PublishedAt >= since);

        switch (scope)
        {
            case "watch": query = query.Where(n => n.Origin == NewsOrigins.Watch); break;
            case "radar": query = query.Where(n => n.Origin == NewsOrigins.Radar); break;
            case "mine":
                var mine = _db.CompanyWatches.Where(w => w.UserId == me).Select(w => w.CompanyId)
                    .Union(_db.FavoriteCompanies.Where(f => f.UserId == me).Select(f => f.CompanyId));
                query = query.Where(n => n.CompanyId != null && mine.Contains(n.CompanyId.Value));
                break;
        }

        string? companyName = null;
        if (company is int cid)
        {
            query = query.Where(n => n.CompanyId == cid);
            companyName = await _db.Companies.VisibleTo(_db, me, Admin).Where(c => c.Id == cid).Select(c => c.Name).FirstOrDefaultAsync(ct);
        }
        if (topic is int tid) query = query.Where(n => n.RadarTopicId == tid);

        if (!string.IsNullOrWhiteSpace(category) && NewsCategories.All.Contains(category))
            query = query.Where(n => n.Category == category);
        if (importance > 0) query = query.Where(n => n.Importance >= importance);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var like = $"%{q.Trim()}%";
            query = query.Where(n => EF.Functions.ILike(n.Title, like)
                || (n.MentionedCompany != null && EF.Functions.ILike(n.MentionedCompany, like))
                || (n.Company != null && EF.Functions.ILike(n.Company.Name, like)));
        }

        var total = await query.CountAsync(ct);
        var current = PagerModel.Clamp(page, total);
        var rows = await ToRows(query.OrderByDescending(n => n.PublishedAt).ThenByDescending(n => n.Id)
            .Skip((current - 1) * PagerModel.DefaultPageSize).Take(PagerModel.DefaultPageSize)).ToListAsync(ct);

        var unfiltered = string.IsNullOrWhiteSpace(q) && string.IsNullOrWhiteSpace(category) && importance == 0
            && (string.IsNullOrEmpty(scope) || scope == "all") && company is null && topic is null && current == 1;
        var shell = await BuildShellAsync("list", q, current == 1 && string.IsNullOrWhiteSpace(q), ct);
        shell.InjectTarget = "ncGrid";
        shell.CanInject = unfiltered;

        return View(new NewsFeedViewModel
        {
            Shell = shell, Rows = rows, Q = q, Scope = scope ?? "all", Category = category, Importance = importance, Days = days,
            CompanyId = company, CompanyName = companyName, TopicId = topic,
            Pager = new PagerModel { Page = current, TotalItems = total }
        });
    }

    /// <summary>Şirketler: takip edilen firmaların haber nabzı.</summary>
    [HttpGet]
    public async Task<IActionResult> Companies(CancellationToken ct)
    {
        var pulses = await LoadPulsesAsync(ct);
        var shell = await BuildShellAsync("companies", null, false, ct);
        // En çok haberi olan üstte; eşitse puanı yüksek olan, sonra ada göre.
        var rows = pulses.OrderByDescending(p => p.Count7).ThenByDescending(p => p.Count30).ThenByDescending(p => p.Score).ThenBy(p => p.Name).ToList();
        return View(new NewsCompaniesViewModel { Shell = shell, Rows = rows });
    }

    /// <summary>Sektörler: firmaların sektörüne göre haber dağılımı; radar haberleri ayrı grup.</summary>
    [HttpGet]
    public async Task<IActionResult> Sectors(CancellationToken ct)
    {
        var pulses = await LoadPulsesAsync(ct);
        var shell = await BuildShellAsync("sectors", null, false, ct);

        var sectors = pulses.GroupBy(p => string.IsNullOrWhiteSpace(p.Industry) ? NoSector : p.Industry.Trim())
            .Select(g => new SectorStat(g.Key, g.Count(), g.Sum(p => p.Count30), g.Sum(p => p.Positive), g.Sum(p => p.Negative),
                g.SelectMany(p => p.Top).OrderByDescending(t => t.Importance).Take(3).ToList(), false))
            .OrderByDescending(s => s.Count).ThenBy(s => s.Name).ToList();

        var since = DateTime.UtcNow.AddDays(-30);
        var radar = await InRegion(_db.NewsItems.AsNoTracking()).Where(n => n.CompanyId == null && n.PublishedAt >= since)
            .OrderByDescending(n => n.PublishedAt).Take(400)
            .Select(n => new { n.Category, n.Title, n.Url, n.Importance }).ToListAsync(ct);
        if (radar.Count > 0)
            sectors.Add(new SectorStat("Radar (sektör bilinmiyor)", 0, radar.Count,
                radar.Count(r => r.Category == NewsCategories.Investment), radar.Count(r => r.Category == NewsCategories.Risk),
                radar.OrderByDescending(r => r.Importance).Take(3).Select(r => new NewsTop(r.Title, r.Url, r.Importance)).ToList(), true));

        return View(new NewsSectorsViewModel { Shell = shell, Sectors = sectors });
    }

    /// <summary>Radar: radar konuları, her birinin haber yoğunluğu ve seçilen konunun haberleri.</summary>
    [HttpGet]
    public async Task<IActionResult> Radar(int? topic, CancellationToken ct)
    {
        var shell = await BuildShellAsync("radar", null, false, ct);
        var topics = await TopicStatsAsync(ct);
        var since = DateTime.UtcNow.AddDays(-30);
        var query = VisibleItems(Me, Admin).Where(n => n.Origin == NewsOrigins.Radar && n.PublishedAt >= since);
        if (topic is int tid) query = query.Where(n => n.RadarTopicId == tid);
        var rows = await ToRows(query.OrderByDescending(n => n.PublishedAt).ThenByDescending(n => n.Id).Take(20)).ToListAsync(ct);
        return View(new NewsRadarViewModel { Shell = shell, Topics = topics, SelectedTopicId = topic, Rows = rows });
    }

    // ---------------- Canlı uçlar ----------------

    /// <summary>Canlı ekran: ajanın anlık durumu, sayaçlar ve verilen kimlikten sonra kaç yeni haber geldi (20 sn'de bir çağrılır).</summary>
    [HttpGet]
    public async Task<IActionResult> Live(long after, CancellationToken ct)
    {
        var items = VisibleItems(Me, Admin).Where(n => n.Id > after);
        var agent = _agent.Snapshot();
        Response.Headers.CacheControl = "no-store";
        return Json(new
        {
            count = await items.CountAsync(ct),
            hot = await items.CountAsync(n => n.Importance >= 4, ct),
            maxId = await _db.NewsItems.AsNoTracking().MaxAsync(n => (long?)n.Id, ct) ?? 0,
            agent = new
            {
                state = agent.State,
                query = agent.CurrentQuery,
                done = agent.Done,
                total = agent.Total,
                lastTick = agent.LastTickAt,
                nextTick = agent.NextTickAt,
                restUntil = agent.RestUntil
            },
            counters = await CountersAsync(ct)
        });
    }

    /// <summary>Verilen kimlikten sonraki yeni haber kartları (HTML); canlı ekran bunları listenin başına ekler.</summary>
    [HttpGet]
    public async Task<IActionResult> Cards(long after, bool compact = false, CancellationToken ct = default)
    {
        var rows = await ToRows(VisibleItems(Me, Admin).Where(n => n.Id > after)
            .OrderByDescending(n => n.PublishedAt).ThenByDescending(n => n.Id).Take(30)).ToListAsync(ct);
        ViewData["ForceNew"] = true;
        ViewData["Compact"] = compact;
        Response.Headers.CacheControl = "no-store";
        return PartialView("_NewsCards", rows);
    }

    /// <summary>Firmayı takibe alır / takipten çıkarır (firma kartındaki düğme).</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleWatch(int id, string? returnUrl, CancellationToken ct)
    {
        var me = Me;
        var existing = await _db.CompanyWatches.FirstOrDefaultAsync(w => w.UserId == me && w.CompanyId == id, ct);
        bool watching;
        if (existing is not null)
        {
            _db.CompanyWatches.Remove(existing);
            watching = false;
        }
        else
        {
            if (!await _db.Companies.VisibleTo(_db, me, Admin).AnyAsync(c => c.Id == id, ct)) return NotFound();
            _db.CompanyWatches.Add(new CompanyWatch { UserId = me, CompanyId = id });
            watching = true;
        }

        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { /* aynı anda iki tıklama: kayıt zaten var */ }

        var name = await _db.Companies.Where(c => c.Id == id).Select(c => c.Name).FirstOrDefaultAsync(ct);
        AuditActionFilter.SetAuditSummary(HttpContext, $"{(watching ? "Takibe alındı" : "Takipten çıkarıldı")}: {name}");
        TempData["SettingsSaved"] = watching
            ? "Firma takibe alındı; haberleri Haber Akışı'nda görünecek."
            : "Firma takipten çıkarıldı.";
        return !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl) ? Redirect(returnUrl) : RedirectToAction(nameof(Index));
    }

    // ---------------- Yönetici: radar konuları ve elle tarama ----------------

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<IActionResult> AddTopic(string? query, string? city, CancellationToken ct)
    {
        query = query?.Trim();
        if (string.IsNullOrWhiteSpace(query) || query.Length < 4)
        {
            TempData["SettingsError"] = "Radar konusu en az 4 karakter olmalı (ör. \"yeni fabrika yatırımı\").";
            return RedirectToAction(nameof(Radar));
        }

        city = string.IsNullOrWhiteSpace(city) ? null : city.Trim();
        if (!await _db.RadarTopics.AnyAsync(t => t.Query == query && t.City == city, ct))
        {
            _db.RadarTopics.Add(new RadarTopic { Query = query.Length > 200 ? query[..200] : query, City = city, CreatedByUserId = User.UserId() });
            await _db.SaveChangesAsync(ct);
        }
        AuditActionFilter.SetAuditSummary(HttpContext, $"Radar konusu eklendi: {query} {city}");
        TempData["SettingsSaved"] = "Radar konusu eklendi; bir sonraki taramada haberleri gelir.";
        return RedirectToAction(nameof(Radar));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<IActionResult> RemoveTopic(int id, CancellationToken ct)
    {
        var topic = await _db.RadarTopics.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (topic is not null)
        {
            _db.RadarTopics.Remove(topic);
            await _db.SaveChangesAsync(ct);
            AuditActionFilter.SetAuditSummary(HttpContext, $"Radar konusu silindi: {topic.Query}");
            TempData["SettingsSaved"] = "Radar konusu silindi (eski haberler kalır).";
        }
        return RedirectToAction(nameof(Radar));
    }

    /// <summary>Taramayı arka planda başlatır; sayfa beklemez.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public IActionResult ScanNow(string? returnUrl, [FromServices] IServiceScopeFactory scopes, [FromServices] ILogger<NewsController> logger)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<NewsScanService>().RunAsync(CancellationToken.None);
            }
            catch (Exception ex) { logger.LogError(ex, "Elle başlatılan haber taraması başarısız."); }
        });

        AuditActionFilter.SetAuditSummary(HttpContext, "Haber taraması elle başlatıldı");
        TempData["SettingsSaved"] = "Tur arka planda başladı; yeni haberler canlı olarak akışa eklenir.";
        return !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl) ? Redirect(returnUrl) : RedirectToAction(nameof(Index));
    }
}
