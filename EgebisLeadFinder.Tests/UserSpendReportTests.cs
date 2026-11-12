using EgebisLeadFinder.Models;
using EgebisLeadFinder.Services;
using Xunit;

namespace EgebisLeadFinder.Tests;

public class UserSpendReportTests
{
    private static SearchRun Run(int userId, string name, DateTime utc, int serper, int ai) => new()
    {
        UserId = userId, User = new AppUser { Id = userId, UserName = name },
        StartedAt = DateTime.SpecifyKind(utc, DateTimeKind.Utc), SerperCredits = serper, AiCalls = ai
    };

    [Fact]
    public void Kullanici_bazinda_toplar_ve_en_cok_harcayani_one_koyar()
    {
        var runs = new[]
        {
            Run(1, "ayse", new DateTime(2026, 11, 2, 10, 0, 0), 5, 10),
            Run(1, "ayse", new DateTime(2026, 11, 3, 10, 0, 0), 3, 2),
            Run(2, "mehmet", new DateTime(2026, 11, 3, 11, 0, 0), 20, 30)
        };

        var rows = UserSpendReport.ByUser(runs);

        Assert.Equal(new[] { "mehmet", "ayse" }, rows.Select(r => r.UserName));
        Assert.Equal(2, rows[1].Searches);
        Assert.Equal(8, rows[1].Serper);
        Assert.Equal(12, rows[1].AiCalls);
    }

    [Fact]
    public void Gunluk_toplam_turkiye_gunune_gore_ve_bos_gunler_sifirdir()
    {
        var period = CreditsPeriod.Resolve(CreditsPeriod.Week, new DateOnly(2026, 11, 4)); // 2-8 Kasim
        // 2 Kasim 22:30 UTC = 3 Kasim 01:30 Turkiye
        var runs = new[] { Run(1, "ayse", new DateTime(2026, 11, 2, 22, 30, 0), 4, 6) };

        var days = UserSpendReport.ByDay(runs, period);

        Assert.Equal(7, days.Count);
        Assert.Equal(0, days.First(d => d.Date == new DateOnly(2026, 11, 2)).Serper);
        Assert.Equal(4, days.First(d => d.Date == new DateOnly(2026, 11, 3)).Serper);
    }
}
