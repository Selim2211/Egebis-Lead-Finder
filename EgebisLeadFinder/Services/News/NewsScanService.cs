using EgebisLeadFinder.Configuration;
using EgebisLeadFinder.Controllers;
using EgebisLeadFinder.Data;
using EgebisLeadFinder.Models;
using EgebisLeadFinder.Services.CompanyIntel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EgebisLeadFinder.Services.News;

/// <summary>
/// Haber ajanı: her tur, dönüşümlü sıradan en fazla <see cref="NewsOptions.QueriesPerTick"/> sorgu atar (yeni takip ve radar önce).
/// Takip edilen firmalar (CompanyWatch + favoriler) ve radar konuları için haber toplar, tekilleştirir, sınıflar, kaydeder;
/// olumlu (yatırım) haber alan firmanın puanını yükseltir. Aynı anda tek tur çalışır.
/// Maliyet: ana kaynak ücretsiz RSS; hata verirse <see cref="NewsSourceHealth"/> turu keser ve RSS'i dinlendirir, yedek (Serper)
/// yalnızca dinlenmeden sonra ve günlük tavanla kullanılır.
/// </summary>
public class NewsScanService
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private readonly ApplicationDbContext _db;
    private readonly IEnumerable<INewsSource> _sources;
    private readonly ISettingsService _settings;
    private readonly NewsOptions _news;
    private readonly ResearchOptions _research;
    private readonly ILogger<NewsScanService> _logger;
    private readonly INewsAi? _ai;
    private readonly Services.Inbox.NotificationService? _notifications;
    private readonly NewsSourceHealth _health;
    private readonly NewsAgentStatus _status;
    private readonly IcpService? _icp;

    public NewsScanService(ApplicationDbContext db, IEnumerable<INewsSource> sources, ISettingsService settings,
        IOptions<NewsOptions> news, IOptions<ResearchOptions> research, ILogger<NewsScanService> logger,
        INewsAi? ai = null, Services.Inbox.NotificationService? notifications = null,
        NewsSourceHealth? health = null, NewsAgentStatus? status = null, IcpService? icp = null)
    {
        _db = db;
        _sources = sources;
        _settings = settings;
        _news = news.Value;
        _research = research.Value;
        _logger = logger;
        _ai = ai;
        _notifications = notifications;
        _health = health ?? new NewsSourceHealth();
        _status = status ?? new NewsAgentStatus();
        _icp = icp;
    }

    /// <summary>Takip kümesi: CompanyWatch ∪ favoriler (firma başına bir tarama, kullanıcılar arası paylaşılır).</summary>
    public async Task<List<Company>> WatchedCompaniesAsync(CancellationToken ct)
    {
        var ids = await _db.CompanyWatches.Select(w => w.CompanyId)
            .Union(_db.FavoriteCompanies.Select(f => f.CompanyId))
            .ToListAsync(ct);
        if (ids.Count == 0) return new List<Company>();

        return await _db.Companies.AsNoTracking()
            .Where(c => ids.Contains(c.Id)).OrderBy(c => c.Id).Take(_news.MaxWatchedCompanies).ToListAsync(ct);
    }

    public static string CompanyQuery(Company c, string window) =>
        $"\"{c.Name.Replace("\"", string.Empty).Trim()}\" when:{window}";

    public static string RadarQuery(RadarTopic t, string window) =>
        $"{t.Query.Trim()} {t.City?.Trim()} when:{window}".Replace("  ", " ");

    /// <summary>Bir tur çalıştırır; başka bir tur sürüyorsa null döner.</summary>
    public async Task<NewsScanRun?> RunAsync(CancellationToken ct)
    {
        if (!await Gate.WaitAsync(0, ct)) return null;
        var started = DateTime.UtcNow;
        var run = new NewsScanRun { StartedAt = started };
        _db.NewsScanRuns.Add(run);
        await _db.SaveChangesAsync(ct);

        try
        {
            var primary = _sources.FirstOrDefault(s => !s.IsFallback);
            var fallback = _news.SerperFallback ? _sources.FirstOrDefault(s => s.IsFallback) : null;

            if (_health.IsResting(started))
            {
                run.Error = $"Haber kaynağı dinleniyor ({_health.RestUntil?.ToLocalTime():HH:mm}'e kadar).";
                return run;
            }

            var blacklist = SearchBlacklist.Parse(await _settings.GetAsync(SettingKeys.ExtraBlockedDomains, ct));
            var knownHashes = (await _db.NewsItems.AsNoTracking()
                .Where(n => n.FetchedAt > started.AddDays(-30)).Select(n => n.UrlHash).ToListAsync(ct)).ToHashSet();
            var knownTitles = (await _db.NewsItems.AsNoTracking()
                .Where(n => n.FetchedAt > started.AddDays(-7)).Select(n => n.TitleKey).ToListAsync(ct)).ToHashSet();

            // Aynı olayın başka sitedeki/başka başlıklı kopyası kaydedilmesin (NewsSimilarity).
            var recentStories = (await _db.NewsItems.AsNoTracking().Where(n => n.PublishedAt > started.AddDays(-7))
                    .Select(n => new { n.Id, n.Title, n.Summary, n.PublishedAt, n.CompanyId }).ToListAsync(ct))
                .Select(n => (Story: new StoryItem(n.Id, n.PublishedAt, NewsSimilarity.Print(n.Title, n.Summary)), n.CompanyId))
                .ToList();
            var stories = recentStories.Select(s => s.Story).ToList();
            var storyIdf = NewsSimilarity.Idf(stories.Select(s => s.Print));
            var linkToCompany = new Dictionary<long, int>();
            var duplicates = 0;

            var companies = (await WatchedCompaniesAsync(ct)).Where(c => !blacklist.Matches(c.Domain, c.Name)).ToList();
            var topics = await _db.RadarTopics.AsNoTracking().Where(t => t.IsActive)
                .OrderBy(t => t.Id).Take(_news.MaxRadarTopics).ToListAsync(ct);

            var cursors = await _db.NewsScanCursors.ToListAsync(ct);
            DateTime? Last(string kind, int id) => cursors.FirstOrDefault(c => c.Kind == kind && c.RefId == id)?.LastScannedAt;

            var targets = companies.Select(c => new NewsTarget(NewsCursorKinds.Company, c.Id, Last(NewsCursorKinds.Company, c.Id)))
                .Concat(topics.Select(t => new NewsTarget(NewsCursorKinds.Topic, t.Id, Last(NewsCursorKinds.Topic, t.Id))))
                .ToList();
            var picked = NewsQueue.Pick(targets, started, _news.QueriesPerTick,
                TimeSpan.FromMinutes(_news.TopicMinutes), TimeSpan.FromMinutes(_news.CompanyMinMinutes));

            _status.BeginTick(picked.Count);
            var companyById = companies.ToDictionary(c => c.Id);
            var topicById = topics.ToDictionary(t => t.Id);

            var serperThisRun = 0;
            var added = new List<NewsFeedItem>();
            var done = 0;
            foreach (var target in picked)
            {
                ct.ThrowIfCancellationRequested();
                var company = target.Kind == NewsCursorKinds.Company ? companyById[target.RefId] : null;
                var topic = target.Kind == NewsCursorKinds.Topic ? topicById[target.RefId] : null;
                var query = company is not null ? CompanyQuery(company, _news.Window) : RadarQuery(topic!, _news.Window);
                _status.Progress(company?.Name ?? topic!.Query, done);
                run.Queries++;

                List<NewsHit>? hits = null;
                try
                {
                    hits = primary is null ? throw new InvalidOperationException("Ana haber kaynağı yok.") : await primary.SearchAsync(query, ct);
                    _health.RecordSuccess();
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Haber kaynağı hata verdi: {Query}", query);
                    if (_health.RecordFailure(DateTime.UtcNow))
                    {
                        run.Error = "Haber kaynağı üst üste hata verdi; 60 dakika dinlenecek.";
                        break; // tur kesilir; kursor güncellenmez, hedefler sonra yeniden denenir
                    }

                    // Yedek yalnızca bir dinlenmeden sonra ve tavan dahilinde kullanılır (engel kredi yakmasın).
                    if (fallback is not null && _health.FallbackAllowed(DateTime.UtcNow)
                        && serperThisRun < _news.MaxSerperPerRun && _health.TryUseSerper(DateTime.UtcNow, _news.MaxSerperPerDay))
                    {
                        try { hits = await fallback.SearchAsync(query, ct); serperThisRun++; run.SerperCredits++; }
                        catch (Exception ex2) when (ex2 is not OperationCanceledException) { _logger.LogWarning(ex2, "Yedek haber kaynağı da hata verdi."); }
                    }
                }

                if (hits is not null)
                {
                    Touch(cursors, target, DateTime.UtcNow);
                    foreach (var hit in hits)
                    {
                        run.Found++;
                        var text = $"{hit.Title}. {hit.Snippet}".Trim();
                        if (company is not null && !SnippetRelevance.IsAbout(company, text, hit.Url)) continue;
                        if (topic is not null && blacklist.Matches(DomainOf(hit.Url), hit.Title)) continue;

                        var hash = NewsDedup.UrlHash(hit.Url);
                        var titleKey = NewsDedup.TitleKey(hit.Title);
                        if (hash.Length == 0 || titleKey.Length < 8) continue;
                        if (!knownHashes.Add(hash) || !knownTitles.Add(titleKey)) continue;

                        var story = new StoryItem(-1 - added.Count, hit.PublishedUtc ?? DateTime.UtcNow, NewsSimilarity.Print(hit.Title, hit.Snippet));
                        if (NewsSimilarity.FindSame(story, stories, storyIdf) is { } same)
                        {
                            // Aynı olay zaten kayıtlı: kopya eklenmez. Kayıtlı haber radardan geldiyse bu firmaya bağlanır.
                            duplicates++;
                            if (company is not null && same.Id > 0 && recentStories.FirstOrDefault(s => s.Story.Id == same.Id).CompanyId is null)
                                linkToCompany.TryAdd(same.Id, company.Id);
                            continue;
                        }

                        var (category, importance) = NewsRuleClassifier.Classify(text, _research);
                        var item = new NewsFeedItem
                        {
                            CompanyId = company?.Id,
                            RadarTopicId = topic?.Id,
                            Title = Trim(hit.Title, 500)!,
                            Summary = Trim(hit.Snippet, 1000),
                            Url = Trim(hit.Url, 2000)!,
                            UrlHash = hash,
                            TitleKey = titleKey,
                            SourceName = Trim(hit.Source, 150),
                            PublishedAt = hit.PublishedUtc ?? DateTime.UtcNow,
                            FetchedAt = DateTime.UtcNow,
                            Category = category,
                            Importance = importance,
                            City = topic?.City,
                            Origin = company is not null ? NewsOrigins.Watch : NewsOrigins.Radar
                        };
                        _db.NewsItems.Add(item);
                        added.Add(item);
                        stories.Add(story);
                        run.NewCount++;
                    }
                }

                done++;
                _status.Progress(company?.Name ?? topic!.Query, done);
                if (_db.ChangeTracker.HasChanges()) await _db.SaveChangesAsync(ct);
                if (_news.RequestDelayMs > 0) await Task.Delay(_news.RequestDelayMs, ct);
            }

            if (duplicates > 0) _logger.LogInformation("Haber turu: {Count} kopya haber (aynı olay) atlandı.", duplicates);
            if (linkToCompany.Count > 0)
            {
                var ids = linkToCompany.Keys.ToList();
                foreach (var n in await _db.NewsItems.Where(n => ids.Contains(n.Id) && n.CompanyId == null).ToListAsync(ct))
                {
                    n.CompanyId = linkToCompany[n.Id];
                    n.Origin = NewsOrigins.Watch;
                }
                await _db.SaveChangesAsync(ct);
            }

            await EnrichAsync(added, companies, run, ct);
            await RefreshBoostsAsync(added, ct);
            await NotifyAsync(added, companies, ct);
            await CleanupAsync(started, ct);
        }
        catch (OperationCanceledException)
        {
            run.Error = "Tarama iptal edildi.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Haber taraması başarısız.");
            run.Error = Trim(ex.Message, 500);
        }
        finally
        {
            run.FinishedAt = DateTime.UtcNow;
            try { await _db.SaveChangesAsync(CancellationToken.None); }
            catch (Exception ex) { _logger.LogError(ex, "Haber tarama kaydı yazılamadı."); }
            _status.EndTick(DateTime.UtcNow, DateTime.UtcNow.AddMinutes(_news.TickMinutes), _health.RestUntil);
            Gate.Release();
        }

        _logger.LogInformation("Haber turu: {Queries} sorgu, {Found} sonuç, {New} yeni haber, {Credits} Serper kredisi.",
            run.Queries, run.Found, run.NewCount, run.SerperCredits);
        return run;
    }

    /// <summary>Hedefin son tarama zamanını yazar (kayıt yoksa oluşturur).</summary>
    private void Touch(List<NewsScanCursor> cursors, NewsTarget target, DateTime now)
    {
        var cursor = cursors.FirstOrDefault(c => c.Kind == target.Kind && c.RefId == target.RefId);
        if (cursor is null)
        {
            cursor = new NewsScanCursor { Kind = target.Kind, RefId = target.RefId };
            cursors.Add(cursor);
            _db.NewsScanCursors.Add(cursor);
        }
        cursor.LastScannedAt = now;
    }

    /// <summary>
    /// Olumlu haberden puan artışı: yeni takip haberi alan firmaların ve artışı 12 saatten eski firmaların
    /// <see cref="Company.NewsBoost"/> değeri son 30 güne göre yeniden hesaplanır; değişirse puan yeniden hesaplanır.
    /// </summary>
    private async Task RefreshBoostsAsync(List<NewsFeedItem> added, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var affected = added.Where(a => a.CompanyId is not null).Select(a => a.CompanyId!.Value).ToHashSet();
        var stale = await _db.Companies.AsNoTracking()
            .Where(c => c.NewsBoost > 0 && (c.NewsBoostAt == null || c.NewsBoostAt < now.AddHours(-12))).Select(c => c.Id).ToListAsync(ct);
        affected.UnionWith(stale);
        if (affected.Count == 0) return;

        var since = now.AddDays(-NewsBoostCalculator.WindowDays);
        var items = (await _db.NewsItems.AsNoTracking()
                .Where(n => n.CompanyId != null && affected.Contains(n.CompanyId.Value) && n.PublishedAt >= since)
                .Select(n => new { n.CompanyId, n.Category, n.Importance, n.PublishedAt }).ToListAsync(ct))
            .GroupBy(n => n.CompanyId!.Value).ToDictionary(g => g.Key, g => g.Select(n => (n.Category, n.Importance, n.PublishedAt)).ToList());

        var companies = await _db.Companies.Include(c => c.Contacts).Where(c => affected.Contains(c.Id)).ToListAsync(ct);
        foreach (var company in companies)
        {
            var boost = NewsBoostCalculator.Compute(items.TryGetValue(company.Id, out var list) ? list : new(), now);
            company.NewsBoostAt = now;
            if (boost == company.NewsBoost) continue;

            company.NewsBoost = boost;
            if (_icp is not null)
            {
                try { await _icp.ScoreAsync(company, CompanyController.ParseAnalysis(company.AiAnalysis), site: null, ct); }
                catch (Exception ex) when (ex is not OperationCanceledException) { _logger.LogWarning(ex, "Haber puanı yeniden hesaplanamadı: {Company}", company.Name); }
            }
        }
        if (_db.ChangeTracker.HasChanges()) await _db.SaveChangesAsync(ct);
    }

    /// <summary>14 günden eski tur kayıtları silinir (günde ~144 satır birikmesin).</summary>
    private async Task CleanupAsync(DateTime now, CancellationToken ct)
    {
        // Önceden kaydedilmiş (ya da yapay zekâ özetiyle sonradan benzeşen) kopyalar temizlenir.
        var dropped = await NewsDuplicateCleaner.RunAsync(_db, DateTime.UtcNow, ct);
        if (dropped > 0) _logger.LogInformation("Haber kopyası temizlendi: {Count} kayıt silindi.", dropped);

        var cutoff = now.AddDays(-14);
        var old = await _db.NewsScanRuns.Where(r => r.StartedAt < cutoff).ToListAsync(ct);
        if (old.Count == 0) return;
        _db.NewsScanRuns.RemoveRange(old);
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Yeni haberleri toplu yapay zekâ çağrısıyla özetler/sınıflar (tur başına en fazla <see cref="NewsOptions.MaxAiBatchesPerRun"/> çağrı).
    /// Günlük çağrı tavanı dolduysa ya da yapay zekâ hata verirse atlanır: haberler kural tabanlı kategori ve önemle görünmeye devam eder.
    /// </summary>
    private async Task EnrichAsync(List<NewsFeedItem> added, List<Company> companies, NewsScanRun run, CancellationToken ct)
    {
        if (_ai is null || added.Count == 0 || _news.AiBatchSize <= 0) return;

        var todayStart = NewsSlots.TodayStartUtc(DateTime.UtcNow);
        var usedToday = await _db.NewsScanRuns.AsNoTracking().Where(r => r.StartedAt >= todayStart && r.Id != run.Id).SumAsync(r => r.AiCalls, ct);

        var names = companies.ToDictionary(c => c.Id, c => c.Name);
        var candidates = added.Where(n => n.Origin == NewsOrigins.Radar || n.Importance >= 2)
            .OrderByDescending(n => n.Importance).Take(_news.AiBatchSize * Math.Max(1, _news.MaxAiBatchesPerRun)).ToList();

        foreach (var batch in candidates.Chunk(_news.AiBatchSize))
        {
            if (usedToday + run.AiCalls >= _news.MaxAiCallsPerDay) break;
            ct.ThrowIfCancellationRequested();

            var inputs = batch.Select((n, i) => new NewsAiInput(i, n.Title, n.Summary,
                n.CompanyId is int id && names.TryGetValue(id, out var nm) ? nm : null, n.Origin == NewsOrigins.Radar)).ToList();
            run.AiCalls++;
            var results = await _ai.EnrichNewsAsync(inputs, ct);

            foreach (var r in results)
            {
                if (r.Index < 0 || r.Index >= batch.Length) continue;
                var n = batch[r.Index];
                if (!string.IsNullOrWhiteSpace(r.Summary)) n.Summary = Trim(r.Summary, 300);
                if (r.Category is not null && NewsCategories.All.Contains(r.Category)) n.Category = r.Category;
                n.Importance = r.Importance;
                if (n.Origin == NewsOrigins.Radar)
                {
                    n.MentionedCompany = Trim(r.MentionedCompany, 200);
                    n.City ??= Trim(r.City, 100);
                }
            }
        }

        if (_db.ChangeTracker.HasChanges()) await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Önemi 4 ve üstü haberler için zil bildirimi: takip haberi → o firmayı izleyen kullanıcılar (takip + favori);
    /// radar haberi (yalnızca önem 5) → tüm aktif kullanıcılar.
    /// </summary>
    private async Task NotifyAsync(List<NewsFeedItem> added, List<Company> companies, CancellationToken ct)
    {
        if (_notifications is null) return;
        var hot = added.Where(n => n.Importance >= 4 && (n.Origin == NewsOrigins.Watch || n.Importance >= 5)).ToList();
        if (hot.Count == 0) return;

        var names = companies.ToDictionary(c => c.Id, c => c.Name);
        var activeUsers = await _db.Users.AsNoTracking().Where(u => u.IsActive).Select(u => u.Id).ToListAsync(ct);
        var watchers = (await _db.CompanyWatches.AsNoTracking().Select(w => new { w.UserId, w.CompanyId }).ToListAsync(ct))
            .Concat((await _db.FavoriteCompanies.AsNoTracking().Select(f => new { f.UserId, f.CompanyId }).ToListAsync(ct)))
            .GroupBy(x => x.CompanyId).ToDictionary(g => g.Key, g => g.Select(x => x.UserId).Distinct().ToList());

        foreach (var n in hot.Take(10))
        {
            var recipients = n.CompanyId is int cid ? (watchers.TryGetValue(cid, out var w) ? w : new List<int>()) : activeUsers;
            var company = n.CompanyId is int id && names.TryGetValue(id, out var nm) ? nm : n.MentionedCompany ?? n.City ?? "Radar";
            foreach (var userId in recipients.Where(activeUsers.Contains))
                _notifications.AddNews(userId, company, n.Title);
        }
        if (_db.ChangeTracker.HasChanges()) await _db.SaveChangesAsync(ct);
    }

    private static string? DomainOf(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host : null;

    private static string? Trim(string? s, int max) =>
        string.IsNullOrWhiteSpace(s) ? null : (s.Trim().Length > max ? s.Trim()[..max] : s.Trim());
}

/// <summary>Arka plan ajanı: her <see cref="NewsOptions.TickMinutes"/> dakikada bir dönüşümlü tarama turu çalıştırır.</summary>
public class NewsWatchWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly NewsOptions _options;
    private readonly NewsAgentStatus _status;
    private readonly ILogger<NewsWatchWorker> _logger;

    public NewsWatchWorker(IServiceScopeFactory scopes, IOptions<NewsOptions> options, NewsAgentStatus status, ILogger<NewsWatchWorker> logger)
    {
        _scopes = scopes;
        _options = options.Value;
        _status = status;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _status.Disabled();
            return;
        }

        // Açılışta bir dakika bekle: uygulama ısınsın.
        _status.Schedule(DateTime.UtcNow.AddMinutes(1));
        try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
        catch (OperationCanceledException) { return; }

        var tick = TimeSpan.FromMinutes(Math.Max(1, _options.TickMinutes));
        using var timer = new PeriodicTimer(tick);
        do
        {
            try
            {
                using var scope = _scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<NewsScanService>().RunAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Haber ajanı turu başarısız.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
