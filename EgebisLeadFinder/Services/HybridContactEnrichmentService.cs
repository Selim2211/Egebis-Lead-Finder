using EgebisLeadFinder.Configuration;
using EgebisLeadFinder.Models;
using Microsoft.Extensions.Options;

namespace EgebisLeadFinder.Services;

/// <summary>
/// Iki asamali karar verici arama. Arama sirasinda otomatik calismaz: kullanici
/// firma detayindaki "Lead'leri Bul" butonuna bastiginda, yalnizca o firma icin
/// tetiklenir.
///
/// 1) Apollo kisi aramasi (tercih edilen): domain + unvan filtresiyle yapisal
///    sonuc doner, unvanlar tam ve kirpilmamistir.
/// 2) LinkedIn + Serper (yedek): Apollo kullanilamiyorsa veya o firmayi
///    tanimiyorsa devreye girer. Apollo'nun Turkiye'deki kucuk/orta olcekli
///    ureticileri kapsamasi zayif olabildigi icin bu yedek korunuyor.
/// </summary>
public class HybridContactEnrichmentService : IContactEnrichmentService
{
    private readonly IPeopleSearchService _peopleSearch;
    private readonly ISearchService _search;
    private readonly IPersonEmailFinder _emailFinder;
    private readonly LeadScoringService _scoring;
    private readonly ISettingsService _settings;
    private readonly EnrichmentOptions _options;
    private readonly ILogger<HybridContactEnrichmentService> _logger;

    public HybridContactEnrichmentService(
        IPeopleSearchService peopleSearch,
        ISearchService search,
        IPersonEmailFinder emailFinder,
        LeadScoringService scoring,
        ISettingsService settings,
        IOptions<EnrichmentOptions> options,
        ILogger<HybridContactEnrichmentService> logger)
    {
        _peopleSearch = peopleSearch;
        _search = search;
        _emailFinder = emailFinder;
        _scoring = scoring;
        _settings = settings;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<EnrichmentResult> EnrichAsync(Company company, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(company.Domain))
            return EnrichmentResult.Failed("Firma alan adı yok, e-posta doğrulaması yapılamaz.");

        try
        {
            // Ayarlardaki unvanlar hem Apollo sorgusunu hem LinkedIn aday elemesini belirler.
            var keywords = await _settings.GetTitleKeywordsAsync(ct);

            var fromApollo = await SearchWithApolloAsync(company, keywords, ct);
            if (fromApollo.Count > 0)
                return new EnrichmentResult { Contacts = fromApollo };

            _logger.LogInformation(
                "Apollo kişi araması sonuç vermedi, LinkedIn yedeğine geçiliyor: {Company}", company.Name);

            return new EnrichmentResult { Contacts = await SearchWithLinkedInAsync(company, keywords, ct) };
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Zenginlestirme basarisiz olsa da ana akis (arama/analiz/puanlama) durmamali.
            _logger.LogWarning(ex, "Zenginleştirme başarısız: {Company}", company.Name);
            return EnrichmentResult.Failed(ex.Message);
        }
    }

    /// <summary>
    /// Birinci yol: Apollo. "api_search" (0 kredi) genis bir aday havuzu doner
    /// (isim kismen gizli, e-posta yok). Firmalar arasi unvan/dil farkini
    /// (TOFAŞ'ta "Chief Information Officer", baskasinda "IT Müdürü") tek bir
    /// sabit liste asamaz; bu yuzden unvan puanina uyan HERKES kredisiz listelenir.
    /// E-posta acma (kredi harcayan people/match) burada YAPILMAZ — kullanici
    /// firma detayinda kisi bazinda "E-postayı Aç" butonuna basinca tetiklenir
    /// (bkz. CompanyController.RevealContactEmail). Boylece kredi kullanimi
    /// tamamen kullanicinin kontrolundedir.
    /// </summary>
    private async Task<List<Contact>> SearchWithApolloAsync(
        Company company, List<string> keywords, CancellationToken ct)
    {
        // Listeleme kredisiz oldugu icin genis bir havuz istenir; Apollo tek
        // sayfada en fazla 100 kayit doner (bkz. ApolloPeopleSearchService).
        var result = await _peopleSearch.SearchAsync(company.Domain!, keywords, 100, ct);

        if (result.Skipped)
        {
            _logger.LogDebug("Apollo kişi araması atlandı: {Reason}", result.Error);
            return new List<Contact>();
        }

        if (result.Error is not null)
            _logger.LogWarning("Apollo kişi araması hata verdi: {Error}", result.Error);

        var contacts = result.People
            .Where(p => !string.IsNullOrWhiteSpace(p.Title))
            .Select(p => (Person: p, Score: ResolveTitleScore(p.Title!, keywords)))
            .Where(x => x.Score is not null)
            .OrderByDescending(x => x.Score)
            .Select(x => new Contact
            {
                Name = x.Person.Name,
                Title = x.Person.Title,
                Email = x.Person.Email,   // "api_search" bunu hic doldurmaz, hep null
                Phone = x.Person.Phone,
                SourceUrl = x.Person.ProfileUrl,
                Source = ContactSource.ApolloSearch,
                TitleScore = x.Score!.Value,
                ApolloId = x.Person.ApolloId
            })
            .ToList();

        ct.ThrowIfCancellationRequested();

        return contacts;
    }

