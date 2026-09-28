using System.Net;
using EgebisLeadFinder.Configuration;
using EgebisLeadFinder.Models;
using EgebisLeadFinder.Services;
using EgebisLeadFinder.Services.Progress;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EgebisLeadFinder.Tests;

/// <summary>Yonetici istekleri: arama iptali, arama servisi hatalari, "Incelenemedi" durumu.</summary>
public class ManagerRequestTests
{
    // ---------------- Madde 1: iptal ----------------

    [Fact]
    public void Is_iptal_edilince_token_iptal_olur_ve_durum_cancelled_olur()
    {
        var store = new JobProgressStore();
        var job = store.Create();

        Assert.False(job.Token.IsCancellationRequested);
        Assert.True(store.Cancel(job.Id));
        Assert.True(job.Token.IsCancellationRequested);
        Assert.Equal(JobState.Running, job.State);

        store.MarkCancelled(job.Id, "/sonuc", payload: 42);
        Assert.Equal(JobState.Cancelled, job.State);
        Assert.Equal("/sonuc", job.ResultUrl);

        // Bitmis is tekrar iptal edilemez.
        Assert.False(store.Cancel(job.Id));
    }

    [Fact]
    public void Bitmis_is_iptal_edilemez()
    {
        var store = new JobProgressStore();
        var job = store.Create();
        store.Complete(job.Id, "/sonuc");

        Assert.False(store.Cancel(job.Id));
        Assert.False(job.Token.IsCancellationRequested);
    }

    // ---------------- Madde 3: arama servisi hatalari ----------------

    [Theory]
    [InlineData(403, """{"message":"Unauthorized."}""", "anahtarı geçersiz")]
    [InlineData(401, "", "anahtarı geçersiz")]
    [InlineData(400, """{"message":"Not enough credits"}""", "kredisi/kotası doldu")]
    [InlineData(429, "", "geçici olarak")]
    public void Serper_hatasi_aciklayici_mesaja_cevrilir(int status, string body, string expected)
    {
        var ex = SearchProviderException.FromSerper(status, body);
        Assert.NotNull(ex);
        Assert.Contains(expected, ex!.Message);
    }

    [Fact]
    public void Gecici_serper_hatasi_aramayi_durdurmaz()
    {
        Assert.Null(SearchProviderException.FromSerper(500, "internal"));
        Assert.Null(SearchProviderException.FromSerper(400, "bad query"));
    }

    [Fact]
    public async Task Serper_kredisi_bitince_hata_yutulmaz()
    {
        var service = CreateSerper(HttpStatusCode.BadRequest, """{"message":"Not enough credits","statusCode":400}""");

        var ex = await Assert.ThrowsAsync<SearchProviderException>(() => service.SearchCompaniesAsync(
            new SearchCriteria { Industry = "Otomotiv", MaxCompanies = 5 }));

        Assert.Contains("kredisi/kotası doldu", ex.Message);
    }

    [Fact]
    public async Task Gecersiz_serper_anahtari_hata_olarak_doner()
    {
        var service = CreateSerper(HttpStatusCode.Forbidden, """{"message":"Unauthorized."}""");

        await Assert.ThrowsAsync<SearchProviderException>(() => service.SearchCompaniesAsync(
            new SearchCriteria { Industry = "Otomotiv", MaxCompanies = 5 }));
    }

    [Fact]
    public async Task Tek_sorgu_hatasi_bos_sonuc_doner()
    {
        var service = CreateSerper(HttpStatusCode.InternalServerError, "oops");

        var results = await service.SearchCompaniesAsync(new SearchCriteria { Industry = "Otomotiv", MaxCompanies = 5 });

        Assert.Empty(results);
    }

    // ---------------- Madde 3: "Incelenemedi" ----------------

    [Fact]
    public void Incelenemedi_notu_500_karakterle_sinirlanir()
    {
        var company = new Company { Name = "Örnek" };
        company.MarkNotEvaluated(new string('x', 800));

        Assert.Equal(EvaluationStatus.NotEvaluated, company.EvaluationStatus);
        Assert.Equal(500, company.EvaluationNote!.Length);
    }

    private static SerperSearchService CreateSerper(HttpStatusCode status, string body) =>
        new(new HttpClient(new StaticHandler(status, body)),
            Options.Create(new SearchOptions { UsePlaces = false, CacheHours = 0 }),
            new FakeSettingsService(new Dictionary<string, string?> { [SettingKeys.SerperApiKey] = "test-anahtar" }),
            new FakeApiUsageTracker(),
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<SerperSearchService>.Instance);

    private sealed class StaticHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
            });
    }
}
