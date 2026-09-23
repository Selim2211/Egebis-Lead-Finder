using EgebisLeadFinder.Data;
using EgebisLeadFinder.Models;
using EgebisLeadFinder.Services;

namespace EgebisLeadFinder.Tests;

public class CompanyCityResolverTests
{
    private const string Marmara =
        "Balıkesir, Bilecik, Bursa, Çanakkale, Edirne, İstanbul, Kırklareli, Kocaeli, Sakarya, Tekirdağ, Yalova";

    [Fact]
    public void Birden_fazla_il_seciliyse_adresteki_il_yazilir()
    {
        var result = new SearchResult { Address = "Minareliçavuş OSB, 16140 Nilüfer/Bursa, Türkiye" };

        Assert.Equal("Bursa", CompanyCityResolver.Resolve(result, Marmara));
    }

    [Fact]
    public void Adres_secili_iller_disindaysa_gercek_il_yazilir()
    {
        var result = new SearchResult { Address = "Atatürk OSB, 35620 Çiğli/İzmir, Türkiye" };

        Assert.Equal("İzmir", CompanyCityResolver.Resolve(result, "Bursa, Kocaeli"));
    }

    [Fact]
    public void Adres_yoksa_ve_tek_il_seciliyse_o_il_yazilir()
    {
        Assert.Equal("Bursa", CompanyCityResolver.Resolve(new SearchResult { Title = "ABC Kalıp" }, "Bursa"));
    }

    [Fact]
    public void Il_bulunamazsa_ve_cok_il_seciliyse_bos_kalir()
    {
        // Eskiden tum secili iller birlestirilip yaziliyordu; 100 karakteri asip kaydi dusuruyordu.
        Assert.Null(CompanyCityResolver.Resolve(new SearchResult { Title = "ABC Kalıp" }, Marmara));
    }

    [Fact]
    public void Ozette_gecen_secili_il_kullanilir()
    {
        var result = new SearchResult { Snippet = "Kocaeli Gebze'de kurulu metal şekillendirme firması" };

        Assert.Equal("Kocaeli", CompanyCityResolver.Resolve(result, Marmara));
    }

    [Fact]
    public void Uzunluk_siniri_asan_alanlar_kirpilir()
    {
        var company = new Company { Name = "X", City = new string('a', 250), Industry = new string('b', 400) };

        StringLengthGuard.Apply(company);

        Assert.Equal(100, company.City!.Length);
        Assert.Equal(150, company.Industry!.Length);
        Assert.Equal("X", company.Name);
    }
}
