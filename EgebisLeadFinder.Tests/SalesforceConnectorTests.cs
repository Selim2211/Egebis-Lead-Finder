using System.Net;
using System.Text;
using EgebisLeadFinder.Configuration;
using EgebisLeadFinder.Models;
using EgebisLeadFinder.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EgebisLeadFinder.Tests;

/// <summary>
/// Refresh token rotasyonu: Salesforce her yenilemede yeni refresh token verip eskisini
/// iptal eder. Yeni token kaydedilmezse ya da iki istek ayni anda yenilerse baglanti duser.
/// </summary>
public class SalesforceConnectorTests
{
    [Fact]
    public async Task Yenilemede_donen_yeni_refresh_token_kaydedilir()
    {
        var settings = ConnectedSettings("rt-eski");
        var handler = new SalesforceHandler(rotateTo: "rt-yeni");

        var (success, error) = await CreateConnector(handler, settings, new MemoryCache(new MemoryCacheOptions()))
            .TestConnectionAsync();

        Assert.True(success, error);
        Assert.Equal("rt-yeni", await settings.GetAsync(SettingKeys.SalesforceRefreshToken));
        Assert.Equal("rt-eski", handler.RefreshTokensUsed.Single());
    }

    [Fact]
    public async Task Ayni_anda_gelen_istekler_tokeni_bir_kez_yeniler()
    {
        var settings = ConnectedSettings("rt-1");
        var handler = new SalesforceHandler(rotateTo: "rt-2", tokenDelayMs: 100);
        // DI'da IMemoryCache tekildir; her istek ise yeni connector ornegi alir.
        var cache = new MemoryCache(new MemoryCacheOptions());

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(i =>
            CreateConnector(handler, settings, cache).SyncLeadAsync(NewLead(i))));

        Assert.All(results, r => Assert.True(r.Success, r.Error));
        Assert.Single(handler.RefreshTokensUsed);
        Assert.Equal("rt-2", await settings.GetAsync(SettingKeys.SalesforceRefreshToken));
    }

    private static FakeSettingsService ConnectedSettings(string refreshToken) => new(new Dictionary<string, string?>
    {
        [SettingKeys.SalesforceConsumerKey] = "ck",
        [SettingKeys.SalesforceConsumerSecret] = "cs",
        [SettingKeys.SalesforceRefreshToken] = refreshToken,
        [SettingKeys.SalesforceInstanceUrl] = "https://ornek.my.salesforce.com"
    });

    private static SalesforceConnector CreateConnector(HttpMessageHandler handler, ISettingsService settings, IMemoryCache cache) =>
        new(new HttpClient(handler), Options.Create(new SalesforceOptions()), settings, cache,
            NullLogger<SalesforceConnector>.Instance);

    private static Lead NewLead(int id) => new()
    {
        Id = id,
        Company = new Company { Name = "Örnek A.Ş." },
        Contact = new Contact { Name = "Ali Veli", Title = "IT Müdürü" }
    };

    /// <summary>Token ucunda rotasyonlu cevap, diger uclarda basarili upsert dondurur.</summary>
    private class SalesforceHandler : HttpMessageHandler
    {
        private readonly string _rotateTo;
        private readonly int _tokenDelayMs;

        public SalesforceHandler(string rotateTo, int tokenDelayMs = 0)
        {
            _rotateTo = rotateTo;
            _tokenDelayMs = tokenDelayMs;
        }

        public List<string> RefreshTokensUsed { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/oauth2/token"))
            {
                var form = await request.Content!.ReadAsStringAsync(ct);
                var used = form.Split('&').Select(p => p.Split('='))
                    .First(p => p[0] == "refresh_token")[1];
                lock (RefreshTokensUsed) RefreshTokensUsed.Add(Uri.UnescapeDataString(used));

                await Task.Delay(_tokenDelayMs, ct);
                return Json(HttpStatusCode.OK,
                    $$"""{"access_token":"at","instance_url":"https://ornek.my.salesforce.com","refresh_token":"{{_rotateTo}}"}""");
            }

            return Json(HttpStatusCode.Created, """{"id":"00QXX0000000001","success":true}""");
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
            new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
