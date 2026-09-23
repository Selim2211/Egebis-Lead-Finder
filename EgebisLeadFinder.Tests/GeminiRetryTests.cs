using System.Net;
using EgebisLeadFinder.Configuration;
using EgebisLeadFinder.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EgebisLeadFinder.Tests;

/// <summary>
/// Gemini cagrisinin tekrar deneme davranisi: gecici hatalarda (429/5xx)
/// artan bekleme ile tekrar denenir, kalici hatalarda (4xx) denenmez.
/// </summary>
public class GeminiRetryTests
{
    private const string SuccessBody = """
        {"candidates":[{"content":{"parts":[{"text":"{\"companyName\":\"Test\",\"industry\":\"Otomotiv\",\"manufacturer\":true,\"sap\":\"yes\",\"sapVendor\":false,\"potential\":true,\"reason\":\"r\",\"recommendedTemplate\":\"GENEL\"}"}]}}]}
        """;

    private static GeminiAiService CreateService(HttpMessageHandler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://generativelanguage.googleapis.com") };
        var options = Options.Create(new AiOptions
        {
            GeminiApiKey = "ana-anahtar",
            GeminiModel = "gemini-3.6-flash",
            GeminiEndpoint = "https://generativelanguage.googleapis.com/v1beta/models",
            TimeoutSeconds = 5,
            RetryBaseDelayMs = 1
        });

        var settings = FakeSettingsService.WithGeminiKeys("ana-anahtar");

        return new GeminiAiService(http, options, settings, new FakeApiUsageTracker(), NullLogger<GeminiAiService>.Instance);
    }

    [Fact]
    public async Task Basarili_yanitta_tek_istek_atilir()
    {
        var callCount = 0;
        var handler = new FakeHandler(_ =>
        {
            callCount++;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(SuccessBody) };
        });

        var result = await CreateService(handler).AnalyzeCompanyAsync("firma metni");

        Assert.True(result.Success);
        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task Gecici_429_sonrasi_tekrar_denenir_ve_basarili_olur()
    {
        var callCount = 0;
        var handler = new FakeHandler(_ =>
        {
            callCount++;
            // Ilk iki deneme kota hatasi, ucuncusu basarili.
            return callCount <= 2
                ? new HttpResponseMessage((HttpStatusCode)429)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(SuccessBody) };
        });

        var result = await CreateService(handler).AnalyzeCompanyAsync("firma metni");

        Assert.True(result.Success);
        Assert.Equal(3, callCount);
    }

    [Fact]
    public async Task Kota_surekli_doluysa_hata_doner()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage((HttpStatusCode)429));

        var result = await CreateService(handler).AnalyzeCompanyAsync("firma metni");

        Assert.False(result.Success);
    }

    [Fact]
    public async Task Kalici_hatada_tekrar_denenmez()
    {
        // 400 (gecersiz istek) gibi kalici bir hatada tekrar denemek anlamsizdir.
        var callCount = 0;
        var handler = new FakeHandler(_ =>
        {
            callCount++;
            return new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("{\"error\":\"bad\"}")
            };
        });

        var result = await CreateService(handler).AnalyzeCompanyAsync("firma metni");

        Assert.False(result.Success);
        Assert.Equal(1, callCount);
    }

    private class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(_responder(request));
    }
}