    /// <summary>
    /// Ikinci yol: Google uzerinden herkese acik LinkedIn sonuclari. Basliklar
    /// kirpilabildigi ve alakasiz sonuc gelebildigi icin ek dogrulama gerekir.
    /// </summary>
    private async Task<List<Contact>> SearchWithLinkedInAsync(
        Company company, List<string> keywords, CancellationToken ct)
    {
        var profiles = await _search.SearchLinkedInProfilesAsync(company.Name, ct);
        if (profiles.Count == 0) return new List<Contact>();

        var contacts = new List<Contact>();

        foreach (var profile in profiles.Take(_options.MaxCandidatesPerCompany))
        {
            ct.ThrowIfCancellationRequested();

            // Ozet de veriliyor: baslik kirpilmissa unvan oradan tamamlanir.
            var parsed = LinkedInTitleParser.Parse(profile.Title, profile.Url, profile.Snippet);
            if (parsed is null || string.IsNullOrWhiteSpace(parsed.Title))
                continue;

            // Google'in "site:" + tam ifade aramasi bazen alakasiz kisileri de
            // getiriyor: firma adi sonucun basliginda/ozetinde hic gecmeyebilir,
            // sadece "SAP" gibi genel bir kelime eslesmis olabilir. Boyle bir
            // adayi Apollo'ya sormadan once bagliligi dogruluyoruz.
            if (!LinkedInRelevanceFilter.MentionsCompany(company.Name, profile.Title, profile.Snippet))
            {
                _logger.LogDebug(
                    "Firma adı sonuçta geçmiyor, alakasız kabul edildi: {Title}", profile.Title);
                continue;
            }

            var titleScore = ResolveTitleScore(parsed.Title, keywords);
            if (titleScore is null) continue;

            var (first, last) = parsed.SplitName();
            if (string.IsNullOrWhiteSpace(last))
            {
                _logger.LogDebug("Soyad ayrıştırılamadı, Apollo sorgulanmadı: {Title}", profile.Title);
                continue;
            }

            var match = await _emailFinder.MatchAsync(first, last, company.Domain!, ct);

            contacts.Add(new Contact
            {
                Name = parsed.Name,
                Title = parsed.Title,
                Email = match.Email,
                Phone = match.Phone,
                SourceUrl = parsed.ProfileUrl,
                Source = ContactSource.LinkedInApollo,
                TitleScore = titleScore.Value
            });
        }

        return contacts;
    }

    /// <summary>
    /// Unvan puani sabit bir tablodan gelir; Ayarlar ekranina yeni bir unvan
    /// eklendiginde ("ERP Müdürü" gibi) o tabloda karsiligi olmayabilir. Bu
    /// durumda aday, ayarlardaki anahtar kelimeyle eslestigi icin kabul edilir ve
    /// esik puanla kaydedilir — aksi halde ayarlardan girilen unvanlar hicbir ise
    /// yaramazdi. Hicbiri tutmuyorsa null doner: aday elenir.
    /// </summary>
    private int? ResolveTitleScore(string title, List<string> keywords)
    {
        var score = _scoring.ScoreTitle(title);
        if (score >= _options.MinTitleScore) return score;

        return keywords.Any(k => TurkishText.ContainsWord(title, k))
            ? _options.MinTitleScore
            : null;
    }
}
