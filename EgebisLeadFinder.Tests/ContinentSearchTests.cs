using EgebisLeadFinder.Controllers;
using EgebisLeadFinder.Data;
using EgebisLeadFinder.Models;
using EgebisLeadFinder.Services;
using Xunit;

namespace EgebisLeadFinder.Tests;

/// <summary>Kıta/ülke seçimi ve profil kopyalama (Gürcan Bey talebi).</summary>
public class ContinentSearchTests
{
    [Fact]
    public void Her_ulke_bir_kitada_ve_kitalarin_hepsi_sirali_listede()
    {
        Assert.All(SearchRegions.All, r => Assert.Contains(r.Group, SearchRegions.Continents));
        Assert.Equal(SearchRegions.Continents.Count, SearchRegions.Grouped().Select(g => g.Key).Distinct().Count());
        Assert.Equal(SearchRegions.All.Count, SearchRegions.All.Select(r => r.Key).Distinct().Count());
        Assert.Equal("Türkiye", SearchRegions.Grouped().First().Key);
    }

    [Fact]
    public void Ulke_anahtarlari_okunur_bilinmeyen_ve_tekrar_atilir_sira_korunur()
    {
        var list = SearchRegions.ParseKeys("DE, fr ,XX,de,IT");
        Assert.Equal(new[] { "DE", "FR", "IT" }, list.Select(r => r.Key));
        Assert.Empty(SearchRegions.ParseKeys(null));
    }

    [Fact]
    public void Secimin_adi_tek_ulke_kita_tamami_ya_da_liste_olarak_yazilir()
    {
        Assert.Equal("Almanya", SearchRegions.Describe(SearchRegions.ParseKeys("DE")));
        var allEurope = SearchRegions.All.Where(r => r.Group == "Avrupa").Select(r => r.Key);
        Assert.Equal("Avrupa (tüm ülkeler)", SearchRegions.Describe(SearchRegions.ParseKeys(string.Join(",", allEurope))));
        Assert.Equal("Almanya, Fransa, İtalya", SearchRegions.Describe(SearchRegions.ParseKeys("DE,FR,IT")));
        Assert.Equal("Almanya, Fransa, İtalya +1", SearchRegions.Describe(SearchRegions.ParseKeys("DE,FR,IT,ES")));
    }

    [Fact]
    public void Kriter_tek_ulkede_eski_davranisi_korur_cok_ulkede_listeyi_verir()
    {
        Assert.Equal("TR", new SearchCriteria().Regions().Single().Key);
        Assert.Equal("DE", new SearchCriteria { RegionKey = "DE" }.Regions().Single().Key);
        Assert.Equal("FR", new SearchCriteria { Country = "France" }.Regions().Single().Key);
        Assert.Equal(new[] { "DE", "FR" }, new SearchCriteria { RegionKey = "DE", RegionKeys = "DE,FR" }.Regions().Select(r => r.Key));
    }

    [Fact]
    public void Ulke_kopyasi_il_secimini_siler_ve_firma_hedefini_paylastirir()
    {
        var c = new SearchCriteria { Industry = "Otomotiv", RegionKey = "DE", RegionKeys = "DE,FR,IT", City = "Bavyera", MaxCompanies = 30 };
        var copy = c.ForCountry(SearchRegions.Get("FR"), 10);

        Assert.Equal("FR", copy.RegionKey);
        Assert.Equal("France", copy.Country);
        Assert.Null(copy.RegionKeys);
        Assert.Null(copy.City);
        Assert.Equal(10, copy.MaxCompanies);
        Assert.Equal("DE", c.RegionKey);      // asıl kriter değişmez
        Assert.Equal("Bavyera", c.City);
    }

    private sealed class FakeSearch : ISearchService
    {
        public List<(string Region, int Max)> Calls { get; } = new();
        public Func<string, List<SearchResult>>? Respond { get; init; }

        public Task<List<SearchResult>> SearchCompaniesAsync(SearchCriteria criteria, CancellationToken ct = default)
        {
            Calls.Add((criteria.RegionKey!, criteria.MaxCompanies));
            return Task.FromResult(Respond?.Invoke(criteria.RegionKey!) ?? new List<SearchResult>());
        }

        public Task<List<SearchResult>> SearchLinkedInProfilesAsync(string companyName, CancellationToken ct = default) => Task.FromResult(new List<SearchResult>());
        public Task<List<SearchResult>> SearchAsync(string query, int maxResults, CancellationToken ct = default) => Task.FromResult(new List<SearchResult>());
    }

    private static SearchResult Hit(string domain) => new() { Title = domain, Url = "https://" + domain, Domain = domain };

