using EgebisLeadFinder.Configuration;
using EgebisLeadFinder.Models;
using EgebisLeadFinder.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EgebisLeadFinder.Tests;

/// <summary>
/// API anahtari ne veritabaninda ne yapilandirmada varsa, servis calismayi
/// durdurup kullaniciyi Ayarlar ekranina yonlendiren mesaj vermeli.
/// </summary>
public class MissingApiKeyTests
{
    private static SerperSearchService CreateSearch(ISettingsService settings) =>
        new(new HttpClient(new NeverCalledHandler()),
            Options.Create(new SearchOptions()),
            settings,
            new FakeApiUsageTracker(),
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<SerperSearchService>.Instance);

    [Fact]
    public async Task Serper_anahtari_yoksa_firma_aramasi_uyari_firlatir()
    {
        var service = CreateSearch(new FakeSettingsService());

        var ex = await Assert.ThrowsAsync<MissingApiKeyException>(
            () => service.SearchCompaniesAsync(new SearchCriteria { Industry = "Otomotiv" }));

        Assert.Contains("Ayarlar", ex.Message);
    }

    [Fact]
    public async Task Serper_anahtari_yoksa_linkedin_aramasi_uyari_firlatir()
    {
        var service = CreateSearch(new FakeSettingsService());

        await Assert.ThrowsAsync<MissingApiKeyException>(
            () => service.SearchLinkedInProfilesAsync("Test A.Ş."));
    }

    [Fact]
    public async Task Gemini_anahtari_yoksa_analiz_uyari_mesaji_doner()
    {
        var service = new GeminiAiService(
            new HttpClient(new NeverCalledHandler()),
            Options.Create(new AiOptions()),
            new FakeSettingsService(),
            new FakeApiUsageTracker(),
            NullLogger<GeminiAiService>.Instance);

        var result = await service.AnalyzeCompanyAsync("firma metni");

        Assert.False(result.Success);
        Assert.Contains("Ayarlar", result.Error);
    }

    [Fact]
    public async Task Apollo_anahtari_yoksa_eslesme_uyari_mesaji_doner()
    {
        var service = new ApolloPersonEmailFinder(
            new HttpClient(new NeverCalledHandler()),
            Options.Create(new ApolloOptions()),
            new FakeSettingsService(),
            NullLogger<ApolloPersonEmailFinder>.Instance);

        var result = await service.MatchAsync("Ahmet", "Yılmaz", "test.com");

        Assert.False(result.Found);
        Assert.Contains("Ayarlar", result.Error);
    }

    [Fact]
    public async Task Anahtar_ayarlardan_okunup_isteje_konur()
    {
        var settings = new FakeSettingsService(new Dictionary<string, string?>
        {
            [SettingKeys.SerperApiKey] = "ayarlardan-gelen-anahtar"
        });

        var handler = new RecordingHandler();
        var service = new SerperSearchService(
            new HttpClient(handler),
            Options.Create(new SearchOptions()),
            settings,
            new FakeApiUsageTracker(),
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<SerperSearchService>.Instance);

        await service.SearchLinkedInProfilesAsync("Test A.Ş.");

        // Anahtar Ayarlar'dan okunup istek basligina konmus olmali.
        Assert.Equal("ayarlardan-gelen-anahtar", handler.LastApiKey);
    }

    /// <summary>Anahtar yokken hicbir dis istek atilmadigini garanti eder.</summary>
    private class NeverCalledHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new InvalidOperationException("Anahtar yokken dış istek atılmamalıydı.");
    }

    /// <summary>Istekte kullanilan anahtari kaydeder, bos sonuc doner.</summary>
    private class RecordingHandler : HttpMessageHandler
    {
        public string? LastApiKey { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastApiKey = request.Headers.TryGetValues("X-API-KEY", out var v) ? v.First() : null;

            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("{\"organic\":[]}", System.Text.Encoding.UTF8, "application/json")
            });
        }
    }
}
