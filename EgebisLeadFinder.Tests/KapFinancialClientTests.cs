using System.Net;
using EgebisLeadFinder.Configuration;
using EgebisLeadFinder.Services.CompanyIntel;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EgebisLeadFinder.Tests;

/// <summary>
/// KAP finansal istemcisi: iki kademeli — (1) son finansal rapor başlığı+linki,
/// (2) rapor içinden sayısal kalemler (best-effort). Testler ayrıştırma mantığını
/// doğrular; canlı API'yi değil.
/// </summary>
public class KapFinancialClientTests
{
    private const string DisclosureList = """
        [
          { "disclosureIndex": "900", "kapTitle": "2023/12 Konsolide Finansal Rapor", "publishDate": "2024-03-01" },
          { "disclosureIndex": "1200", "kapTitle": "2024/12 Konsolide Finansal Rapor", "publishDate": "2025-03-05" }
        ]
        """;

    private const string FinancialReport = """
        { "items": [
          { "label": "Hasılat", "currentPeriod": 2100000000 },
          { "label": "Dönem Kârı (Zararı)", "value": 180000000 },
          { "label": "Toplam Özkaynaklar", "value": "1.400.000.000" },
          { "label": "Toplam Varlıklar", "value": 3500000000 }
        ]}
        """;

    private static KapFinancialClient Create(Func<HttpRequestMessage, HttpResponseMessage> responder) =>
        new(new HttpClient(new FakeHandler(responder)),
            Options.Create(new ResearchOptions()),
            NullLogger<KapFinancialClient>.Instance);

    [Fact]
    public async Task Son_rapordan_temel_kalemler_cikarilir()
    {
        var client = Create(req => req.Method == HttpMethod.Post
            ? Ok(DisclosureList)
            : Ok(FinancialReport));

        var fin = await client.GetLatestAsync("oid-1");

        Assert.NotNull(fin);
        Assert.Equal("2024/12", fin!.Period);   // en yeni bildirim
        Assert.True(fin.HasFigures);
        Assert.Equal(2100000000m, fin.Revenue);
        Assert.Equal(180000000m, fin.NetProfit);
        Assert.Equal(1400000000m, fin.Equity);   // "1.400.000.000" string parse
        Assert.Contains("milyar TL", fin.ToSummary());
    }

    [Fact]
    public async Task Rapor_ayristirilmazsa_baslik_ve_link_yine_doner()
    {
        var client = Create(req => req.Method == HttpMethod.Post
            ? Ok(DisclosureList)
            : Ok("<html>bir şey</html>"));

        var fin = await client.GetLatestAsync("oid-1");

        Assert.NotNull(fin);
        Assert.False(fin!.HasFigures);
        Assert.Contains("1200", fin.DisclosureUrl);
        Assert.Contains("Finansal Rapor", fin.ToSummary());
    }

    [Fact]
    public async Task Finansal_bildirim_yoksa_null_doner()
    {
        var client = Create(_ => Ok("[]"));

        Assert.Null(await client.GetLatestAsync("oid-1"));
    }

    [Fact]
    public async Task Bos_oid_null_doner()
    {
        var client = Create(_ => throw new InvalidOperationException("çağrılmamalı"));

        Assert.Null(await client.GetLatestAsync(""));
    }

    private static HttpResponseMessage Ok(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body) };

    private class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(_responder(request));
    }
}
