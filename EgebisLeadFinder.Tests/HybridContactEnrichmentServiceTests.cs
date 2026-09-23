using EgebisLeadFinder.Configuration;
using EgebisLeadFinder.Models;
using EgebisLeadFinder.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EgebisLeadFinder.Tests;

/// <summary>
/// Iki asamali zenginlestirme: once Apollo kisi aramasi, sonuc yoksa LinkedIn yedegi.
/// Testlerin cogu yedek akisi dogrular; Apollo'nun devrede oldugu durumlar ayrica isaretli.
/// </summary>
public class HybridContactEnrichmentServiceTests
{
    private static LeadScoringService CreateScoring() =>
        new(Options.Create(new ScoringOptions
        {
            TitleScores = new Dictionary<string, int>
            {
                ["cto"] = 95,
                ["bilgi işlem müdürü"] = 95,
                ["sap danışmanı"] = 90,
                ["it manager"] = 80,
                ["satış temsilcisi"] = 20
            }
        }));

    private static HybridContactEnrichmentService CreateService(
        FakeSearchService search,
        FakeEmailFinder emailFinder,
        EnrichmentOptions? options = null,
        List<string>? titleKeywords = null,
        FakePeopleSearch? peopleSearch = null) =>
        new(peopleSearch ?? FakePeopleSearch.Unavailable(),
            search, emailFinder, CreateScoring(),
            new FakeSettingsService(titleKeywords: titleKeywords),
            Options.Create(options ?? new EnrichmentOptions { Enabled = true, MinTitleScore = 60, MaxCandidatesPerCompany = 3 }),
            NullLogger<HybridContactEnrichmentService>.Instance);

    // ----- Apollo kisi aramasi (birinci yol) -----

    [Fact]
    public async Task Apollo_sonuc_dondurunce_linkedin_hic_aranmaz()
    {
        var apollo = FakePeopleSearch.Returning(new PersonCandidate
        {
            Name = "Emrah Arslan",
            FirstName = "Emrah",
            LastName = "Arslan",
            Title = "SAP Danışmanı",
            Email = "emrah@test.com",
            ProfileUrl = "https://linkedin.com/in/emrah"
        });

        var search = new FakeSearchService(new List<SearchResult>());
        var emailFinder = new FakeEmailFinder();

        var service = CreateService(search, emailFinder, peopleSearch: apollo);
        var result = await service.EnrichAsync(new Company { Name = "Test A.Ş.", Domain = "test.com" });

        var contact = Assert.Single(result.Contacts);
        Assert.Equal("Emrah Arslan", contact.Name);
        Assert.Equal("SAP Danışmanı", contact.Title);
        Assert.Equal(ContactSource.ApolloSearch, contact.Source);
        Assert.Equal(90, contact.TitleScore);

        // Apollo yeterli oldu: ne LinkedIn aramasi ne de ayri e-posta sorgusu gerekti.
        Assert.False(search.WasCalled);
        Assert.False(emailFinder.WasCalled);
    }

    [Fact]
    public async Task Apollo_unvani_tam_dondurur_kirpilmis_metin_kaydedilmez()
    {
        // Asil kazanim: Google basligindan gelen "... şirketinde Sap ..." yerine
        // yapisal ve tam unvan kaydedilir.
        var apollo = FakePeopleSearch.Returning(new PersonCandidate
        {
            Name = "Emrah Arslan",
            FirstName = "Emrah",
            LastName = "Arslan",
            Title = "SAP Danışmanı ve Bilgi İşlem Müdürü",
            Email = "emrah@test.com"
        });

        var service = CreateService(new FakeSearchService(new List<SearchResult>()),
            new FakeEmailFinder(), peopleSearch: apollo);

        var result = await service.EnrichAsync(new Company { Name = "Test A.Ş.", Domain = "test.com" });

        var contact = Assert.Single(result.Contacts);
        Assert.Equal("SAP Danışmanı ve Bilgi İşlem Müdürü", contact.Title);
        Assert.DoesNotContain("...", contact.Title);
    }

