using EgebisLeadFinder.Configuration;
using EgebisLeadFinder.Models;
using EgebisLeadFinder.Services;
using EgebisLeadFinder.Services.CompanyIntel;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EgebisLeadFinder.Tests;

public class NewsIntelSourceTests
{
    private static NewsIntelSource Create(FakeSearch search, ResearchOptions? options = null) =>
        new(search, Options.Create(options ?? new ResearchOptions()), NullLogger<NewsIntelSource>.Instance);

    [Fact]
    public async Task Firma_adi_yoksa_hata_doner()
    {
        var intel = await Create(new FakeSearch()).CollectAsync(new Company { Name = "" });

        Assert.NotNull(intel.Error);
        Assert.False(intel.FoundSomething);
    }

    [Fact]
    public async Task Sorgu_sayisi_MaxNewsQueries_ile_sinirlanir()
    {
        var search = new FakeSearch();
        var options = new ResearchOptions { MaxNewsQueries = 2 };

        await Create(search, options).CollectAsync(new Company { Name = "Trakya Döküm" });

        Assert.Equal(2, search.Queries.Count);
    }

    [Fact]
    public async Task Ayni_url_birden_fazla_sorguda_ciksa_bir_kez_eklenir()
    {
        var search = new FakeSearch
        {
            Result = new List<SearchResult>
            {
                new() { Title = "Trakya Döküm yatırım yaptı", Url = "https://haber/1", Snippet = "..." }
            }
        };

        var intel = await Create(search).CollectAsync(new Company { Name = "Trakya Döküm" });

        Assert.Single(intel.Snippets);
    }

    [Fact]
    public async Task Risk_kelimesi_iceren_snippet_Risk_olarak_siniflanir()
    {
        var search = new FakeSearch
        {
            PerQuery = q => q.Contains("konkordato")
                ? new List<SearchResult>
                {
                    new() { Title = "Trakya Döküm konkordato başvurusu", Url = "https://haber/k", Snippet = "mahkeme kararı" }
                }
                : new List<SearchResult>()
        };

        var intel = await Create(search).CollectAsync(new Company { Name = "Trakya Döküm" });

        Assert.Contains(intel.Snippets, s => s.Kind == IntelKind.Risk);
    }

    private class FakeSearch : ISearchService
    {
        public List<string> Queries { get; } = new();
        public List<SearchResult> Result { get; set; } = new();
        public Func<string, List<SearchResult>>? PerQuery { get; set; }

        public Task<List<SearchResult>> SearchAsync(string query, int maxResults, CancellationToken ct = default)
        {
            Queries.Add(query);
            return Task.FromResult(PerQuery?.Invoke(query) ?? Result);
        }

        public Task<List<SearchResult>> SearchCompaniesAsync(SearchCriteria criteria, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<List<SearchResult>> SearchLinkedInProfilesAsync(string companyName, CancellationToken ct = default) =>
            throw new NotImplementedException();
    }
}
