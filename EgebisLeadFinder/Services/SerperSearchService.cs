using System.Net.Http.Json;
using System.Text.Json.Serialization;
using EgebisLeadFinder.Configuration;
using EgebisLeadFinder.Data;
using EgebisLeadFinder.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace EgebisLeadFinder.Services;

/// <summary>
/// Serper.dev uzerinden Google sonuclarini ceker.
/// Kendi arama motorumuzu yazmiyoruz (dokuman bolum 4).
/// </summary>
public class SerperSearchService : ISearchService
{
    private readonly HttpClient _http;
    private readonly SearchOptions _options;
    private readonly ISettingsService _settings;
    private readonly IApiUsageTracker _usage;
    private readonly IMemoryCache _cache;
    private readonly ILogger<SerperSearchService> _logger;

    /// <summary>Ayarlar ekranindaki kredi sayacinin anahtari.</summary>
    public const string UsageProvider = "Serper";

    public SerperSearchService(
        HttpClient http,
        IOptions<SearchOptions> options,
        ISettingsService settings,
        IApiUsageTracker usage,
        IMemoryCache cache,
        ILogger<SerperSearchService> logger)
    {
        _http = http;
        _options = options.Value;
        _settings = settings;
        _usage = usage;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Elenecek alan adlari: appsettings listesi + Ayarlar ekranindan eklenenler.
    /// Her aramanin basinda tazelenir (bkz. LoadBlockedAsync).
    /// </summary>
    private List<string> _blocked = new();

    /// <summary>Aktif arama bolgesi: Google ulke/dil kodlarini belirler.</summary>
    private SearchRegion _region = SearchRegions.Default;

    private async Task LoadBlockedAsync(CancellationToken ct)
    {
        var extra = await _settings.GetAsync(SettingKeys.ExtraBlockedDomains, ct);

        _blocked = _options.BlockedDomains
            .Concat(SearchBlacklist.Parse(extra).Domains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>"sahibinden.com, haberler" gibi metni listeye cevirir.</summary>
    public static List<string> ParseDomains(string? raw) =>
        (raw ?? string.Empty)
            .Split(new[] { ',', ';', '\n', '\r', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(d => d.Trim().TrimStart('*', '.').ToLowerInvariant())
            .Where(d => d.Length > 2)
            .Distinct()
            .ToList();

    /// <summary>
    /// Anahtari once Ayarlar ekranindan, orada bos ise yapilandirmadan okur.
    /// Ikisi de bos ise kullaniciyi Ayarlar ekranina yonlendiren hata firlatir.
    /// </summary>
    private async Task<string> RequireApiKeyAsync(CancellationToken ct)
    {
        await LoadBlockedAsync(ct);

        var key = await _settings.GetAsync(SettingKeys.SerperApiKey, ct);

        if (string.IsNullOrWhiteSpace(key))
            throw new MissingApiKeyException("Serper");

        return key;
    }

    public async Task<List<SearchResult>> SearchCompaniesAsync(SearchCriteria criteria, CancellationToken ct = default)
    {
        var apiKey = await RequireApiKeyAsync(ct);

        _region = SearchRegions.Get(criteria.RegionKey ?? SearchRegions.ByCountry(criteria.Country)?.Key);

        if (criteria.IsNameSearch)
            return await SearchByNameAsync(criteria, apiKey, ct);

        // Domain -> sonuc. Ayni firma birden fazla sorguda cikabilir, ilk gorulen kalir.
        var found = new Dictionary<string, SearchResult>(StringComparer.OrdinalIgnoreCase);

        // Once Google Haritalar: fabrikalarin SEO'su zayif oldugu icin organik
        // sonuclarda ust siralara cikamiyorlar, ama Haritalar kaydi neredeyse
        // hepsinde var. Olculen fark: ayni sorguda organik 9 link, Haritalar
        // 3 sayfada 30 firma (29'unda web sitesi).
        try
        {
            // Urun aramasi: once dizinlerden (Europages "uretici" etiketi, OSB listeleri) birkac aday; Haritalar/organik sonra doldurur.
            if (_options.UseDirectories && criteria.ProductList.Count > 0 && _region.Key == SearchRegions.DefaultKey)
                await CollectFromDirectoriesAsync(criteria, found, apiKey, ct);

            if (_options.UsePlaces)
                await CollectFromPlacesAsync(criteria, found, apiKey, ct);

            // Haritalar hedefi dolduramadiysa organik arama ile tamamla.
            if (found.Count < criteria.EffectiveCollectTarget)
                await CollectFromOrganicAsync(criteria, found, apiKey, ct);
        }
        catch (Exception ex) when (ex is SearchProviderException or QuotaExceededException && found.Count > 0)
        {
            // Kota/anahtar hatasi geldi ama bir kisim firma bulunmustu: onlarla devam edilebilsin.
            throw new SearchProviderException(ex.Message) { Partial = found.Values.ToList() };
        }

        _logger.LogInformation("{Count} firma bulundu.", found.Count);
        return found.Values.ToList();
    }

    /// <summary>
    /// Belirli bir firmayi adiyla bulur ("Toyota", "Egebis"). 3 organik + 1 Haritalar
    /// sorgusu (~4 kredi) atilir; firma adini alan adinda veya basliginda tasiyan
    /// siteler one alinir. Haritalar kategori filtresi uygulanmaz: kullanici bu
    /// firmayi bilerek ariyor.
    /// </summary>
    private async Task<List<SearchResult>> SearchByNameAsync(SearchCriteria criteria, string apiKey, CancellationToken ct)
    {
        var name = criteria.CompanyName!.Trim();
        var country = criteria.Country?.Trim();
        var found = new Dictionary<string, SearchResult>(StringComparer.OrdinalIgnoreCase);
        // Ayni site birden fazla sorguda/sonucta cikiyorsa buyuk olasilikla resmi sitedir.
        var hits = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        void Add(string? url, string? title, string? snippet, string? phone = null, string? address = null, string? category = null)
        {
            var domain = DomainHelper.ExtractDomain(url);
            if (domain is null) return;
            if (found.TryGetValue(domain, out var existing))
            {
                hits[domain] = hits.GetValueOrDefault(domain) + 1;
                // Alt sayfa basliklari (urun PDF'i vb.) firma adini tasimaz: adi en iyi tasiyan baslik kalir.
                if (!string.IsNullOrWhiteSpace(title)
                    && NameRelevance(new SearchResult { Domain = domain, Title = title }, name) > NameRelevance(new SearchResult { Domain = domain, Title = existing.Title }, name))
                {
                    existing.Title = title;
                    existing.Snippet = snippet ?? existing.Snippet;
                }
                return;
            }
            if (DomainHelper.IsBlocked(domain, _blocked)) return;
            hits[domain] = 1;

            found[domain] = new SearchResult
            {
                Domain = domain,
                Url = $"https://{domain}",
                Title = string.IsNullOrWhiteSpace(title) ? DomainHelper.GuessNameFromDomain(domain) : title,
                Snippet = snippet,
                Phone = phone,
                Address = address,
                Category = category
            };
        }

        foreach (var query in new[] { $"\"{name}\"", $"{name} resmi web sitesi", $"{name} {country}".Trim() })
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                foreach (var organic in await QueryAsync(query, apiKey, ct))
                    Add(organic.Link, organic.Title, organic.Snippet);
            }
            catch (Exception ex) when (IsQueryLocal(ex))
            {
                _logger.LogWarning(ex, "Firma adı sorgusu başarısız: {Query}", query);
            }
        }

        if (_options.UsePlaces)
        {
            try
            {
                foreach (var place in await QueryPlacesAsync($"{name} {country}".Trim(), 1, apiKey, ct))
                    Add(place.Website, place.Title, null, place.PhoneNumber, place.Address, place.Category);
            }
            catch (Exception ex) when (IsQueryLocal(ex))
            {
                _logger.LogWarning(ex, "Firma adı Haritalar sorgusu başarısız: {Name}", name);
            }
        }

        // Ayni sitenin alt alan adi ("cdn.ugur.com.tr") ana alan adi da bulunduysa ayri firma sayilmaz.
        foreach (var child in found.Keys.Where(d => found.Keys.Any(p => d.EndsWith("." + p, StringComparison.OrdinalIgnoreCase))).ToList())
            found.Remove(child);

        // Adla hic ilgisi olmayan siteler (haber, rehber) varsa ilgili olanlar varken elenir.
        var scored = found.Values
            .Select(r => (Result: r, Score: NameRelevance(r, name) + Math.Min(hits.GetValueOrDefault(r.Domain) - 1, 4) * 2))
            .OrderByDescending(x => x.Score)
            .ToList();
        if (scored.Any(x => x.Score > 0))
            scored = scored.Where(x => x.Score > 0).ToList();

        var ranked = scored
            .Take(Math.Max(1, criteria.MaxCompanies))
            .Select(x => x.Result)
            .ToList();

        _logger.LogInformation("Firma adı araması: {Name} → {Count} aday ({Top}).", name, ranked.Count,
            string.Join(", ", scored.Take(8).Select(x => $"{x.Result.Domain}={x.Score}")));
        return ranked;
    }

    /// <summary>Alan adinda firma adi gecmesi en guclu isaret; sonra baslik, sonra ozet.</summary>
    public static int NameRelevance(SearchResult r, string name)
    {
        var tokens = TurkishText.Normalize(name)
            .Split(new[] { ' ', '-', '.', ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length >= 3 && t is not "san" and not "tic" and not "ltd" and not "sti")
            .ToList();
        if (tokens.Count == 0) return 0;

        var domain = TurkishText.Normalize(r.Domain);
        var title = TurkishText.Normalize(r.Title);
        var snippet = TurkishText.Normalize(r.Snippet);

        var score = 0;
        foreach (var t in tokens)
        {
            if (domain.Contains(t)) score += 10;
            if (title.Contains(t)) score += 4;
            if (snippet.Contains(t)) score += 1;
        }

        // Marka alan adinin kendisi: "ugur.com.tr" ya da "ugursogutma.com.tr" ("ugurkombiklima.com" gibi ek kelimeli
        // benzerlerinden ayrilir). Resmi site genelde adin ilk kelimesi veya birlesik halidir.
        if (score > 0)
        {
            var label = domain.StartsWith("www.", StringComparison.Ordinal) ? domain[4..] : domain;
            label = label.Split('.')[0].Replace("-", string.Empty);
            if (tokens.Contains(label)) score += 8;
            else if (label == string.Concat(tokens) || (tokens.Count >= 2 && label == string.Concat(tokens.Take(2)))) score += 12;
        }
        return score;
    }

    /// <summary>
    /// Google Haritalar (Places) uzerinden firma toplar. Her sorgu icin sayfa sayfa
    /// ilerler; her sayfa 10 sonuc ve 1 Serper kredisi demektir.
    /// </summary>
    private async Task CollectFromPlacesAsync(
        SearchCriteria criteria, Dictionary<string, SearchResult> found, string apiKey, CancellationToken ct)
    {
        var queries = SearchQueryBuilder.BuildPlaceQueries(criteria);

        foreach (var query in queries)
        {
            for (var page = 1; page <= _options.PlacesPagesPerQuery; page++)
            {
                if (found.Count >= criteria.EffectiveCollectTarget) return;
                ct.ThrowIfCancellationRequested();

                try
                {
                    var places = await QueryPlacesAsync(query, page, apiKey, ct);

                    // Bos sayfa geldiyse bu sorgunun sonucu tukenmistir,
                    // sonraki sayfalari istemeye gerek yok (kredi tasarrufu).
                    if (places.Count == 0) break;

                    foreach (var place in places)
                    {
                        var domain = DomainHelper.ExtractDomain(place.Website);
                        if (domain is null) continue;
                        if (DomainHelper.IsBlocked(domain, _blocked)) continue;
                        if (found.ContainsKey(domain)) continue;

                        // Tamirci, galeri, lastikci gibi hedef disi isletmeleri
                        // site okumaya ve AI'a gitmeden ele. Her tur firma aranirken kategori aranan
                        // terimle eslesiyorsa ("restoran" ararken "Restoran") elenmez.
                        if (!PlaceCategoryFilter.IsRelevant(place.Category,
                                criteria.ManufacturerQueries ? null : criteria.SearchTerms.Prepend(criteria.Industry)))
                        {
                            _logger.LogDebug(
                                "Kategori nedeniyle elendi: {Title} ({Category})", place.Title, place.Category);
                            continue;
                        }

                        found[domain] = new SearchResult
                        {
                            Domain = domain,
                            Url = $"https://{domain}",
                            Title = string.IsNullOrWhiteSpace(place.Title)
                                ? DomainHelper.GuessNameFromDomain(domain)
                                : place.Title,
                            Phone = place.PhoneNumber,
                            Address = place.Address,
                            Category = place.Category
                        };

                        if (found.Count >= criteria.EffectiveCollectTarget) return;
                    }
                }
                catch (Exception ex) when (IsQueryLocal(ex))
                {
                    // Tek sorgu/sayfa patlarsa digerleri devam etsin.
                    _logger.LogWarning(ex, "Haritalar sorgusu başarısız: {Query} (sayfa {Page})", query, page);
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Dizinlerden aday toplar: "site:europages.com.tr {urun}" gibi sorgularin sonuc BASLIKLARINDAN firma adi okunur (dizin
    /// sitesine istek atilmaz), sonra her ad icin firmanin kendi sitesi aranir. Kredi tavani: DirectoryMaxNames ad; toplanan aday
    /// sayisi hedefin ucte biriyle sinirlidir ki Haritalar/organik sonuclara da yer kalsin.
    /// </summary>
    private async Task CollectFromDirectoriesAsync(
        SearchCriteria criteria, Dictionary<string, SearchResult> found, string apiKey, CancellationToken ct)
    {
        var limit = Math.Min(_options.DirectoryMaxNames, Math.Max(3, criteria.EffectiveCollectTarget / 3));
        var terms = criteria.SearchTerms.Prepend(criteria.Industry).Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(2).ToList();

        // Ad -> hangi dizinden geldigi. Ayni ad birden fazla sorguda cikabilir.
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var term in terms)
        {
            foreach (var site in DirectoryCandidates.TurkishDirectories)
            {
                if (names.Count >= limit) break;
                ct.ThrowIfCancellationRequested();
                try
                {
                    foreach (var hit in await QueryAsync(DirectoryCandidates.Query(site, term), apiKey, ct))
                    {
                        var name = DirectoryCandidates.ExtractName(hit.Title, term);
                        if (name is not null && names.Count < limit) names.TryAdd(name, site);
                    }
                }
                catch (Exception ex) when (IsQueryLocal(ex))
                {
                    _logger.LogWarning(ex, "Dizin sorgusu başarısız: {Site} {Term}", site, term);
                }
            }
        }

        var resolved = 0;
        foreach (var (name, site) in names)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                // Firmanin KENDI sitesi: alan adi firma adini tasiyan ilk engellenmemis sonuc. Eslesme yoksa atlanir
                // (yanlis siteyi incelemektense adayi kaybetmek yeglenir).
                // "ABC Plastik Sanayi" icin "plastik" gibi genel kelime degil, ayirt edici ilk kelime ("abc") alan adinda gecmeli.
                var brand = TurkishText.Normalize(name).Split(new[] { ' ', '.', ',', '-' }, StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault(t => t.Length >= 3) ?? string.Empty;
                var best = (await QueryAsync($"\"{name}\"", apiKey, ct))
                    .Select(o => (Organic: o, Domain: DomainHelper.ExtractDomain(o.Link)))
                    .Where(x => x.Domain is not null && !DomainHelper.IsBlocked(x.Domain, _blocked))
                    .Select(x => (x.Organic, Domain: x.Domain!, Score: NameRelevance(new SearchResult { Domain = x.Domain!, Title = x.Organic.Title ?? string.Empty }, name)))
                    .Where(x => x.Score >= 10 && brand.Length > 0 && TurkishText.Normalize(x.Domain).Contains(brand))
                    .OrderByDescending(x => x.Score)
                    .FirstOrDefault();
                if (best.Domain is null || found.ContainsKey(best.Domain)) continue;

                found[best.Domain] = new SearchResult
                {
                    Domain = best.Domain,
                    Url = $"https://{best.Domain}",
                    Title = name,
                    Snippet = $"Dizinde listeli: {site}"
                };
                resolved++;
            }
            catch (Exception ex) when (IsQueryLocal(ex))
            {
                _logger.LogWarning(ex, "Dizin firması çözülemedi: {Name}", name);
            }
        }

        _logger.LogInformation("Dizinlerden {Names} firma adı, {Resolved} firma sitesi bulundu.", names.Count, resolved);
    }

    /// <summary>Klasik organik web aramasi ile firma toplar.</summary>
    private async Task CollectFromOrganicAsync(
        SearchCriteria criteria, Dictionary<string, SearchResult> found, string apiKey, CancellationToken ct)
    {
        var queries = SearchQueryBuilder.Build(criteria);

        foreach (var query in queries)
        {
            if (found.Count >= criteria.EffectiveCollectTarget) return;
            ct.ThrowIfCancellationRequested();

            try
            {
                foreach (var organic in await QueryAsync(query, apiKey, ct))
                {
                    var domain = DomainHelper.ExtractDomain(organic.Link);
                    if (domain is null) continue;
                    if (DomainHelper.IsBlocked(domain, _blocked)) continue;
                    if (found.ContainsKey(domain)) continue;

                    found[domain] = new SearchResult
                    {
                        Domain = domain,
                        Url = $"https://{domain}",
                        Title = string.IsNullOrWhiteSpace(organic.Title)
                            ? DomainHelper.GuessNameFromDomain(domain)
                            : organic.Title,
                        Snippet = organic.Snippet
                    };

                    if (found.Count >= criteria.EffectiveCollectTarget) return;
                }
            }
            catch (Exception ex) when (IsQueryLocal(ex))
            {
                // Tek sorgu patlarsa digerleri devam etsin.
                _logger.LogWarning(ex, "Arama sorgusu başarısız: {Query}", query);
            }
        }
    }

    /// <summary>
    /// Google Dorking: "site:linkedin.com/in/" ile firmaya bagli herkese acik profil
    /// basliklarini arar. LinkedIn'e istek atilmaz; sadece Serper'in indeksledigi
    /// arama sonucu okunur. Sonuc URL'leri kara listeden gecmez (bilerek linkedin.com).
    /// </summary>
    public async Task<List<SearchResult>> SearchLinkedInProfilesAsync(string companyName, CancellationToken ct = default)
    {
        var apiKey = await RequireApiKeyAsync(ct);

        if (string.IsNullOrWhiteSpace(companyName))
            return new List<SearchResult>();

        // Aranacak unvanlar Ayarlar ekranindan okunur, koda gomulu degil.
        var keywords = await _settings.GetTitleKeywordsAsync(ct);
        var titleFilter = string.Join(" OR ", keywords.Select(k => $"\"{k}\""));

        var query = keywords.Count == 0
            ? $"site:linkedin.com/in/ \"{companyName}\""
            : $"site:linkedin.com/in/ \"{companyName}\" ({titleFilter})";

        try
        {
            var organic = await QueryAsync(query, apiKey, ct);

            return organic
                .Where(o => !string.IsNullOrWhiteSpace(o.Title) && !string.IsNullOrWhiteSpace(o.Link))
                .Select(o => new SearchResult
                {
                    Title = o.Title!,
                    Url = o.Link!,
                    Snippet = o.Snippet
                })
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LinkedIn araması başarısız: {Company}", companyName);
            return new List<SearchResult>();
        }
    }

    /// <summary>
    /// Genel amacli web aramasi (Faz-II on arastirma). Ham organik sonuclari
    /// baslik + link + snippet olarak doner; kara liste veya kategori filtresi uygulanmaz.
    /// </summary>
    public async Task<List<SearchResult>> SearchAsync(string query, int maxResults, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return new List<SearchResult>();

        var apiKey = await RequireApiKeyAsync(ct);

        try
        {
            var organic = await QueryAsync(query, apiKey, ct, maxResults);

            return organic
                .Where(o => !string.IsNullOrWhiteSpace(o.Title) && !string.IsNullOrWhiteSpace(o.Link))
                .Take(maxResults)
                .Select(o => new SearchResult
                {
                    Title = o.Title!,
                    Url = o.Link!,
                    Snippet = o.Snippet,
                    Date = o.Date
                })
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Arama basarisiz: {Query}", query);
            return new List<SearchResult>();
        }
    }

    /// <summary>
    /// Ayni sorgu kisa surede tekrarlanirsa (profili yeniden calistirma, sayfayi
    /// yenileme) Serper'a gidilmez; kredi harcanmaz.
    /// </summary>
    private async Task<List<T>> CachedAsync<T>(string kind, string query, int num, Func<Task<List<T>>> fetch)
    {
        if (_options.CacheHours <= 0) return await fetch();

        var key = $"serper:{kind}:{_region.Gl}:{num}:{query.Trim().ToLowerInvariant()}";
        if (_cache.TryGetValue(key, out List<T>? cached) && cached is not null)
        {
            _logger.LogInformation("Serper önbellekten döndü: {Query}", query);
            return cached;
        }

        var fresh = await fetch();
        _cache.Set(key, fresh, TimeSpan.FromHours(_options.CacheHours));
        return fresh;
    }

    /// <summary>
    /// Tek sorguya ozel (gecici ag, bozuk yanit) hatalar atlanir; kota, gecersiz anahtar ve
    /// iptal ise aramanin tamamini ilgilendirir, yukari tasinir.
    /// </summary>
    private static bool IsQueryLocal(Exception ex) =>
        ex is not SearchProviderException and not QuotaExceededException and not OperationCanceledException;

    /// <summary>Basarisiz Serper yanitini aciklayici hataya cevirir.</summary>
    private async Task EnsureOkAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;

        string? body = null;
        try { body = await response.Content.ReadAsStringAsync(ct); } catch { /* govde okunamazsa durum koduyla yetin */ }

        var provider = SearchProviderException.FromSerper((int)response.StatusCode, body);
        if (provider is not null)
        {
            await _usage.RecordErrorAsync(UsageProvider, provider.Message, ct);
            throw provider;
        }

        throw new HttpRequestException($"Serper HTTP {(int)response.StatusCode}", null, response.StatusCode);
    }

    /// <summary>Aylik kredi tavani asildiysa hicbir cagri yapilmaz.</summary>
    private async Task EnsureUnderCapAsync(CancellationToken ct)
    {
        if (_options.MonthlyCreditCap <= 0) return;

        var today = UsageService.Today; // sayaclar Turkiye gunune gore tutulur (UTC+3)
        var used = await _usage.GetRangeCountAsync(
            UsageProvider, new DateOnly(today.Year, today.Month, 1), today, ct);

        if (used >= _options.MonthlyCreditCap)
        {
            await _usage.RecordErrorAsync(UsageProvider, $"Aylık {_options.MonthlyCreditCap} kredi tavanına ulaşıldı", ct);
            throw new QuotaExceededException(
                $"Serper (aylık {_options.MonthlyCreditCap} kredi tavanı, kullanılan {used})");
        }
    }

    private Task<List<SerperOrganic>> QueryAsync(string query, string apiKey, CancellationToken ct, int? num = null) =>
        CachedAsync("organic", query, num ?? _options.ResultsPerQuery,
            () => QueryLiveAsync(query, apiKey, ct, num));

    private async Task<List<SerperOrganic>> QueryLiveAsync(string query, string apiKey, CancellationToken ct, int? num = null)
    {
        await EnsureUnderCapAsync(ct);

        using var request = new HttpRequestMessage(HttpMethod.Post, _options.SerperEndpoint);
        request.Headers.Add("X-API-KEY", apiKey);
        request.Content = JsonContent.Create(new
        {
            q = query,
            gl = _region.Gl,
            hl = _region.Hl,
            num = num ?? _options.ResultsPerQuery
        });

        using var response = await _http.SendAsync(request, ct);
        await EnsureOkAsync(response, ct);

        // Basarili istek = 1 Serper kredisi. Sayac kritik degil; Ayarlar
        // ekranindaki "kalan kredi" barini besler.
        await _usage.IncrementAsync(UsageProvider, ct);

        var payload = await response.Content.ReadFromJsonAsync<SerperResponse>(cancellationToken: ct);
        return payload?.Organic ?? new List<SerperOrganic>();
    }

    /// <summary>Google Haritalar (Places) uc noktasina tek sayfalik istek atar.</summary>
    private Task<List<SerperPlace>> QueryPlacesAsync(string query, int page, string apiKey, CancellationToken ct) =>
        CachedAsync("places", query, page, () => QueryPlacesLiveAsync(query, page, apiKey, ct));

    private async Task<List<SerperPlace>> QueryPlacesLiveAsync(string query, int page, string apiKey, CancellationToken ct)
    {
        await EnsureUnderCapAsync(ct);

        using var request = new HttpRequestMessage(HttpMethod.Post, _options.SerperPlacesEndpoint);
        request.Headers.Add("X-API-KEY", apiKey);
        request.Content = JsonContent.Create(new
        {
            q = query,
            gl = _region.Gl,
            hl = _region.Hl,
            page
        });

        using var response = await _http.SendAsync(request, ct);
        await EnsureOkAsync(response, ct);

        await _usage.IncrementAsync(UsageProvider, ct);

        var payload = await response.Content.ReadFromJsonAsync<SerperPlacesResponse>(cancellationToken: ct);
        return payload?.Places ?? new List<SerperPlace>();
    }

    private class SerperResponse
    {
        [JsonPropertyName("organic")]
        public List<SerperOrganic>? Organic { get; set; }
    }

    private class SerperPlacesResponse
    {
        [JsonPropertyName("places")]
        public List<SerperPlace>? Places { get; set; }
    }

    private class SerperPlace
    {
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("website")]
        public string? Website { get; set; }

        [JsonPropertyName("phoneNumber")]
        public string? PhoneNumber { get; set; }

        [JsonPropertyName("address")]
        public string? Address { get; set; }

        [JsonPropertyName("category")]
        public string? Category { get; set; }
    }

    private class SerperOrganic
    {
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("link")]
        public string? Link { get; set; }

        [JsonPropertyName("snippet")]
        public string? Snippet { get; set; }

        [JsonPropertyName("date")]
        public string? Date { get; set; }
    }
}
