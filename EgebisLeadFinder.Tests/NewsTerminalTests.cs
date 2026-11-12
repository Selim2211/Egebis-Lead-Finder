using EgebisLeadFinder.Models;
using EgebisLeadFinder.Services.News;
using Xunit;

namespace EgebisLeadFinder.Tests;

public class NewsTerminalTests
{
    private static readonly DateTime Now = new(2026, 11, 12, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(NewsCategories.Investment, NewsCategories.Positive, "Pozitif")]
    [InlineData(NewsCategories.Risk, NewsCategories.Negative, "Negatif")]
    [InlineData(NewsCategories.Finance, NewsCategories.Neutral, "Nötr")]
    [InlineData(NewsCategories.General, NewsCategories.Neutral, "Nötr")]
    public void Duygu_etiketi_kategoriden_turetilir(string category, string sentiment, string label)
    {
        Assert.Equal(sentiment, NewsCategories.Sentiment(category));
        Assert.Equal(label, NewsCategories.SentimentLabel(category));
    }

    [Fact]
    public void Mini_grafik_son_7_gunu_eskiden_yeniye_sayar_bos_gunler_sifir()
    {
        var today = NewsSlots.TodayStartUtc(Now);
        var spark = NewsSpark.Build(new[]
        {
            today.AddHours(1), today.AddHours(2),      // bugün: 2
            today.AddHours(-1),                         // dün: 1
            today.AddDays(-3).AddHours(-2),             // 4 gün önce
            today.AddDays(-30)                          // pencere dışı
        }, Now);

        Assert.Equal(7, spark.Length);
        Assert.Equal(2, spark[6]);
        Assert.Equal(1, spark[5]);
        Assert.Equal(1, spark[2]);
        Assert.Equal(4, spark.Sum());
        Assert.All(new[] { 0, 1, 3, 4 }, i => Assert.Equal(0, spark[i]));
    }

    [Fact]
    public void Konu_adi_firma_radar_firma_sehir_sirasiyla_secilir()
    {
        var item = new NewsFeedItem { Title = "x", Url = "u", City = "Manisa", MentionedCompany = "Arçelik" };
        Assert.Equal("Vestel", NewsFormat.Subject(new NewsRow(item, "Vestel")));
        Assert.Equal("Arçelik", NewsFormat.Subject(new NewsRow(item, null)));
        item.MentionedCompany = null;
        Assert.Equal("Manisa", NewsFormat.Subject(new NewsRow(item, null)));
        item.City = null;
        Assert.Equal("Radar", NewsFormat.Subject(new NewsRow(item, null)));
    }

    [Fact]
    public void Serit_baslik_zaten_konuyla_basliyorsa_tekrar_yazilmaz()
    {
        Assert.True(NewsFormat.TitleStartsWithSubject("Tesla", "Tesla Türkiye'ye fabrika kuruyor"));
        Assert.False(NewsFormat.TitleStartsWithSubject("Vestel", "Manisa'da yeni fabrika"));
    }

    [Theory]
    [InlineData(NewsCategories.Investment, 4, 4)]
    [InlineData(NewsCategories.Risk, 3, -3)]
    [InlineData(NewsCategories.Finance, 5, 0)]
    public void Haber_etkisi_kategori_ve_onemden_gelir(string category, int importance, int expected) =>
        Assert.Equal(expected, NewsImpact.Of(category, importance));

    [Fact]
    public void Itibar_serisi_bugunku_puanda_biter_geriye_dogru_etkiler_cikarilir()
    {
        var series = NewsImpact.Series(80, new[] { 0, 0, 0, 0, 5, -2, 3 });
        Assert.Equal(new[] { 74, 74, 74, 74, 79, 77, 80 }, series);
    }

    [Fact]
    public void Hareketler_firmaya_ve_radar_adina_gore_gruplanir_haftalik_etkiye_gore_siralanir()
    {
        var today = NewsSlots.TodayStartUtc(Now);
        MoverNews N(int? id, string name, string cat, int imp, DateTime at) =>
            new(id, name, null, id is null ? null : 60, 0, "İzmir", at, cat, imp, "t-" + name, "u", null);
        var movers = NewsImpact.Movers(new[]
        {
            N(1, "Vestel", NewsCategories.Investment, 4, today.AddHours(1)),
            N(1, "Vestel", NewsCategories.Risk, 2, today.AddDays(-2)),
            N(null, "Arçelik", NewsCategories.Investment, 5, today.AddDays(-1)),
            N(null, "ARÇELİK ", NewsCategories.General, 2, today.AddHours(2)),
            N(2, "Simfer", NewsCategories.Risk, 3, today.AddHours(3))
        }, Now);

        Assert.Equal(new int?[] { null, 1, 2 }, movers.Select(m => m.CompanyId).ToArray());
        var arcelik = movers.Single(m => m.CompanyId is null);
        Assert.Equal(5, arcelik.WeekImpact);
        Assert.Equal(2, arcelik.Count7);
        Assert.Equal("Radar · İzmir", arcelik.Industry);
        var vestel = movers.Single(m => m.CompanyId == 1);
        Assert.Equal(4, vestel.DayImpact);
        Assert.Equal(2, vestel.WeekImpact);
        Assert.NotNull(vestel.Positive);
        Assert.NotNull(vestel.Risk);
        Assert.Equal(-3, movers.Single(m => m.CompanyId == 2).WeekImpact);
    }

    [Fact]
    public void Rozet_harfleri_ve_gecen_sure()
    {
        Assert.Equal("VE", NewsFormat.Initials("Vestel Elektronik"));
        Assert.Equal("Sİ", NewsFormat.Initials("Simfer"));
        Assert.Equal("•", NewsFormat.Initials(null));
        Assert.Equal("az önce", NewsFormat.Ago(Now.AddSeconds(-20), Now));
        Assert.Equal("5 dk önce", NewsFormat.Ago(Now.AddMinutes(-5), Now));
        Assert.Equal("3 sa önce", NewsFormat.Ago(Now.AddHours(-3), Now));
    }
}
