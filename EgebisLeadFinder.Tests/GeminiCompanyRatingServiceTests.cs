using System.Net;
using EgebisLeadFinder.Configuration;
using EgebisLeadFinder.Models;
using EgebisLeadFinder.Services;
using EgebisLeadFinder.Services.CompanyIntel;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EgebisLeadFinder.Tests;

/// <summary>
/// AI #3: GeminiAiService.RateCompanyAsync sema round-trip'i. GeminiRetryTests ile ayni desen.
/// </summary>
public class GeminiCompanyRatingServiceTests
{
    private const string RatingBody = """
        {"candidates":[{"content":{"parts":[{"text":"{\"signal\":\"riskli\",\"summary\":\"Konkordato başvurusu var.\",\"financialSource\":\"yok\",\"riskSignals\":[{\"text\":\"Konkordato\",\"severity\":\"yuksek\",\"sourceUrl\":\"https://haber/1\"}],\"growthSignals\":[],\"customers\":[]}"}]}}]}
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
            RetryBaseDelayMs = 1,
            MaxInputChars = 8000
        });

        return new GeminiAiService(http, options, FakeSettingsService.WithGeminiKeys("ana-anahtar"),
            new FakeApiUsageTracker(), NullLogger<GeminiAiService>.Instance);
    }

    private static CompanyRatingInput Input() => new()
    {
        Company = new Company { Name = "Trakya Döküm" },
        Snippets = new List<IntelSnippet>
        {
            new() { Text = "Trakya Döküm konkordato başvurusu yaptı.", Kind = IntelKind.Risk, SourceUrl = "https://haber/1" }
        }
    };

    [Fact]
    public async Task Basarili_yanit_CompanyRating_olarak_cozulur()
    {
        var handler = new FakeHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(RatingBody) });

        var result = await CreateService(handler).RateCompanyAsync(Input());

        Assert.True(result.Success);
        Assert.Equal("riskli", result.Rating!.Signal);
        Assert.Single(result.Rating.RiskSignals);
        Assert.Equal("yuksek", result.Rating.RiskSignals[0].Severity);
    }

    [Fact]
    public async Task Kaynak_yoksa_AI_cagrilmadan_hata_doner()
    {
        var handler = new FakeHandler(_ => throw new InvalidOperationException("çağrılmamalıydı"));

        var result = await CreateService(handler).RateCompanyAsync(new CompanyRatingInput
        {
            Company = new Company { Name = "X" },
            Snippets = new List<IntelSnippet>()
        });

        Assert.False(result.Success);
    }

    [Fact]
    public async Task Anahtar_yoksa_MissingApiKey_mesaji_doner()
    {
        var http = new HttpClient(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));
        var service = new GeminiAiService(http,
            Options.Create(new AiOptions { GeminiModel = "m", GeminiEndpoint = "https://x", MaxInputChars = 8000 }),
            new FakeSettingsService(),
            new FakeApiUsageTracker(),
            NullLogger<GeminiAiService>.Instance);

        var result = await service.RateCompanyAsync(Input());

        Assert.False(result.Success);
        Assert.Contains("API anahtar", result.Error);
    }

    private class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(_responder(request));
    }
}
