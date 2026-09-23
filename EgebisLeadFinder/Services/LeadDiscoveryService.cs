using EgebisLeadFinder.Configuration;
using EgebisLeadFinder.Data;
using EgebisLeadFinder.Models;
using EgebisLeadFinder.Services.Progress;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EgebisLeadFinder.Services;

/// <summary>
/// Uctan uca akis: Ara -> Sitesini oku -> AI ile analiz et -> Puanla -> Kaydet.
/// Bir firma hata verirse digerleri devam eder; hata firma kaydina yazilir.
/// </summary>
public class LeadDiscoveryService
{
    private readonly ISearchService _search;
    private readonly IWebScraperService _scraper;
    private readonly IAiService _ai;
    private readonly LeadScoringService _scoring;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PipelineOptions _options;
    private readonly ILogger<LeadDiscoveryService> _logger;

    public LeadDiscoveryService(
        ISearchService search,
        IWebScraperService scraper,
        IAiService ai,
        LeadScoringService scoring,
        IServiceScopeFactory scopeFactory,
        IOptions<PipelineOptions> options,
        ILogger<LeadDiscoveryService> logger)
    {
        _search = search;
        _scraper = scraper;
        _ai = ai;
        _scoring = scoring;
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<DiscoveryResult> RunAsync(
        SearchCriteria criteria,
        CancellationToken ct = default,
        IProgress<JobStep>? progress = null)
    {
        var result = new DiscoveryResult();

        progress?.Report(new JobStep(3, "Arama sorguları hazırlanıyor"));

        var searchResults = await _search.SearchCompaniesAsync(criteria, ct);
        result.FoundBySearch = searchResults.Count;

        progress?.Report(new JobStep(8, "Firmalar bulundu",
            $"{searchResults.Count} firma bulundu"));

        if (searchResults.Count == 0) return result;

        // Ayni domain daha once islendiyse tekrar isleme; arama kotasi ve AI maliyeti bosa gitmesin.
        var knownDomains = await GetKnownDomainsAsync(searchResults.Select(r => r.Domain), ct);

        var toProcess = searchResults.Where(r => !knownDomains.Contains(r.Domain)).ToList();
        result.AlreadyKnown = searchResults.Count - toProcess.Count;

        // Hedef siteleri de kendimizi de yormamak icin es zamanli istek sayisi sinirli.
        using var throttle = new SemaphoreSlim(_options.MaxParallelism);

        var total = toProcess.Count;
        var done = 0;

        progress?.Report(new JobStep(10, "Firmalar değerlendiriliyor",
            $"0/{total} firma işlendi"));

        // Kota bitince kalan firmalar islenmez; o ana kadarkiler yine kaydedilir.
        var quotaHit = 0;

        var tasks = toProcess.Select(async Task<Company?> (searchResult) =>
        {
            if (Volatile.Read(ref quotaHit) == 1) return null;

            await throttle.WaitAsync(ct);
            try
            {
                return await ProcessCompanyAsync(searchResult, criteria, ct);
            }
            catch (QuotaExceededException ex)
            {
                Interlocked.Exchange(ref quotaHit, 1);
                result.AbortReason ??= ex.Message;
                _logger.LogWarning("Kota bitti, kalan firmalar atlanıyor: {Domain}", searchResult.Domain);
                return null;
            }
            finally
            {
                throttle.Release();

                // Firmalar paralel islendigi icin ilerleme, tamamlanan sayaci
                // uzerinden bildirilir. 10-90 araligina yayilir.
                var n = Interlocked.Increment(ref done);
                progress?.Report(new JobStep(
                    10 + (int)(80.0 * n / Math.Max(1, total)),
                    "Firmalar değerlendiriliyor",
                    $"{n}/{total} firma işlendi"));
            }
        });

        var companies = (await Task.WhenAll(tasks)).Where(c => c is not null).Select(c => c!).ToList();
        result.SkippedByQuota = toProcess.Count - companies.Count;

        progress?.Report(new JobStep(92, "Sonuçlar kaydediliyor"));

        // Kayit tek noktadan yapilir: DbContext thread-safe degildir.
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Kayit sirasinda son bir tekillik kontrolu: arama basladiktan sonra
        // baska bir arama ayni firmayi eklemis olabilir. Veritabanindaki benzersiz
        // indeks bunu zaten engelliyor, ama kullaniciya hata gostermek yerine
        // cakisan kayitlari sessizce eliyoruz.
        var domainsNow = await db.Companies
            .Where(c => c.Domain != null)
            .Select(c => c.Domain!)
            .ToListAsync(ct);

        var existing = domainsNow.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var toInsert = companies
            .Where(c => c.Domain is null || existing.Add(c.Domain))
            .ToList();

        foreach (var company in toInsert)
        {
            StringLengthGuard.Apply(company);
            foreach (var contact in company.Contacts)
                StringLengthGuard.Apply(contact);
        }

        db.Companies.AddRange(toInsert);
        await db.SaveChangesAsync(ct);

        result.Processed = toInsert.Count;
        result.Failed = toInsert.Count(c => c.ProcessingError is not null);

        // Daha once kaydedilmis firmalar tekrar islenmiyor ama kullanici
        // "20 bulundu" dediginde 20'sini de gormeli; sadece bu aramada yeni
        // islenenleri gostermek "az sonuc buluyoruz" izlenimi yaratiyordu.
        var previouslyKnown = knownDomains.Count > 0
            ? await db.Companies
                .Where(c => c.Domain != null && knownDomains.Contains(c.Domain))
                .Include(c => c.Contacts)
                .ToListAsync(ct)
            : new List<Company>();

        // Kaydedilenler + zaten kayitli olanlar. Cakisma nedeniyle eklenmeyen
        // kayitlar listeye girmez; onlarin veritabanindaki hali previouslyKnown
        // icinde zaten var. Ayni domain iki kez listelenmemeli.
        result.Companies = toInsert
            .Concat(previouslyKnown)
            .GroupBy(c => c.Domain ?? Guid.NewGuid().ToString(), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderByDescending(c => c.Score)
            .ToList();

        _logger.LogInformation(
            "Arama tamamlandı: {Found} bulundu, {Known} zaten kayıtlı, {Processed} işlendi, {Failed} hatalı.",
            result.FoundBySearch, result.AlreadyKnown, result.Processed, result.Failed);

        return result;
    }

    private async Task<Company> ProcessCompanyAsync(SearchResult searchResult, SearchCriteria criteria, CancellationToken ct)
    {
        var company = new Company
        {
            Name = searchResult.Title,
            Website = searchResult.Url,
            Domain = searchResult.Domain,
            Description = searchResult.Snippet,
            Industry = criteria.Industry,
            City = CompanyCityResolver.Resolve(searchResult, criteria.City),
            Country = criteria.Country
        };

        try
        {
            var site = await _scraper.ScrapeAsync(searchResult.Url, ct);

            if (!site.Success)
            {
                company.ProcessingError = site.Error;
                return company;
            }

            var aiResult = await _ai.AnalyzeCompanyAsync(site.Text, ct);

            if (aiResult.Success)
            {
                company.AiAnalysis = aiResult.RawJson;

                // AI'in tespit ettigi sektor, kullanicinin yazdigi arama terimini geçer.
                if (!string.IsNullOrWhiteSpace(aiResult.Analysis!.Industry))
                    company.Industry = aiResult.Analysis.Industry;

                // Arama sonucu basligi cogu zaman haber/ilan basligidir;
                // sitenin metninden okunan firma adi daha dogrudur.
                if (!string.IsNullOrWhiteSpace(aiResult.Analysis.CompanyName))
                    company.Name = aiResult.Analysis.CompanyName;
            }
            else
            {
                company.ProcessingError = aiResult.Error;
            }

            company.Contacts = BuildContacts(site, searchResult);

            // Bu asamada kisi/lead aramasi YAPILMAZ. Kesif (discovery) ve
            // zenginlestirme (enrichment) iki ayri adim: zenginlestirme, kullanici
            // firma detayindaki "Lead'leri Bul" butonuna bastiginda tetiklenir.
            // Boylece arama hizli kalir ve kisisel veri yalnizca kullanicinin
            // bilerek sectigi firmalar icin toplanir.
            company.Score = _scoring.ScoreCompany(aiResult.Analysis, site, company.Contacts).Total;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Tek firmanin hatasi tum aramayi durdurmaz.
            _logger.LogWarning(ex, "Firma işlenemedi: {Domain}", searchResult.Domain);
            company.ProcessingError = ex.Message;
        }

        return company;
    }

    /// <summary>
    /// Sitede bulunan kisileri Contact'a cevirir. Kisi bulunamazsa genel iletisim
    /// adresi tek basina kaydedilir; muhatap ismi olmasa da e-posta degerlidir.
    /// </summary>
    private List<Contact> BuildContacts(ScrapedSite site, SearchResult? searchResult = null)
    {
        var contacts = site.People
            .Where(p => !string.IsNullOrWhiteSpace(p.Name))
            .Select(p => new Contact
            {
                Name = p.Name,
                Title = p.Title,
                Email = p.Email,
                SourceUrl = p.SourceUrl,
                TitleScore = _scoring.ScoreTitle(p.Title)
            })
            .GroupBy(c => c.Name!.ToLowerInvariant())
            .Select(g => g.OrderByDescending(c => c.TitleScore).First())
            .OrderByDescending(c => c.TitleScore)
            .Take(10)
            .ToList();

        // Telefon oncelikle Google Haritalar kaydindan alinir: dogrulanmis isletme
        // numarasidir, sitede regex ile yakalanandan daha guvenilir.
        var phone = searchResult?.Phone ?? site.Phones.FirstOrDefault();

        if (contacts.Count == 0 && (site.Emails.Count > 0 || phone is not null))
        {
            contacts.Add(new Contact
            {
                Email = site.Emails.FirstOrDefault(),
                Phone = phone,
                Title = "Genel İletişim",
                SourceUrl = site.Url
            });
        }

        return contacts;
    }

    private async Task<HashSet<string>> GetKnownDomainsAsync(IEnumerable<string> domains, CancellationToken ct)
    {
        var list = domains.ToList();

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var known = await db.Companies
            .Where(c => c.Domain != null && list.Contains(c.Domain))
            .Select(c => c.Domain!)
            .ToListAsync(ct);

        return known.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}

public class DiscoveryResult
{
    public int FoundBySearch { get; set; }
    public int AlreadyKnown { get; set; }
    public int Processed { get; set; }
    public int Failed { get; set; }
    public List<Company> Companies { get; set; } = new();

    /// <summary>
    /// Kullanici arama oncesi bir profil adi girdiyse doldurulur (bkz.
    /// CompanyController.GetOrCreateProfileAsync). Doluysa sonuc ekraninda
    /// "seçilenleri profile kaydet" butonu gosterilir.
    /// </summary>
    public int? ProfileId { get; set; }
    public string? ProfileName { get; set; }

    /// <summary>API kotasi bittigi icin arama yarida kesildiyse sebebi; null ise tamamlandi.</summary>
    public string? AbortReason { get; set; }

    /// <summary>Kota bittigi icin hic islenmeyen firma sayisi.</summary>
    public int SkippedByQuota { get; set; }

    public bool Aborted => AbortReason is not null;
}