    [Fact]
    public async Task Apollo_listelemesi_email_acmaz_kredisiz_kalir()
    {
        // "api_search" e-postayi hic acmaz; kullanici kisi bazinda "E-postayı Aç"
        // butonuna basana kadar (CompanyController.RevealContactEmail) hicbir
        // kredi harcanmamali — bu yuzden EnrichAsync sirasinda emailFinder hic
        // cagrilmamali, ApolloId sadece kaydedilmeli.
        var apollo = FakePeopleSearch.Returning(new PersonCandidate
        {
            Name = "Emrah Ar***n", // "api_search" soyadi boyle kismi gizli dondurur
            FirstName = "Emrah",
            LastName = "Ar***n",
            Title = "CTO",
            Email = null,
            ApolloId = "apollo-id-1"
        });

        var emailFinder = new FakeEmailFinder { EmailToReturn = "emrah@test.com" };

        var service = CreateService(new FakeSearchService(new List<SearchResult>()),
            emailFinder, peopleSearch: apollo);

        var result = await service.EnrichAsync(new Company { Name = "Test A.Ş.", Domain = "test.com" });

        var contact = Assert.Single(result.Contacts);
        Assert.Null(contact.Email);
        Assert.Equal("apollo-id-1", contact.ApolloId);
        Assert.False(emailFinder.WasCalled);
    }

    [Fact]
    public async Task Apollo_erisilemezse_linkedin_yedegine_dusulur()
    {
        // Ucretli plan yok / anahtar yok: bu bir hata degil, yedek akisa gecis sebebi.
        var search = new FakeSearchService(new List<SearchResult>
        {
            new() { Title = "Ahmet Yılmaz - CTO - Test A.Ş. | LinkedIn", Url = "url1" }
        });
        var emailFinder = new FakeEmailFinder { EmailToReturn = "ahmet@test.com" };

        var service = CreateService(search, emailFinder, peopleSearch: FakePeopleSearch.Unavailable());
        var result = await service.EnrichAsync(new Company { Name = "Test A.Ş.", Domain = "test.com" });

        var contact = Assert.Single(result.Contacts);
        Assert.Equal(ContactSource.LinkedInApollo, contact.Source);
        Assert.True(search.WasCalled);
    }

    [Fact]
    public async Task Apollo_firmayi_tanimiyorsa_linkedin_yedegine_dusulur()
    {
        // Apollo calisti ama bu firmada kimse yok — Turk KOBI'lerinde beklenen durum.
        var search = new FakeSearchService(new List<SearchResult>
        {
            new() { Title = "Ahmet Yılmaz - CTO - Test A.Ş. | LinkedIn", Url = "url1" }
        });
        var emailFinder = new FakeEmailFinder { EmailToReturn = "ahmet@test.com" };

        var service = CreateService(search, emailFinder, peopleSearch: FakePeopleSearch.Empty());
        var result = await service.EnrichAsync(new Company { Name = "Test A.Ş.", Domain = "test.com" });

        var contact = Assert.Single(result.Contacts);
        Assert.Equal(ContactSource.LinkedInApollo, contact.Source);
        Assert.True(search.WasCalled);
    }

    // ----- LinkedIn yedegi (ikinci yol) -----

    [Fact]
    public async Task Ayarlardaki_unvan_puan_tablosunda_olmasa_da_aday_kabul_edilir()
    {
        // Ayarlar ekranina "ERP Müdürü" eklendiginde, bu unvan sabit puan
        // tablosunda olmadigi icin 0 puan alir. Yine de kabul edilmeli;
        // aksi halde ayarlardan girilen unvanlar hicbir ise yaramaz.
        var search = new FakeSearchService(new List<SearchResult>
        {
            new() { Title = "Ayşe Kaya - ERP Müdürü - Test A.Ş. | LinkedIn", Url = "url1" }
        });
        var emailFinder = new FakeEmailFinder { EmailToReturn = "ayse@test.com" };

        var service = CreateService(search, emailFinder,
            titleKeywords: new List<string> { "ERP Müdürü" });
        var company = new Company { Name = "Test A.Ş.", Domain = "test.com" };

        var result = await service.EnrichAsync(company);

        var contact = Assert.Single(result.Contacts);
        Assert.Equal("Ayşe Kaya", contact.Name);
        Assert.Equal(60, contact.TitleScore); // esik puanla kaydedilir
    }

