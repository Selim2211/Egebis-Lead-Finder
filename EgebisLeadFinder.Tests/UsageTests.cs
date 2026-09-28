using EgebisLeadFinder.Services;

namespace EgebisLeadFinder.Tests;

/// <summary>Madde 5: API kullanim ekrani esikleri ve Apollo kredi yaniti.</summary>
public class UsageTests
{
    [Theory]
    [InlineData(0, 0, UsageLevel.Unknown)]
    [InlineData(100, 0, UsageLevel.Unknown)]
    [InlineData(10, 100, UsageLevel.Normal)]
    [InlineData(79, 100, UsageLevel.Normal)]
    [InlineData(80, 100, UsageLevel.Warning)]
    [InlineData(95, 100, UsageLevel.Critical)]
    [InlineData(100, 100, UsageLevel.Exhausted)]
    [InlineData(2600, 2500, UsageLevel.Exhausted)]
    public void Esikler_yuzde_80_ve_95(int used, int limit, UsageLevel expected) =>
        Assert.Equal(expected, UsageService.Evaluate(used, limit));

    [Fact]
    public void Son_24_saatte_kota_hatasi_varsa_doldu_sayilir()
    {
        var now = new DateTime(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal(UsageLevel.Exhausted, UsageService.Combine(UsageLevel.Unknown, now.AddHours(-2), now));
        Assert.Equal(UsageLevel.Normal, UsageService.Combine(UsageLevel.Normal, now.AddDays(-2), now));
        Assert.Equal(UsageLevel.Warning, UsageService.Combine(UsageLevel.Warning, null, now));
    }

    [Fact]
    public void Apollo_kredi_yaniti_cozulur()
    {
        const string body = """
            {"credit_usage_stats":{"lead_credit":{"limit":10000,"consumed":2500,"left_over":7500},
             "export_credit":{"limit":5000,"consumed":250,"left_over":4750}},
             "current_credit_cycle":{"start_date":"2026-09-01T00:00:00.000Z","end_date":"2026-10-01T00:00:00.000Z"}}
            """;

        var status = UsageService.ParseApolloCredits(body, DateTime.UtcNow);

        Assert.True(status.Ok);
        Assert.Equal(10000, status.Limit);
        Assert.Equal(2500, status.Consumed);
        Assert.Equal(7500, status.LeftOver);
        Assert.Equal(new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), status.CycleEnd);
    }

    [Fact]
    public void Beklenmeyen_apollo_yaniti_hata_doner()
    {
        Assert.False(UsageService.ParseApolloCredits("""{"error":"nope"}""", DateTime.UtcNow).Ok);
        Assert.False(UsageService.ParseApolloCredits("""{"credit_usage_stats":{}}""", DateTime.UtcNow).Ok);
    }
}
