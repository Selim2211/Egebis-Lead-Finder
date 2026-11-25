using EgebisLeadFinder.Services;
using Xunit;

namespace EgebisLeadFinder.Tests;

/// <summary>Ünvanlar tam gelir: kırpık / firma adı olan ünvan tanınır, en tam kaynak seçilir.</summary>
public class FullContactTitleTests
{
    [Theory]
    [InlineData("Akaryakıt ve Tüplü ...", true)]
    [InlineData("After Sales & Warehouse Management …", true)]
    [InlineData("Bilgi İşlem Müdürü", false)]
    [InlineData(null, false)]
    public void Kirpik_unvan_taninir(string? title, bool expected) => Assert.Equal(expected, ContactTitle.IsTruncated(title));

    [Theory]
    [InlineData("Hyundai Motor Türkiye Otomotiv A.Ş.", true)]
    [InlineData("BCS ÇELİK BORU SANAYİ A.Ş. ...", true)]
    [InlineData("OM Mekanik Tesisat İnşaat San. ve Tic. ...", true)]
    [InlineData("Müller GmbH", true)]
    [InlineData("CIO BEKO Europe", false)]
    [InlineData("Satın Alma Müdürü / Arçelik A.Ş.", false)]
    [InlineData("Homologation Assistant Manager at Hyundai Motor Türkiye Otomotiv A.Ş", false)]
    [InlineData("Genel Müdür Yardımcısı / SİMFER", false)]
    [InlineData("Endüstri Mühendisi", false)]
    public void Firma_adi_unvan_sanilmaz(string title, bool expected) => Assert.Equal(expected, ContactTitle.LooksLikeCompany(title));

    [Theory]
    [InlineData("Akaryakıt ve Tüplü ...", "Akaryakıt ve Tüplü Gaz Satış Müdürü", "Akaryakıt ve Tüplü Gaz Satış Müdürü")]
    [InlineData("Hyundai Motor Türkiye Otomotiv A.Ş.", "Homologation Assistant Manager", "Homologation Assistant Manager")]
    [InlineData(null, "IT Manager", "IT Manager")]
    [InlineData("Bilgi Teknolojileri Müdürü", "IT Manager", "Bilgi Teknolojileri Müdürü")]
    [InlineData("IT Müdürü", null, "IT Müdürü")]
    [InlineData("IT Müdürü", "Satış ...", "IT Müdürü")]
    public void En_tam_unvan_secilir(string? current, string? candidate, string expected) =>
        Assert.Equal(expected, ContactTitle.Best(current, candidate));

    [Fact]
    public void LinkedIn_basliginda_unvan_yerine_firma_varsa_unvan_bos_firma_dolu()
    {
        var p = LinkedInTitleParser.Parse("Zeki Onur Elmas - Hyundai Motor Türkiye Otomotiv A.Ş. | LinkedIn", "https://tr.linkedin.com/in/x");
        Assert.NotNull(p);
        Assert.Null(p!.Title);
        Assert.Equal("Hyundai Motor Türkiye Otomotiv A.Ş", p.Company);
    }

    [Fact]
    public void LinkedIn_normal_unvan_aynen_kalir()
    {
        var p = LinkedInTitleParser.Parse("Ayşe Kaya - Bilgi İşlem Müdürü - Arçelik A.Ş. | LinkedIn", null);
        Assert.Equal("Bilgi İşlem Müdürü", p!.Title);
        Assert.Equal("Arçelik A.Ş.", p.Company);
    }

    [Theory]
    [InlineData("Ahmet Yılmaz - Bilgi Teknolojileri Müdürü", "Ahmet Yılmaz", "Bilgi Teknolojileri Müdürü")]
    [InlineData("Gökhan Sığın Named New CEO of Vestel", "Gökhan Sığın", "CEO")]
    [InlineData("Zeynep Arslan, Genel Müdür Yardımcısı", "Zeynep Arslan", "Genel Müdür Yardımcısı")]
    public void Siteden_gelen_unvan_isimden_ve_haber_cumlesinden_ayiklanir(string line, string name, string title)
    {
        var person = Assert.Single(PersonExtractor.Extract(line));
        Assert.Equal(name, person.Name);
        Assert.Equal(title, person.Title);
    }

    [Fact]
    public void Apollo_eslesmesinde_unvan_okunur_title_yoksa_guncel_isten()
    {
        using var withTitle = System.Text.Json.JsonDocument.Parse("""{"title":"Chief Information Officer"}""");
        using var fromHistory = System.Text.Json.JsonDocument.Parse(
            """{"employment_history":[{"current":false,"title":"Eski"},{"current":true,"title":"IT Director"}]}""");

        Assert.Equal("IT Director", ApolloPersonEmailFinder.CurrentEmploymentTitle(fromHistory.RootElement));
        Assert.Null(ApolloPersonEmailFinder.CurrentEmploymentTitle(withTitle.RootElement));
    }
}
