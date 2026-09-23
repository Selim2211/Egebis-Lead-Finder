using System.Net;
using EgebisLeadFinder.Configuration;
using EgebisLeadFinder.Models;
using EgebisLeadFinder.Services;
using EgebisLeadFinder.Services.CompanyIntel;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EgebisLeadFinder.Tests;

public class KapIntelSourceTests
{
    private const string MemberList = """
        [
          { "memberOid": "111", "kapMemberTitle": "TRAKYA CAM SANAYİİ A.Ş.", "stockCode": "TRKCM" },
          { "memberOid": "222", "kapMemberTitle": "AKBANK T.A.Ş.", "stockCode": "AKBNK" }
        ]
        """;

    private static KapIntelSource Create(
        Func<HttpRequestMessage, HttpResponseMessage> responder, ISearchService search)
    {
        // Testte dogrudan uye eslesmesini calistirmak icin uc adresini set et
        // (varsayilan bos: uretimde site: aramasina birakilir).
        var options = Options.Create(new ResearchOptions
        {
            KapMemberListUrl = "https://kap.test/memberList"
        });
        var financials = new KapFinancialClient(
            new HttpClient(new FakeHandler(responder)), options, NullLogger<KapFinancialClient>.Instance);

        return new KapIntelSource(
            new HttpClient(new FakeHandler(responder)), search, financials, options,
            NullLogger<KapIntelSource>.Instance);
    }

    [Fact]
    public async Task KAP_uyesi_firma_yakalanir()
    {
        var intel = await Create(
                _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(MemberList) },
                new NoSearch())
            .CollectAsync(new Company { Name = "Trakya Cam Sanayii A.Ş." });

        Assert.True(intel.FoundSomething);
        Assert.Contains(intel.Snippets, s => s.Text.Contains("KAP'a kayıtlı"));
        Assert.Contains(intel.Links, l => l.Label.Contains("KAP"));
    }

    [Fact]
    public async Task KAP_disindaki_firma_temiz_bos_doner()
    {
        var intel = await Create(
                _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(MemberList) },
                new NoSearch())
            .CollectAsync(new Company { Name = "Bursa Küçük Döküm Ltd. Şti." });

        Assert.False(intel.FoundSomething);
        Assert.Null(intel.Error);
    }

    [Fact]
    public async Task Uye_listesi_erisilemezse_hata_akisi_kirmaz()
    {
        var intel = await Create(
                _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
                new NoSearch())
            .CollectAsync(new Company { Name = "Herhangi A.Ş." });

        Assert.False(intel.FoundSomething);
        Assert.Null(intel.Error);
    }

    [Fact]
    public async Task Uye_degilse_site_aramasi_devreye_girer()
    {
        var search = new StubSearch(new List<SearchResult>
        {
            new() { Title = "Küçük Firma A.Ş. finansal rapor", Url = "https://www.kap.org.tr/x", Snippet = "..." }
        });

        var intel = await Create(
                _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") },
                search)
            .CollectAsync(new Company { Name = "Küçük Firma A.Ş." });

        Assert.Contains(intel.Snippets, s => s.Text.Contains("KAP bildirimi"));
    }

    private class NoSearch : ISearchService
    {
        public Task<List<SearchResult>> SearchAsync(string query, int maxResults, CancellationToken ct = default) =>
            Task.FromResult(new List<SearchResult>());

        public Task<List<SearchResult>> SearchCompaniesAsync(SearchCriteria criteria, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<List<SearchResult>> SearchLinkedInProfilesAsync(string companyName, CancellationToken ct = default) =>
            throw new NotImplementedException();
    }

    private class StubSearch : ISearchService
    {
        private readonly List<SearchResult> _results;
        public StubSearch(List<SearchResult> results) => _results = results;

        public Task<List<SearchResult>> SearchAsync(string query, int maxResults, CancellationToken ct = default) =>
            Task.FromResult(_results);

        public Task<List<SearchResult>> SearchCompaniesAsync(SearchCriteria criteria, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<List<SearchResult>> SearchLinkedInProfilesAsync(string companyName, CancellationToken ct = default) =>
            throw new NotImplementedException();
    }

    private class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(_responder(request));
    }
}
