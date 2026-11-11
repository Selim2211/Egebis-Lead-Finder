using EgebisLeadFinder.Models;
using EgebisLeadFinder.Services;
using Xunit;

namespace EgebisLeadFinder.Tests;

public class CreditsReportTests
{
    private static ApiUsageDaily Row(string provider, DateOnly date, int count) =>
        new() { Provider = provider, Date = date, Count = count };

    [Fact]
    public void Week_starts_on_monday_and_covers_seven_days()
    {
        // 12 Kasim 2026 Persembe
        var p = CreditsPeriod.Resolve(CreditsPeriod.Week, new DateOnly(2026, 11, 12));

        Assert.Equal(new DateOnly(2026, 11, 9), p.From);
        Assert.Equal(DayOfWeek.Monday, p.From.DayOfWeek);
        Assert.Equal(new DateOnly(2026, 11, 15), p.To);
        Assert.Equal(7, p.Days().Count());
        Assert.Equal(new DateOnly(2026, 11, 2), p.Prev);
        Assert.Equal(new DateOnly(2026, 11, 16), p.Next);
    }

    [Fact]
    public void Sunday_belongs_to_the_week_that_ends_on_it()
    {
        var p = CreditsPeriod.Resolve(CreditsPeriod.Week, new DateOnly(2026, 11, 15)); // Pazar

        Assert.Equal(new DateOnly(2026, 11, 9), p.From);
        Assert.Equal(new DateOnly(2026, 11, 15), p.To);
    }

    [Theory]
    [InlineData(2026, 2, 1, 28)]   // subat
    [InlineData(2028, 2, 15, 29)]  // artik yil
    [InlineData(2026, 11, 30, 30)]
    [InlineData(2026, 12, 31, 31)]
    public void Month_covers_the_whole_month(int year, int month, int day, int length)
    {
        var p = CreditsPeriod.Resolve(CreditsPeriod.Month, new DateOnly(year, month, day));

        Assert.Equal(1, p.From.Day);
        Assert.Equal(length, p.Days().Count());
        Assert.Equal(month, p.To.Month);
    }

    [Fact]
    public void Month_navigation_crosses_year_boundaries()
    {
        var p = CreditsPeriod.Resolve(CreditsPeriod.Month, new DateOnly(2026, 1, 20));

        Assert.Equal(new DateOnly(2025, 12, 1), p.Prev);
        Assert.Equal(new DateOnly(2026, 2, 1), p.Next);
    }

    [Fact]
    public void Day_period_is_a_single_day_and_unknown_kind_falls_back_to_week()
    {
        var day = CreditsPeriod.Resolve(CreditsPeriod.Day, new DateOnly(2026, 11, 10));
        Assert.Equal(day.From, day.To);
        Assert.Single(day.Days());

        Assert.Equal(CreditsPeriod.Week, CreditsPeriod.Resolve("yok", new DateOnly(2026, 11, 10)).Kind);
        Assert.Equal(CreditsPeriod.Week, CreditsPeriod.Resolve(null, new DateOnly(2026, 11, 10)).Kind);
    }

    [Fact]
    public void Utc_range_uses_utc_kind_and_turkey_offset()
    {
        // Npgsql yalnizca Kind=Utc kabul eder; Turkiye gunu UTC+3'tur.
        var (from, to) = CreditsPeriod.Resolve(CreditsPeriod.Week, new DateOnly(2026, 11, 12)).UtcRange();

        Assert.Equal(DateTimeKind.Utc, from.Kind);
        Assert.Equal(DateTimeKind.Utc, to.Kind);
        Assert.Equal(new DateTime(2026, 11, 8, 21, 0, 0, DateTimeKind.Utc), from);   // Pzt 9 Kasim 00:00 (UTC+3)
        Assert.Equal(new DateTime(2026, 11, 15, 21, 0, 0, DateTimeKind.Utc), to);    // Pzt 16 Kasim 00:00 (UTC+3), haric
    }

    [Fact]
    public void Report_sums_each_provider_per_day_and_in_total()
    {
        var period = CreditsPeriod.Resolve(CreditsPeriod.Week, new DateOnly(2026, 11, 9));
        var d1 = new DateOnly(2026, 11, 9);
        var d2 = new DateOnly(2026, 11, 11);
        var rows = new[]
        {
            Row("Serper", d1, 30), Row("Serper", d2, 12),
            Row("ApolloCredit", d2, 3), Row("Apollo", d2, 40),       // kisi aramasi kredi sayilmaz
            Row("Gemini", d1, 5), Row("Groq", d1, 80), Row("Nvidia", d2, 7),
            Row("Serper", new DateOnly(2026, 11, 20), 999)            // donem disi
        };

        var report = CreditsReport.Build(period, rows, (_, calls) => calls * 0.5m);

        Assert.Equal(7, report.Days.Count);
        Assert.Equal(42, report.Serper);
        Assert.Equal(3, report.Apollo);
        Assert.Equal(5, report.Gemini);
        Assert.Equal(2.5m, report.GeminiTry);
        Assert.Equal(87, report.OtherAi);
        Assert.Equal(30, report.Days[0].Serper);
        Assert.Equal(12, report.Days[2].Serper);
        Assert.True(report.Days[1].IsEmpty);
    }

    [Fact]
    public void Gemini_cost_is_not_asked_for_days_without_calls()
    {
        var period = CreditsPeriod.Resolve(CreditsPeriod.Day, new DateOnly(2026, 11, 9));
        var asked = 0;

        var report = CreditsReport.Build(period, new[] { Row("Serper", new DateOnly(2026, 11, 9), 4) }, (_, _) => { asked++; return 1m; });

        Assert.Equal(0, asked);
        Assert.Equal(0m, report.GeminiTry);
    }

    [Fact]
    public void Usage_scope_counts_only_inside_and_separates_concurrent_scopes()
    {
        UsageScope.Record("Serper"); // kapsam yok: sessizce yok sayilir

        using (var outer = UsageScope.Begin())
        {
            UsageScope.Record("Serper");
            UsageScope.Record("Serper");
            UsageScope.Record("Groq");
            UsageScope.Record("Apollo");

            Assert.Equal(2, outer.SerperCredits);
            Assert.Equal(1, outer.AiCalls); // Apollo ve Serper yapay zeka degildir
        }

        Assert.Null(UsageScope.Current);
    }

    [Fact]
    public async Task Parallel_work_inside_one_scope_is_counted_and_other_scopes_stay_separate()
    {
        using var a = UsageScope.Begin();
        await Task.WhenAll(Enumerable.Range(0, 20).Select(async _ =>
        {
            await Task.Yield();
            UsageScope.Record("Serper");
            UsageScope.Record("Gemini");
        }));

        UsageScope? b = null;
        await Task.Run(() =>
        {
            using var inner = UsageScope.Begin(); // ayri akista baska bir arama
            UsageScope.Record("Serper");
            b = inner;
        });

        Assert.Equal(21, a.SerperCredits); // ic ice kapsam: dis aramaya da yazilir
        Assert.Equal(20, a.AiCalls);
        Assert.Equal(1, b!.SerperCredits);
    }
}
