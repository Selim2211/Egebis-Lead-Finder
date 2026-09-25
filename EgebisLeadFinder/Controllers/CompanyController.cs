using System.Text.Json;
using EgebisLeadFinder.Configuration;
using EgebisLeadFinder.Data;
using EgebisLeadFinder.Models;
using EgebisLeadFinder.Services;
using EgebisLeadFinder.Services.Progress;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EgebisLeadFinder.Controllers;

public class CompanyController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly LeadDiscoveryService _discovery;
    private readonly LeadScoringService _scoring;
    private readonly IContactEnrichmentService _enrichment;
    private readonly IPersonEmailFinder _emailFinder;
    private readonly ICompanyResearchService _research;
    private readonly JobProgressStore _progress;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ResearchOptions _researchOptions;
    private readonly ISettingsService _settings;
    private readonly IWebScraperService _scraper;
    private readonly IAiService _ai;
    private readonly IcpService _icp;
    private readonly ILogger<CompanyController> _logger;

    public CompanyController(
        ApplicationDbContext db,
        LeadDiscoveryService discovery,
        LeadScoringService scoring,
        IContactEnrichmentService enrichment,
        IPersonEmailFinder emailFinder,
        ICompanyResearchService research,
        JobProgressStore progress,
        IServiceScopeFactory scopeFactory,
        IOptions<ResearchOptions> researchOptions,
        ISettingsService settings,
        IWebScraperService scraper,
        IAiService ai,
        ILogger<CompanyController> logger,
        IcpService icp)
    {
        _icp = icp;
        _settings = settings;
        _scraper = scraper;
        _ai = ai;
        _db = db;
        _discovery = discovery;
        _scoring = scoring;
        _enrichment = enrichment;
        _emailFinder = emailFinder;
        _research = research;
        _progress = progress;
        _scopeFactory = scopeFactory;
        _researchOptions = researchOptions.Value;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Search(CancellationToken ct)
    {
        var model = new CompanySearchViewModel();
        await FillSearchPageAsync(model, ct);
        return View(model);
    }

    /// <summary>Arama sayfasinin ortak verileri: kayitli profiller + Ayarlar'daki arama limitleri.</summary>
    private async Task FillSearchPageAsync(CompanySearchViewModel model, CancellationToken ct)
    {
        var (maxCompanies, region) = await GetSearchLimitsAsync(ct);
        model.MaxCompanies = maxCompanies;
        model.Region = region;
        model.DefaultCountry = region.Name;
        model.Profiles = await _db.SearchProfiles.AsNoTracking()
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(ct);
    }

    private async Task<(int MaxCompanies, SearchRegion Region)> GetSearchLimitsAsync(CancellationToken ct)
    {
        var max = int.TryParse(await _settings.GetAsync(SettingKeys.SearchMaxCompanies, ct), out var m) && m > 0
            ? Math.Min(m, SettingKeys.SearchMaxCompaniesUpperLimit)
            : SettingKeys.DefaultSearchMaxCompanies;

        return (max, await GetRegionAsync(_settings, ct));
    }

    /// <summary>
    /// Veritabanindaki ulke adlarini bolge anahtarlarina cevirip sayilari toplar
    /// ("Türkiye" → TR). Taninmayan ulkeler atlanir.
    /// </summary>
    public static Dictionary<string, int> CountryCounts(IEnumerable<(string? Country, int Count)> rows)
    {
        var result = new Dictionary<string, int>();

        foreach (var (country, count) in rows)
        {
            var region = SearchRegions.ByCountry(country);
            if (region is null) continue;

            result[region.Key] = result.GetValueOrDefault(region.Key) + count;
        }

        return result;
    }

    /// <summary>Ayarlar'da secili arama bolgesi; secim yoksa Türkiye.</summary>
    public static async Task<SearchRegion> GetRegionAsync(ISettingsService settings, CancellationToken ct)
    {
        var key = await settings.GetAsync(SettingKeys.SearchRegion, ct);
        if (SearchRegions.IsKnown(key)) return SearchRegions.Get(key);

        // Eski kurulumlarda yalnizca ulke adi kayitliydi.
        var country = await settings.GetAsync(SettingKeys.SearchDefaultCountry, ct);
        return SearchRegions.ByCountry(country) ?? SearchRegions.Default;
    }

    /// <summary>
    /// Formdan gelen kriterleri moda gore normalize eder. Azami firma ve ulke her
    /// zaman Ayarlar'dan gelir (formdan gelen deger yok sayilir). Temel aramada il
    /// ve profil yoktur: ulke genelinde taranir.
    /// </summary>
    private async Task<string?> PrepareCriteriaAsync(CompanySearchViewModel model, CancellationToken ct)
    {
        var c = model.Criteria;
        var (maxCompanies, defaultRegion) = await GetSearchLimitsAsync(ct);

        // Bolge her aramada formdan secilebilir; secim yoksa Ayarlar'daki bolge kullanilir.
        var region = SearchRegions.IsKnown(c.RegionKey) ? SearchRegions.Get(c.RegionKey) : defaultRegion;
        c.Country = region.Country;
        c.RegionKey = region.Key;
        model.Region = region;
        model.DefaultCountry = region.Name;

        if (model.SubmitMode == CompanySearchViewModel.ModeCompany)
        {
            if (string.IsNullOrWhiteSpace(c.CompanyName))
                return "Aranacak firma adını yazın.";

            c.CompanyName = c.CompanyName.Trim();
            c.Industry = string.Empty;
            c.City = null;
            c.ProfileName = null;
            c.MaxCompanies = CompanySearchViewModel.NameSearchMaxResults;
            return null;
        }

        c.CompanyName = null;
        if (string.IsNullOrWhiteSpace(c.Industry))
            return "Sektör alanı zorunludur.";

        c.Industry = c.Industry.Trim();
        c.MaxCompanies = maxCompanies;

        switch (model.SubmitMode)
        {
            case CompanySearchViewModel.ModeAdvancedSave:
                if (string.IsNullOrWhiteSpace(c.ProfileName))
                    return "Profili kaydetmek için bir profil adı girin.";
                break;
            case CompanySearchViewModel.ModeAdvanced:
                c.ProfileName = null;
                break;
            default:
                c.City = null;
                c.ProfileName = null;
                break;
        }

        return null;
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Search(CompanySearchViewModel model, CancellationToken ct)
    {
        await FillSearchPageAsync(model, ct);

        var error = await PrepareCriteriaAsync(model, ct);
        if (error is not null)
        {
            model.Error = error;
            return View(model);
        }

        try
        {
            var profile = await SaveProfileAsync(model.Criteria, ct);
            model.Result = await _discovery.RunAsync(model.Criteria, ct);
            if (profile is not null)
            {
                model.Result.ProfileId = profile.Id;
                model.Result.ProfileName = profile.Name;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Firma araması başarısız.");
            model.Error = ex.Message;
        }

        return View(model);
    }

    /// <summary>
    /// Criteria.ProfileName doluysa ayni isimli profili kriterlerle gunceller, yoksa
    /// olusturur. Olusturma tarihi (CreatedAt) ilk kayitta sabitlenir. Ad bossa null.
    /// </summary>
    private async Task<SearchProfile?> SaveProfileAsync(SearchCriteria criteria, CancellationToken ct)
    {
        var name = criteria.ProfileName?.Trim();
        if (string.IsNullOrWhiteSpace(name)) return null;
        if (name.Length > 150) name = name[..150].TrimEnd();

        // Duzenleme: ayni Id'li kayit bulunursa yeniden adlandirma dahil uzerine yazilir.
        // Yeni ad baska bir profilde varsa o profil guncellenir (eski davranis).
        SearchProfile? profile = null;
        if (criteria.ProfileId is int editId)
        {
            profile = await _db.SearchProfiles.FirstOrDefaultAsync(p => p.Id == editId, ct);
            if (profile is not null
                && await _db.SearchProfiles.AnyAsync(p => p.Name == name && p.Id != editId, ct))
                profile = null;
            if (profile is not null) profile.Name = name;
        }

        profile ??= await _db.SearchProfiles.FirstOrDefaultAsync(p => p.Name == name, ct);
        if (profile is null)
        {
            profile = new SearchProfile { Name = name };
            _db.SearchProfiles.Add(profile);
        }

        profile.Industry = criteria.Industry;
        profile.City = criteria.City;
        profile.Country = criteria.Country;
        profile.RegionKey = criteria.RegionKey;

        await _db.SaveChangesAsync(ct);
        return profile;
    }

    /// <summary>Gelismis arama ekranindaki "Profili Kaydet": arama yapmadan sadece profili kaydeder.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveProfile([FromForm] CompanySearchViewModel model, CancellationToken ct)
    {
        model.SubmitMode = CompanySearchViewModel.ModeAdvancedSave;
        var error = await PrepareCriteriaAsync(model, ct);
        if (error is not null)
            return BadRequest(new { error });

        var profile = (await SaveProfileAsync(model.Criteria, ct))!;
        return Json(new
        {
            id = profile.Id,
            name = profile.Name,
            industry = profile.Industry,
            city = profile.City,
            regionKey = profile.RegionKey,
            regionName = SearchRegions.Get(profile.RegionKey).Name,
            createdAt = profile.CreatedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm")
        });
    }

    // ============ İlerleme göstergeli (arka planda çalışan) firma araması ============

    /// <summary>
    /// Aramayi arka planda baslatir ve bir is kimligi doner. Tarayici bu kimlikle
    /// <see cref="JobStatus"/>'u yoklayarak yuzde ilerlemeyi gosterir. JavaScript
    /// kapaliysa form dogrudan <see cref="Search"/> POST'una duser (kademeli iyilestirme).
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StartSearch([FromForm] CompanySearchViewModel model, CancellationToken ct)
    {
        var error = await PrepareCriteriaAsync(model, ct);
        if (error is not null)
            return BadRequest(new { error });

        var profile = await SaveProfileAsync(model.Criteria, ct);

        var job = _progress.Create();
        var criteria = model.Criteria;
        var resultUrl = Url.Action(nameof(SearchResult), new { jobId = job.Id })!;

        _ = Task.Run(async () =>
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var discovery = scope.ServiceProvider.GetRequiredService<LeadDiscoveryService>();
            var reporter = new Progress<JobStep>(s => _progress.Report(job.Id, s.Percent, s.Stage, s.Detail));

            try
            {
                var result = await discovery.RunAsync(criteria, CancellationToken.None, reporter);
                if (profile is not null)
                {
                    result.ProfileId = profile.Id;
                    result.ProfileName = profile.Name;
                }
                _progress.Complete(job.Id, resultUrl, result);
            }
            catch (MissingApiKeyException ex)
            {
                _progress.Fail(job.Id, ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Firma araması başarısız (arka plan).");
                _progress.Fail(job.Id, ex.Message);
            }
        });

        return Json(new { jobId = job.Id, progressUrl = Url.Action(nameof(JobStatus), new { jobId = job.Id }) });
    }

    /// <summary>Bir arka plan isinin anlik durumu (yuzde, asama, hata, sonuc adresi).</summary>
    [HttpGet]
    public IActionResult JobStatus(string jobId)
    {
        var job = _progress.Get(jobId);
        if (job is null)
            return NotFound(new { error = "İş bulunamadı veya süresi doldu." });

        return Json(new
        {
            percent = job.Percent,
            stage = job.Stage,
            detail = job.Detail,
            state = job.State.ToString().ToLowerInvariant(),
            error = job.Error,
            resultUrl = job.ResultUrl
        });
    }

    /// <summary>Tamamlanan aramanin sonuc ozetini gosterir (Search görünümü yeniden kullanılır).</summary>
    [HttpGet]
    public async Task<IActionResult> SearchResult(string jobId, CancellationToken ct)
    {
        var job = _progress.Get(jobId);
        if (job?.Payload is not DiscoveryResult result)
            return RedirectToAction(nameof(Search));

        var model = new CompanySearchViewModel { Result = result };
        await FillSearchPageAsync(model, ct);
        return View(nameof(Search), model);
    }

    /// <summary>Bu sure icinde tekrar arastirma istegi atlanir (kredi korumasi).</summary>
    private const int ResearchCacheHours = 24;

    /// <summary>Ön araştırma + finansal analizi arka planda baslatir; is kimligi doner.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StartResearch(int id, bool force, CancellationToken ct)
    {
        var company = await _db.Companies.AsNoTracking()
            .Select(c => new { c.Id, c.RatedAt })
            .FirstOrDefaultAsync(c => c.Id == id, ct);

        if (company is null) return NotFound();

        // Ayni firmayi gun icinde tekrar arastirmak Serper kredisi + AI cagrisi
        // harcar; kullanici bilerek "zorla" demedikce mevcut sonuc korunur.
        if (!force && company.RatedAt is not null
            && company.RatedAt > DateTime.UtcNow.AddHours(-ResearchCacheHours))
        {
            TempData["ResearchInfo"] =
                $"Bu firma {company.RatedAt.Value.ToLocalTime():dd.MM.yyyy HH:mm} tarihinde araştırıldı; " +
                "mevcut sonuç gösteriliyor. Yenilemek için \"Yeniden araştır (zorla)\" seçeneğini kullanın.";

            return Json(new { skipped = true, resultUrl = Url.Action(nameof(Details), new { id }) });
        }

        var job = _progress.Create();
        var resultUrl = Url.Action(nameof(Details), new { id })!;

        _ = Task.Run(async () =>
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var research = scope.ServiceProvider.GetRequiredService<ICompanyResearchService>();
            var reporter = new Progress<JobStep>(s => _progress.Report(job.Id, s.Percent, s.Stage, s.Detail));

            try
            {
                var company = await db.Companies.FirstAsync(c => c.Id == id);
                var result = await research.ResearchAsync(company, CancellationToken.None, reporter);

                if (!result.Success)
                {
                    _progress.Fail(job.Id, result.Error ?? "Ön araştırma tamamlanamadı.");
                    return;
                }

                company.RatingJson = result.RawJson;
                company.RatingSignal = result.Evaluation!.Signal.ToString();
                company.RatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();

                _progress.Complete(job.Id, resultUrl);
            }
            catch (MissingApiKeyException ex)
            {
                _progress.Fail(job.Id, ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ön araştırma başarısız (arka plan): {CompanyId}", id);
                _progress.Fail(job.Id, $"Ön araştırma başarısız: {ex.Message}");
            }
        });

        return Json(new { jobId = job.Id, progressUrl = Url.Action(nameof(JobStatus), new { jobId = job.Id }) });
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? search, int minScore = 0, bool onlyWithoutLead = false,
        int? profileId = null, string? stage = null, string? signal = null, string? sort = null,
        [FromQuery(Name = "region")] string[]? regions = null, [FromQuery(Name = "city")] string[]? cities = null,
        [FromQuery(Name = "country")] string[]? countries = null, string? nace = null, bool icp = false,
        int page = 1, CancellationToken ct = default)
    {
        var geo = GeoFilter.From(regions, cities, countries);
        if (signal is not ("guclu" or "incelenmeli" or "riskli" or "none")) signal = null;
        sort = CompanySort.Options.Any(o => o.Key == sort) ? sort! : CompanySort.Default;
        nace = NaceCatalog.DivisionCode(nace);
        var query = await FilterCompaniesAsync(search, minScore, onlyWithoutLead, profileId, stage, signal, geo, nace, icp, ct);
        var ordered = OrderCompanies(query, sort);

        var total = await query.CountAsync(ct);
        page = PagerModel.Clamp(page, total);

        // Il/ulke secimindeki sayilar: diger filtrelerden bagimsiz, tum firmalar uzerinden.
        geo.CityCounts = await _db.Companies.AsNoTracking()
            .Where(c => c.City != null)
            .GroupBy(c => c.City!)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        var countryRows = await _db.Companies.AsNoTracking()
            .Where(c => c.Country != null)
            .GroupBy(c => c.Country!)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(ct);

        geo.CountryCounts = CountryCounts(countryRows.Select(r => ((string?)r.Key, r.Count)));

        var model = new CompanyListViewModel
        {
            Search = search,
            MinScore = minScore,
            OnlyWithoutLead = onlyWithoutLead,
            ProfileId = profileId,
            Stage = stage,
            Signal = signal,
            Nace = nace,
            Icp = icp,
            NaceCounts = await _db.Companies.AsNoTracking()
                .Where(c => c.NaceCode != null)
                .GroupBy(c => c.NaceCode!.Substring(0, 2))
                .Select(g => new { g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.Count, ct),
            IcpCount = await _db.Companies.CountAsync(c => c.IcpMatch, ct),
            Sort = sort,
            Geo = geo,
            Pager = new PagerModel { Page = page, TotalItems = total },
            Profiles = await _db.SearchProfiles.AsNoTracking()
                .OrderByDescending(p => p.CreatedAt)
                .ToListAsync(ct),
            Companies = await ordered
                .ThenBy(c => c.Id)
                .Skip((page - 1) * PagerModel.DefaultPageSize)
                .Take(PagerModel.DefaultPageSize)
                .Include(c => c.Contacts)
                .Include(c => c.Leads)
                .AsSplitQuery()
                .ToListAsync(ct)
        };

        var counts = await _db.Companies.AsNoTracking()
            .GroupBy(_ => 1)
            .Select(g => new
            {
                All = g.Count(),
                Contacted = g.Count(c => c.ContactedAt != null),
                Mailed = g.Count(c => c.EmailSentAt != null),
                Project = g.Count(c => c.ProjectStartedAt != null),
                Untouched = g.Count(c => c.ContactedAt == null && c.EmailSentAt == null && c.ProjectStartedAt == null)
            })
            .FirstOrDefaultAsync(ct);

        if (counts is not null)
        {
            model.StageCounts[""] = counts.All;
            model.StageCounts[CompanyStage.Contacted] = counts.Contacted;
            model.StageCounts[CompanyStage.Mailed] = counts.Mailed;
            model.StageCounts[CompanyStage.Project] = counts.Project;
            model.StageCounts[CompanyStage.Untouched] = counts.Untouched;
        }

        return View(model);
    }

    /// <summary>Firmalar listesindeki filtrelerle Excel (firmalar + kisiler sayfasi) veya CSV indirir.</summary>
    [HttpGet]
    public async Task<IActionResult> Export(string format, string? search, int minScore = 0, bool onlyWithoutLead = false,
        int? profileId = null, string? stage = null, string? signal = null, string? sort = null,
        [FromQuery(Name = "region")] string[]? regions = null, [FromQuery(Name = "city")] string[]? cities = null,
        [FromQuery(Name = "country")] string[]? countries = null, string? nace = null, bool icp = false,
        CancellationToken ct = default)
    {
        var geo = GeoFilter.From(regions, cities, countries);
        if (signal is not ("guclu" or "incelenmeli" or "riskli" or "none")) signal = null;
        sort = CompanySort.Options.Any(o => o.Key == sort) ? sort! : CompanySort.Default;

        var query = await FilterCompaniesAsync(search, minScore, onlyWithoutLead, profileId, stage, signal, geo,
            NaceCatalog.DivisionCode(nace), icp, ct);
        var companies = await OrderCompanies(query, sort).ThenBy(c => c.Id)
            .Take(ExportService.MaxRows)
            .Include(c => c.Contacts)
            .Include(c => c.Leads)
            .AsSplitQuery()
            .ToListAsync(ct);

        var tables = ExportService.CompanyTables(companies);
        var stamp = DateTime.Now.ToString("yyyy-MM-dd-HHmm");

        return format == "csv"
            ? File(ExportService.ToCsv(tables[0]), "text/csv; charset=utf-8", $"firmalar-{stamp}.csv")
            : File(ExportService.ToXlsx(tables),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"firmalar-{stamp}.xlsx");
    }

    /// <summary>Tek firmanin Excel (firma + kisiler sayfasi) veya CSV dosyasi.</summary>
    [HttpGet]
    public async Task<IActionResult> ExportOne(int id, string format, CancellationToken ct)
    {
        var company = await _db.Companies.AsNoTracking()
            .Include(c => c.Contacts)
            .Include(c => c.Leads)
            .AsSplitQuery()
            .FirstOrDefaultAsync(c => c.Id == id, ct);
        if (company is null) return NotFound();

        var tables = ExportService.CompanyTables(new[] { company });
        var stamp = DateTime.Now.ToString("yyyy-MM-dd-HHmm");
        var slug = new string(company.Name.Where(char.IsLetterOrDigit).Take(40).ToArray());
        if (slug.Length == 0) slug = "firma";

        return format == "csv"
            ? File(ExportService.ToCsv(tables[0]), "text/csv; charset=utf-8", $"{slug}-{stamp}.csv")
            : File(ExportService.ToXlsx(tables),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"{slug}-{stamp}.xlsx");
    }

    /// <summary>Firmalar listesi ve disa aktarim ayni suzgeci kullanir: ekranda ne varsa o iner.</summary>
    private async Task<IQueryable<Company>> FilterCompaniesAsync(string? search, int minScore, bool onlyWithoutLead,
        int? profileId, string? stage, string? signal, GeoFilter geo, string? nace, bool icp, CancellationToken ct)
    {
        var query = _db.Companies.AsNoTracking().Where(c => c.Score >= minScore);

        if (nace is not null) query = query.Where(c => c.NaceCode != null && c.NaceCode.StartsWith(nace));
        if (icp) query = query.Where(c => c.IcpMatch);

        if (geo.Countries.Count > 0)
        {
            var names = geo.CountryNames();
            query = query.Where(c => c.Country != null && names.Contains(c.Country));
        }

        if (geo.Regions.Count > 0 || geo.Cities.Count > 0)
        {
            var geoCities = geo.EffectiveCities();
            query = query.Where(c => c.City != null && geoCities.Contains(c.City));
        }

        query = signal switch
        {
            "guclu" => query.Where(c => c.RatingSignal == "Guclu"),
            "incelenmeli" => query.Where(c => c.RatingSignal == "Incelenmeli"),
            "riskli" => query.Where(c => c.RatingSignal == "Riskli"),
            "none" => query.Where(c => c.RatingSignal == null || c.RatingSignal == "Bilinmiyor"),
            _ => query
        };

        query = stage switch
        {
            CompanyStage.Contacted => query.Where(c => c.ContactedAt != null),
            CompanyStage.Mailed => query.Where(c => c.EmailSentAt != null),
            CompanyStage.Project => query.Where(c => c.ProjectStartedAt != null),
            CompanyStage.Untouched => query.Where(c =>
                c.ContactedAt == null && c.EmailSentAt == null && c.ProjectStartedAt == null),
            _ => query
        };

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = $"%{search}%";
            query = query.Where(c =>
                EF.Functions.ILike(c.Name, term) ||
                (c.Industry != null && EF.Functions.ILike(c.Industry, term)) ||
                (c.Domain != null && EF.Functions.ILike(c.Domain, term)));
        }

        if (onlyWithoutLead)
            query = query.Where(c => !c.Leads.Any());

        if (profileId is > 0)
        {
            var companyIds = await _db.CompanySearchProfiles
                .Where(x => x.SearchProfileId == profileId)
                .Select(x => x.CompanyId)
                .ToListAsync(ct);
            query = query.Where(c => companyIds.Contains(c.Id));
        }

        return query;
    }

    private static IOrderedQueryable<Company> OrderCompanies(IQueryable<Company> query, string sort)
    {
        return sort switch
        {
            "score_asc" => query.OrderBy(c => c.Score).ThenBy(c => c.Name),
            "newest" => query.OrderByDescending(c => c.CreatedAt),
            "oldest" => query.OrderBy(c => c.CreatedAt),
            "name" => query.OrderBy(c => c.Name),
            "name_desc" => query.OrderByDescending(c => c.Name),
            "contacts" => query.OrderByDescending(c => c.Contacts.Count).ThenByDescending(c => c.Score),
            "city" => query.OrderBy(c => c.City == null).ThenBy(c => c.City).ThenByDescending(c => c.Score),
            _ => query.OrderByDescending(c => c.Score).ThenBy(c => c.Name)
        };
    }

    /// <summary>
    /// Firma kartindaki satis takibi butonlari: iletisim kuruldu / mail atildi /
    /// proje basladi. Ayni butona tekrar basmak isareti kaldirir (yanlis tiklama).
    /// JS acikken fetch ile cagrilir ve JSON doner; kapaliysa sayfaya geri yonlendirir.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleStage(int id, string stage, string? returnUrl, CancellationToken ct)
    {
        var company = await _db.Companies.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (company is null) return NotFound();

        DateTime? Toggle(DateTime? value) => value is null ? DateTime.UtcNow : null;

        switch (stage)
        {
            case CompanyStage.Contacted: company.ContactedAt = Toggle(company.ContactedAt); break;
            case CompanyStage.Mailed: company.EmailSentAt = Toggle(company.EmailSentAt); break;
            case CompanyStage.Project: company.ProjectStartedAt = Toggle(company.ProjectStartedAt); break;
            default: return BadRequest(new { error = "Bilinmeyen aşama." });
        }

        await _db.SaveChangesAsync(ct);

        var at = stage switch
        {
            CompanyStage.Contacted => company.ContactedAt,
            CompanyStage.Mailed => company.EmailSentAt,
            _ => company.ProjectStartedAt
        };

        if (WantsJson())
            return Json(new { active = at is not null, at = at?.ToLocalTime().ToString("dd.MM.yyyy") });

        return RedirectToLocal(returnUrl);
    }

    /// <summary>Firmayi kisileri, lead'leri ve profil baglantilariyla birlikte kalici olarak siler.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteCompany(int id, string? returnUrl, CancellationToken ct)
    {
        var company = await _db.Companies.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (company is null) return NotFound();

        _db.Companies.Remove(company);
        await _db.SaveChangesAsync(ct);

        if (WantsJson())
            return Json(new { deleted = true, name = company.Name });

        TempData["SettingsSaved"] = $"\"{company.Name}\" silindi.";
        return RedirectToLocal(returnUrl);
    }

    /// <summary>Firmayi (ve kisilerini) Salesforce'a Account/Contact olarak gonderir.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SyncToSalesforce(
        int id, string? returnUrl, [FromServices] SalesforceSyncRunner salesforce, CancellationToken ct)
    {
        var result = await salesforce.SyncCompanyAsync(id, ct);
        if (result is null) return NotFound();

        if (WantsJson())
            return Json(new { success = result.Success, error = result.Error, salesforceId = result.SalesforceId });

        var name = await _db.Companies.Where(c => c.Id == id).Select(c => c.Name).FirstOrDefaultAsync(ct);
        TempData[result.Success ? "SettingsSaved" : "SettingsError"] = result.Success
            ? $"\"{name}\" Salesforce'a gönderildi. Bundan sonraki değişiklikler otomatik aktarılacak."
            : $"Salesforce'a gönderilemedi: {result.Error}";

        return RedirectToLocal(returnUrl);
    }

    private bool WantsJson() =>
        Request.Headers.Accept.Any(a => a?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true);

    private IActionResult RedirectToLocal(string? returnUrl) =>
        !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? Redirect(returnUrl)
            : RedirectToAction(nameof(Index));

    /// <summary>
    /// Arama sonucu ekranindaki secili firmalari bir arama profilinin listesine ekler.
    /// Firmalar zaten kaydedilmis olur (LeadDiscoveryService her aramada otomatik
    /// kaydeder); bu sadece profile "etiketleme" islemidir.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveToProfile(int profileId, int[] companyIds, CancellationToken ct)
    {
        var profile = await _db.SearchProfiles.FindAsync(new object[] { profileId }, ct);
        if (profile is null) return NotFound();

        if (companyIds.Length > 0)
        {
            var already = await _db.CompanySearchProfiles
                .Where(x => x.SearchProfileId == profileId && companyIds.Contains(x.CompanyId))
                .Select(x => x.CompanyId)
                .ToListAsync(ct);

            var toAdd = companyIds.Except(already)
                .Select(id => new CompanySearchProfile { CompanyId = id, SearchProfileId = profileId });

            _db.CompanySearchProfiles.AddRange(toAdd);
            await _db.SaveChangesAsync(ct);
        }

        TempData["SettingsSaved"] = companyIds.Length > 0
            ? $"{companyIds.Length} firma \"{profile.Name}\" profiline kaydedildi."
            : "Hiçbir firma seçilmedi.";

        return RedirectToAction(nameof(Index), new { profileId });
    }

    /// <summary>
    /// Profili siler (firmalari degil — sadece etiketleme kaydini). Yanlislikla
    /// veya test amacli olusturulmus profilleri temizlemek icin.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteProfile(int profileId, CancellationToken ct)
    {
        var profile = await _db.SearchProfiles.FindAsync(new object[] { profileId }, ct);
        if (profile is null) return NotFound();

        _db.SearchProfiles.Remove(profile);
        await _db.SaveChangesAsync(ct);

        TempData["SettingsSaved"] = $"\"{profile.Name}\" profili silindi (firmalar silinmedi).";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        var model = await BuildDetailAsync(id, ct);
        return model is null ? NotFound() : View(model);
    }

    /// <summary>Detay ve rapor ekranlarinin ortak modeli.</summary>
    private async Task<CompanyDetailViewModel?> BuildDetailAsync(int id, CancellationToken ct)
    {
        var company = await _db.Companies
            .Include(c => c.Contacts)
            .Include(c => c.Leads)
            .FirstOrDefaultAsync(c => c.Id == id, ct);

        if (company is null) return null;

        var analysis = ParseAnalysis(company.AiAnalysis);
        var breakdown = _scoring.ScoreCompany(analysis, site: null, company.Contacts, await _icp.GetAsync(ct), company);

        var model = new CompanyDetailViewModel
        {
            Company = company,
            Analysis = analysis,
            Leads = company.Leads.OrderByDescending(l => l.CreatedAt).ToList(),
            BestContact = _scoring.PickBestContact(company.Contacts),
            Rating = BuildRatingViewModel(company),
            Breakdown = new ScoreBreakdownViewModel
            {
                Items = breakdown.Items,
                DisqualifiedReason = breakdown.DisqualifiedReason,
                Unverified = breakdown.Unverified,
                // Kayitli puan arama anindaki site verisiyle hesaplandi;
                // burada yeniden hesaplanan degil, kaydedilen puan gosterilir.
                Total = company.Score
            }
        };

        return model;
    }

    /// <summary>
    /// Tek sayfalik, yazdirilabilir firma raporu (gorusmeye goturulebilir).
    /// Detay ekraniyla ayni veriyi kullanir, sade bir duzende basar.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Report(int id, CancellationToken ct)
    {
        var model = await BuildDetailAsync(id, ct);
        return model is null ? NotFound() : View(model);
    }

    /// <summary>
    /// Firma sitesini yeniden okuyup Gemini analizini tekrar dener. Ilk aramada
    /// Gemini kotasi/gecici hatasi yuzunden analiz basarisiz kalan firmalar icin
    /// (bkz. Company.ProcessingError). Arama gibi arka plan isi gerektirmez: tek
    /// firma, tek site okuma + tek AI cagrisi, birkaç saniye surer.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReanalyzeAi(int id, CancellationToken ct)
    {
        var company = await _db.Companies
            .Include(c => c.Contacts)
            .FirstOrDefaultAsync(c => c.Id == id, ct);

        if (company is null) return NotFound();

        if (string.IsNullOrWhiteSpace(company.Website))
        {
            TempData["EnrichError"] = "Firmanın web sitesi kayıtlı değil, analiz edilemez.";
            return RedirectToAction(nameof(Details), new { id });
        }

        try
        {
            var site = await _scraper.ScrapeAsync(company.Website, ct);
            if (!site.Success)
            {
                TempData["EnrichError"] = $"Site okunamadı: {site.Error}";
                return RedirectToAction(nameof(Details), new { id });
            }

            var aiResult = await _ai.AnalyzeCompanyAsync(site.Text, ct);
            if (!aiResult.Success)
            {
                TempData["EnrichError"] = $"Yapay zeka analizi yine başarısız: {aiResult.Error}";
                return RedirectToAction(nameof(Details), new { id });
            }

            company.AiAnalysis = aiResult.RawJson;
            company.ProcessingError = null;

            if (!string.IsNullOrWhiteSpace(aiResult.Analysis!.Industry))
                company.Industry = aiResult.Analysis.Industry;
            if (!string.IsNullOrWhiteSpace(aiResult.Analysis.CompanyName))
                company.Name = aiResult.Analysis.CompanyName;

            StringLengthGuard.Apply(company);
            await _icp.ScoreAsync(company, aiResult.Analysis, site, ct);

            await _db.SaveChangesAsync(ct);
            TempData["EnrichSuccess"] = "Yapay zeka analizi yenilendi.";
        }
        catch (MissingApiKeyException ex)
        {
            TempData["EnrichError"] = ex.Message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Yeniden analiz başarısız: {CompanyId}", id);
            TempData["EnrichError"] = $"Yeniden analiz başarısız: {ex.Message}";
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    /// <summary>
    /// Ikinci adim: bu firma icin karar vericileri arar (LinkedIn + Apollo) ve
    /// bulunan kisileri firmaya ekler. Arama sirasinda calismaz; kullanici bu
    /// firmayi bilerek sectiginde tetiklenir.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Enrich(int id, CancellationToken ct)
    {
        var company = await _db.Companies
            .Include(c => c.Contacts)
            .FirstOrDefaultAsync(c => c.Id == id, ct);

        if (company is null) return NotFound();

        try
        {
            var result = await _enrichment.EnrichAsync(company, ct);

            if (!result.Success)
            {
                TempData["EnrichError"] = result.Error;
            }
            else if (result.Contacts.Count == 0)
            {
                TempData["EnrichInfo"] = "Bu firma için uygun ünvanlı kişi bulunamadı (Apollo ve LinkedIn denendi).";
            }
            else
            {
                var before = company.Contacts.Count;
                company.Contacts = ContactMerger.Merge(company.Contacts, result.Contacts);

                // Yeni bulunan yonetici "IT/SAP yoneticisi bulundu" kriterini
                // karsilayabilir; puan guncel kisi listesiyle yeniden hesaplanir.
                await _icp.ScoreAsync(company, ParseAnalysis(company.AiAnalysis), site: null, ct);

                await _db.SaveChangesAsync(ct);

                var added = company.Contacts.Count - before;
                TempData["EnrichSuccess"] = added > 0
                    ? $"{added} yeni kişi bulundu."
                    : "Bulunan kişiler zaten kayıtlıydı.";
            }
        }
        catch (MissingApiKeyException ex)
        {
            TempData["EnrichError"] = ex.Message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Zenginleştirme başarısız: {Domain}", company.Domain);
            TempData["EnrichError"] = $"Lead araması başarısız: {ex.Message}";
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    /// <summary>
    /// Kisi bazinda e-posta acma: "Lead'leri Bul" artik butun Apollo adaylarini
    /// kredisiz listeler (isim kismen gizli, e-posta yok); kullanici hangi
    /// kisinin e-postasini acmak istedigine burada karar verir. Sadece bu
    /// cagri kredi harcar (people/match, id ile) — kredi kullanimi boylece
    /// tamamen kullanicinin kontrolundedir.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RevealContactEmail(
        int id, int contactId, [FromServices] ContactRevealService reveal, CancellationToken ct)
    {
        var belongs = await _db.Contacts.AnyAsync(c => c.Id == contactId && c.CompanyId == id, ct);
        if (!belongs) return NotFound();

        var outcome = await reveal.RevealAsync(contactId, ct);
        var key = outcome.Success ? "EnrichSuccess" : outcome.IsError ? "EnrichError" : "EnrichInfo";
        TempData[key] = outcome.Message;

        return RedirectToAction(nameof(Details), new { id });
    }

    /// <summary>
    /// Faz-II: bu firma icin ucretsiz kaynaklardan (haber, KAP, resmi kayit linkleri)
    /// on arastirma yapar ve trafik isigi sinyali uretir. Lead puanindan bagimsizdir.
    /// Arama sirasinda calismaz; kullanici bilerek tetikler.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Research(int id, CancellationToken ct)
    {
        var company = await _db.Companies.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (company is null) return NotFound();

        try
        {
            var result = await _research.ResearchAsync(company, ct);

            if (!result.Success)
            {
                TempData["ResearchError"] = result.Error ?? "Ön araştırma tamamlanamadı.";
            }
            else
            {
                company.RatingJson = result.RawJson;
                company.RatingSignal = result.Evaluation!.Signal.ToString();
                company.RatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);

                TempData["ResearchSuccess"] = result.Evaluation.Signal == RatingSignal.Bilinmiyor
                    ? "Ön araştırma yapıldı ama internette yeterli bilgi bulunamadı."
                    : $"Ön araştırma tamamlandı: {result.Evaluation.Signal}.";
            }
        }
        catch (MissingApiKeyException ex)
        {
            TempData["ResearchError"] = ex.Message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ön araştırma başarısız: {Company}", company.Name);
            TempData["ResearchError"] = $"Ön araştırma başarısız: {ex.Message}";
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    /// <summary>Firma detayindan lead olusturur ve e-mail ekranina gecer.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateLead(int id, int? contactId, CancellationToken ct)
    {
        var company = await _db.Companies
            .Include(c => c.Contacts)
            .Include(c => c.Leads)
            .FirstOrDefaultAsync(c => c.Id == id, ct);

        if (company is null) return NotFound();

        var contact = contactId is not null
            ? company.Contacts.FirstOrDefault(c => c.Id == contactId)
            : _scoring.PickBestContact(company.Contacts);

        // Lead kisi bazlidir: ayni firmada farkli kisilerle ayri lead acilabilir,
        // ayni kisi icin ikinci kez acilmaz (mevcut lead'e gidilir).
        var existing = company.Leads.FirstOrDefault(l => l.ContactId == contact?.Id);
        if (existing is not null)
        {
            TempData["LeadInfo"] = "Bu kişi için zaten bir lead var.";
            return RedirectToAction("Details", "Lead", new { id = existing.Id });
        }

        var lead = new Lead
        {
            CompanyId = company.Id,
            ContactId = contact?.Id,
            Score = company.Score,
            Status = LeadStatus.Incelendi
        };

        _db.Leads.Add(lead);
        await _db.SaveChangesAsync(ct);

        TempData["LeadSuccess"] = "Lead oluşturuldu.";
        return RedirectToAction("Details", "Lead", new { id = lead.Id });
    }

    internal static CompanyAnalysis? ParseAnalysis(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            return JsonSerializer.Deserialize<CompanyAnalysis>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Company.RatingJson + RatingSignal kolonundan detay karti modelini kurar.</summary>
    private RatingViewModel? BuildRatingViewModel(Company company)
    {
        if (string.IsNullOrWhiteSpace(company.RatingJson)) return null;

        CompanyRating? rating;
        try
        {
            rating = JsonSerializer.Deserialize<CompanyRating>(company.RatingJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return null;
        }

        if (rating is null) return null;

        // Yonlu gerekce: yeni satirlarda V2 var; eski satirlarda duz listeyi
        // yonsuz (incelenmeli) EvaluatorNote'a cevir.
        var notes = rating.EvaluatorNotesV2.Count > 0
            ? rating.EvaluatorNotesV2
            : rating.EvaluatorNotes
                .Select(r => new EvaluatorNote { Reason = r, Direction = "incelenmeli" })
                .ToList();

        return new RatingViewModel
        {
            Rating = rating,
            Signal = ParseSignal(company.RatingSignal),
            RatedAt = company.RatedAt,
            Confidence = rating.Confidence,
            StalenessDays = _researchOptions.StalenessDays,
            IsStale = RatingSignalDisplay.IsStale(company.RatedAt, _researchOptions.StalenessDays),
            Notes = notes,
            SourceGroups = rating.SourceSnippets
        };
    }

    private static RatingSignal ParseSignal(string? value) =>
        Enum.TryParse<RatingSignal>(value, ignoreCase: true, out var signal)
            ? signal
            : RatingSignal.Bilinmiyor;
}