    [Fact]
    public async Task Ayarlarda_olmayan_unvan_yine_elenir()
    {
        var search = new FakeSearchService(new List<SearchResult>
        {
            new() { Title = "Mehmet Demir - Aşçıbaşı - Test A.Ş. | LinkedIn", Url = "url1" }
        });
        var emailFinder = new FakeEmailFinder();

        var service = CreateService(search, emailFinder,
            titleKeywords: new List<string> { "ERP Müdürü" });
        var company = new Company { Name = "Test A.Ş.", Domain = "test.com" };

        var result = await service.EnrichAsync(company);

        Assert.Empty(result.Contacts);
        Assert.False(emailFinder.WasCalled);
    }

    [Fact]
    public async Task Domain_yoksa_basarisiz_doner()
    {
        var service = CreateService(new FakeSearchService(new List<SearchResult>()), new FakeEmailFinder());
        var company = new Company { Name = "Test A.Ş.", Domain = null };

        var result = await service.EnrichAsync(company);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task Dusuk_unvan_puanli_adaylar_apolloya_sorulmaz()
    {
        var search = new FakeSearchService(new List<SearchResult>
        {
            new() { Title = "Mehmet Demir - Satış Temsilcisi - Test A.Ş. | LinkedIn", Url = "url1" }
        });
        var emailFinder = new FakeEmailFinder();

        var service = CreateService(search, emailFinder);
        var company = new Company { Name = "Test A.Ş.", Domain = "test.com" };

        var result = await service.EnrichAsync(company);

        Assert.Empty(result.Contacts);
        Assert.False(emailFinder.WasCalled);
    }

    [Fact]
    public async Task Firma_adi_sonucta_gecmeyen_yuksek_unvanli_aday_elenir()
    {
        // Gercek vaka: "Toksan Otomotiv" aramasinda Google, Toksan ile hicbir ilgisi
        // olmayan bir SAP danismanini sadece "SAP" kelimesi eslestigi icin getirmisti.
        var search = new FakeSearchService(new List<SearchResult>
        {
            new()
            {
                Title = "Erdem Bulut - SAP Danışmanı | LinkedIn",
                Url = "url1",
                Snippet = "Freelance SAP PS PPM Senior Consultant. SAP sertifikaları."
            }
        });
        var emailFinder = new FakeEmailFinder { EmailToReturn = "a@test.com" };

        var service = CreateService(search, emailFinder);
        var company = new Company { Name = "Toksan Otomotiv A.Ş.", Domain = "toksanotomotiv.com" };

        var result = await service.EnrichAsync(company);

        Assert.Empty(result.Contacts);
        Assert.False(emailFinder.WasCalled);
    }

    [Fact]
    public async Task Firma_adi_ozette_gecen_aday_kabul_edilir()
    {
        // Unvan basligin kendisinde degil, ozette firma adiyla birlikte gecebilir.
        var search = new FakeSearchService(new List<SearchResult>
        {
            new()
            {
                Title = "Ahmet Yılmaz - CTO | LinkedIn",
                Url = "url1",
                Snippet = "Toksan Otomotiv A.Ş. bünyesinde CTO olarak görev yapıyor."
            }
        });
        var emailFinder = new FakeEmailFinder { EmailToReturn = "ahmet@toksanotomotiv.com" };

        var service = CreateService(search, emailFinder);
        var company = new Company { Name = "Toksan Otomotiv A.Ş.", Domain = "toksanotomotiv.com" };

        var result = await service.EnrichAsync(company);

        var contact = Assert.Single(result.Contacts);
        Assert.Equal("ahmet@toksanotomotiv.com", contact.Email);
    }

    [Fact]
    public async Task Kirpilmis_unvan_ozetten_tamamlanir()
    {
        // Google uzun basligi kirpar. Ozette ayni cumle tam gectigi icin
        // unvan oradan kurtarilir; veritabanina "..." kaydedilmez.
        var search = new FakeSearchService(new List<SearchResult>
        {
            new()
            {
                Title = "Ahmet Yılmaz - Toksan Otomotiv A.Ş. şirketinde Bilgi ... | LinkedIn",
                Url = "url1",
                Snippet = "Toksan Otomotiv A.Ş. şirketinde Bilgi İşlem Müdürü · Deneyim: 12 yıl"
            }
        });
        var emailFinder = new FakeEmailFinder { EmailToReturn = "ahmet@toksanotomotiv.com" };

        var service = CreateService(search, emailFinder);
        var company = new Company { Name = "Toksan Otomotiv A.Ş.", Domain = "toksanotomotiv.com" };

        var result = await service.EnrichAsync(company);

        var contact = Assert.Single(result.Contacts);
        Assert.Equal("Bilgi İşlem Müdürü", contact.Title);
        Assert.Equal(95, contact.TitleScore);
    }

    [Fact]
    public async Task Yuksek_unvan_puanli_aday_apolloya_sorulur_ve_kaydedilir()
    {
        var search = new FakeSearchService(new List<SearchResult>
        {
            new() { Title = "Ahmet Yılmaz - CTO - Test A.Ş. | LinkedIn", Url = "https://linkedin.com/in/ahmet" }
        });
        var emailFinder = new FakeEmailFinder { EmailToReturn = "ahmet.yilmaz@test.com" };

        var service = CreateService(search, emailFinder);
        var company = new Company { Name = "Test A.Ş.", Domain = "test.com" };

        var result = await service.EnrichAsync(company);

        var contact = Assert.Single(result.Contacts);
        Assert.Equal("Ahmet Yılmaz", contact.Name);
        Assert.Equal("CTO", contact.Title);
        Assert.Equal("ahmet.yilmaz@test.com", contact.Email);
        Assert.Equal(ContactSource.LinkedInApollo, contact.Source);
        Assert.Equal(95, contact.TitleScore);
        Assert.Equal("Ahmet", emailFinder.LastFirstName);
        Assert.Equal("Yılmaz", emailFinder.LastLastName);
        Assert.Equal("test.com", emailFinder.LastDomain);
    }

    [Fact]
    public async Task Apollo_eslesme_bulamazsa_kisi_yine_de_email_olmadan_eklenir()
    {
        var search = new FakeSearchService(new List<SearchResult>
        {
            new() { Title = "Ahmet Yılmaz - CTO - Test A.Ş. | LinkedIn", Url = "url1" }
        });
        var emailFinder = new FakeEmailFinder { EmailToReturn = null };

        var service = CreateService(search, emailFinder);
        var company = new Company { Name = "Test A.Ş.", Domain = "test.com" };

        var result = await service.EnrichAsync(company);

        var contact = Assert.Single(result.Contacts);
        Assert.Null(contact.Email);
        Assert.Equal(ContactSource.LinkedInApollo, contact.Source);
    }

    [Fact]
    public async Task Aday_sayisi_MaxCandidatesPerCompany_ile_sinirlanir()
    {
        var search = new FakeSearchService(new List<SearchResult>
        {
            new() { Title = "Kişi Bir - CTO - Test A.Ş. | LinkedIn", Url = "url1" },
            new() { Title = "Kişi İki - CTO - Test A.Ş. | LinkedIn", Url = "url2" },
            new() { Title = "Kişi Üç - CTO - Test A.Ş. | LinkedIn", Url = "url3" },
            new() { Title = "Kişi Dört - CTO - Test A.Ş. | LinkedIn", Url = "url4" }
        });
        var emailFinder = new FakeEmailFinder { EmailToReturn = "a@test.com" };

        var service = CreateService(search, emailFinder,
            new EnrichmentOptions { Enabled = true, MinTitleScore = 60, MaxCandidatesPerCompany = 2 });
        var company = new Company { Name = "Test A.Ş.", Domain = "test.com" };

        var result = await service.EnrichAsync(company);

        Assert.Equal(2, result.Contacts.Count);
    }

    [Fact]
    public async Task Soyadi_ayristirilamayan_aday_apolloya_sorulmaz()
    {
        var search = new FakeSearchService(new List<SearchResult>
        {
            new() { Title = "Ahmet - CTO - Test A.Ş. | LinkedIn", Url = "url1" }
        });
        var emailFinder = new FakeEmailFinder { EmailToReturn = "a@test.com" };

        var service = CreateService(search, emailFinder);
        var company = new Company { Name = "Test A.Ş.", Domain = "test.com" };

        var result = await service.EnrichAsync(company);

        Assert.Empty(result.Contacts);
        Assert.False(emailFinder.WasCalled);
    }

    // ----- Sahteler -----

    private class FakePeopleSearch : IPeopleSearchService
    {
        private readonly PeopleSearchResult _result;
        private FakePeopleSearch(PeopleSearchResult result) => _result = result;

        /// <summary>Anahtar/plan yok: yedek akisa gecilmeli.</summary>
        public static FakePeopleSearch Unavailable() =>
            new(PeopleSearchResult.NotConfigured("Apollo araması kullanılamıyor."));

        /// <summary>Apollo calisti ama bu firmada kimse bulamadi.</summary>
        public static FakePeopleSearch Empty() => new(PeopleSearchResult.Empty);

        public static FakePeopleSearch Returning(params PersonCandidate[] people) =>
            new(new PeopleSearchResult { People = people.ToList() });

        public Task<PeopleSearchResult> SearchAsync(
            string domain, IReadOnlyList<string> titleKeywords, int maxResults, CancellationToken ct = default) =>
            Task.FromResult(_result);
    }

    private class FakeSearchService : ISearchService
    {
        private readonly List<SearchResult> _linkedInResults;
        public bool WasCalled { get; private set; }

        public FakeSearchService(List<SearchResult> linkedInResults) => _linkedInResults = linkedInResults;

        public Task<List<SearchResult>> SearchCompaniesAsync(SearchCriteria criteria, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<List<SearchResult>> SearchLinkedInProfilesAsync(string companyName, CancellationToken ct = default)
        {
            WasCalled = true;
            return Task.FromResult(_linkedInResults);
        }

        public Task<List<SearchResult>> SearchAsync(string query, int maxResults, CancellationToken ct = default) =>
            Task.FromResult(new List<SearchResult>());
    }

    private class FakeEmailFinder : IPersonEmailFinder
    {
        public string? EmailToReturn { get; set; }
        public bool WasCalled { get; private set; }
        public string? LastFirstName { get; private set; }
        public string? LastLastName { get; private set; }
        public string? LastDomain { get; private set; }
        public string? LastApolloId { get; private set; }

        public Task<PersonMatchResult> MatchAsync(string firstName, string lastName, string domain, CancellationToken ct = default)
        {
            WasCalled = true;
            LastFirstName = firstName;
            LastLastName = lastName;
            LastDomain = domain;

            return Task.FromResult(EmailToReturn is null
                ? PersonMatchResult.NotFound()
                : new PersonMatchResult { Email = EmailToReturn });
        }

        public Task<PersonMatchResult> MatchByIdAsync(string apolloId, CancellationToken ct = default)
        {
            WasCalled = true;
            LastApolloId = apolloId;

            return Task.FromResult(EmailToReturn is null
                ? PersonMatchResult.NotFound()
                : new PersonMatchResult { Email = EmailToReturn });
        }
    }
}
