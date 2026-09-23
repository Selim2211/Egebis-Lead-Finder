using System.Net;
using EgebisLeadFinder.Configuration;
using EgebisLeadFinder.Models;
using EgebisLeadFinder.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EgebisLeadFinder.Tests;

/// <summary>
/// Kota bitimi ile dakikalik hiz limiti ayri islenmeli: hiz limitinde beklenip
/// tekrar denenir, kota bitiminde akis hemen durur (bosa bekleme ve kredi yok).
/// </summary>
public class QuotaTests
{
    private const string QuotaBody = """
        {"error":{"code":429,"message":"You exceeded your current quota","details":[
          {"@type":"type.googleapis.com/google.rpc.QuotaFailure","violations":[
            {"quotaMetric":"generativelanguage.googleapis.com/generate_content_free_tier_requests"}]}]}}
        """;

    private const string RateLimitBody = """
        {"error":{"code":429,"message":"Resource has been exhausted","details":[
          {"@type":"type.googleapis.com/google.rpc.RetryInfo","retryDelay":"7s"}]}}
        """;

    [Fact]
    public void Kota_govdesi_taninir()
    {
        Assert.True(QuotaExceededException.LooksLikeQuota(QuotaBody));
        Assert.False(QuotaExceededException.LooksLikeQuota(RateLimitBody));
        Assert.False(QuotaExceededException.LooksLikeQuota(""));
        Assert.False(QuotaExceededException.LooksLikeQuota(null));
    }

    [Fact]
    public async Task Kota_dolunca_tekrar_denenmez()
    {
        var handler = new CountingHandler(HttpStatusCode.TooManyRequests, QuotaBody);
        var service = CreateGemini(handler);

        await Assert.ThrowsAsync<QuotaExceededException>(
            () => service.AnalyzeCompanyAsync("firma metni"));

        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Serper_ayni_sorguyu_onbellekten_doner()
    {
        var handler = new CountingHandler(HttpStatusCode.OK, """{"organic":[{"link":"https://ornek.com","title":"Örnek"}]}""");
        var settings = new FakeSettingsService(new Dictionary<string, string?>
        {
            [SettingKeys.SerperApiKey] = "test-anahtar"
        });

        var service = new SerperSearchService(
            new HttpClient(handler),
            Options.Create(new SearchOptions { UsePlaces = false, CacheHours = 6 }),
            settings,
            new FakeApiUsageTracker(),
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<SerperSearchService>.Instance);

        await service.SearchLinkedInProfilesAsync("Örnek A.Ş.");
        var afterFirst = handler.Calls;
        await service.SearchLinkedInProfilesAsync("Örnek A.Ş.");

        Assert.True(afterFirst > 0);
        Assert.Equal(afterFirst, handler.Calls);
    }

    private static GeminiAiService CreateGemini(HttpMessageHandler handler) =>
        new(new HttpClient(handler),
            Options.Create(new AiOptions { RetryBaseDelayMs = 1 }),
            new FakeSettingsService(new Dictionary<string, string?>
            {
                [SettingKeys.GeminiApiKey] = "test-anahtar"
            }),
            new FakeApiUsageTracker(),
            NullLogger<GeminiAiService>.Instance);

    private class CountingHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;

        public CountingHandler(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, System.Text.Encoding.UTF8, "application/json")
            });
        }
    }
}