    [Fact]
    public async Task Cok_ulkeli_arama_her_ulkeyi_ayri_tarar_hedefi_paylastirir_ayni_siteyi_bir_kez_alir()
    {
        var search = new FakeSearch
        {
            Respond = key => key switch
            {
                "DE" => new() { Hit("a.de"), Hit("ortak.com") },
                "FR" => new() { Hit("ortak.com"), Hit("b.fr") },
                _ => new() { Hit("c.it") }
            }
        };
        var criteria = new SearchCriteria { Industry = "Otomotiv", RegionKey = "DE", RegionKeys = "DE,FR,IT", MaxCompanies = 30 };

        var result = await MultiCountrySearch.RunAsync(search, criteria, default);

        Assert.Equal(new[] { ("DE", 10), ("FR", 10), ("IT", 10) }, search.Calls);
        Assert.Equal(new[] { "a.de", "ortak.com", "b.fr", "c.it" }, result.Select(r => r.Domain));
        Assert.Equal("Deutschland", result[0].Country);
        Assert.Equal("Italy", result[3].Country);
    }

    [Fact]
    public async Task Tek_ulkede_dogrudan_aramaya_gider()
    {
        var search = new FakeSearch { Respond = _ => new() { Hit("x.de") } };
        var result = await MultiCountrySearch.RunAsync(search, new SearchCriteria { RegionKey = "DE", MaxCompanies = 20 }, default);

        Assert.Single(search.Calls);
        Assert.Equal(20, search.Calls[0].Max);
        Assert.Single(result);
    }

    [Fact]
    public async Task Ikinci_ulkede_kota_hatasi_olursa_ilk_ulkenin_sonuclariyla_kismi_sonuc_doner()
    {
        var search = new FailingSecond();
        var criteria = new SearchCriteria { RegionKey = "DE", RegionKeys = "DE,FR", MaxCompanies = 20 };

        var ex = await Assert.ThrowsAsync<SearchProviderException>(() => MultiCountrySearch.RunAsync(search, criteria, default));

        Assert.Equal("a.de", Assert.Single(ex.Partial).Domain);
        Assert.Contains("Fransa", ex.Message);
    }

    private sealed class FailingSecond : ISearchService
    {
        public Task<List<SearchResult>> SearchCompaniesAsync(SearchCriteria criteria, CancellationToken ct = default) =>
            criteria.RegionKey == "DE"
                ? Task.FromResult(new List<SearchResult> { Hit("a.de") })
                : throw new SearchProviderException("Serper kredisi bitti");

        public Task<List<SearchResult>> SearchLinkedInProfilesAsync(string companyName, CancellationToken ct = default) => Task.FromResult(new List<SearchResult>());
        public Task<List<SearchResult>> SearchAsync(string query, int maxResults, CancellationToken ct = default) => Task.FromResult(new List<SearchResult>());
    }

    [Fact]
    public void Profil_adi_kopyasi_Copy_ekler_ayni_ad_varsa_numara_verir_uzunlugu_asmaz()
    {
        Assert.Equal("Ege Otomotiv - Copy", SearchProfilesController.CopyName("Ege Otomotiv", new[] { "Baska" }));
        Assert.Equal("Ege Otomotiv - Copy 2", SearchProfilesController.CopyName("Ege Otomotiv", new[] { "Ege Otomotiv - Copy" }));
        Assert.Equal("Ege Otomotiv - Copy 3", SearchProfilesController.CopyName("Ege Otomotiv", new[] { "ege otomotiv - copy", "Ege Otomotiv - Copy 2" }));
        var longName = new string('A', 150);
        Assert.True(SearchProfilesController.CopyName(longName, Array.Empty<string>()).Length <= 150);
        Assert.EndsWith(" - Copy", SearchProfilesController.CopyName(longName, Array.Empty<string>()));
    }

    [Fact]
    public void Profil_ozeti_cok_ulkede_ulke_listesini_gosterir_ve_il_gizler()
    {
        var p = new SearchProfile { Id = 1, Name = "A", RegionKey = "DE", RegionKeys = "DE,FR", City = "Bavyera" };
        var dto = SearchProfileDto.From(p, 1, false);

        Assert.Equal(new[] { "DE", "FR" }, dto.RegionKeys);
        Assert.Equal("Almanya, Fransa", dto.RegionName);
        Assert.Null(dto.City);

        var single = SearchProfileDto.From(new SearchProfile { Id = 2, Name = "B", RegionKey = "DE", City = "Bavyera" }, 1, false);
        Assert.Equal(new[] { "DE" }, single.RegionKeys);
        Assert.Equal("Bavyera", single.City);
    }
}
