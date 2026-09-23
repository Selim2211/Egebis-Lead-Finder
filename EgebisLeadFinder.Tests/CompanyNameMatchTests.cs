using EgebisLeadFinder.Services.CompanyIntel;

namespace EgebisLeadFinder.Tests;

public class CompanyNameMatchTests
{
    [Theory]
    [InlineData("Trakya Cam Sanayii A.Ş.", "TRAKYA CAM SANAYİİ A.Ş.")]
    [InlineData("Ford Otomotiv Sanayi A.Ş.", "Ford Otomotiv Sanayi Anonim Şirketi")]
    [InlineData("Arçelik A.Ş.", "ARCELIK AS")]
    [InlineData("Tofaş Türk Otomobil Fabrikası A.Ş.", "Tofaş Türk Otomobil Fabrikası")]
    public void Ayni_firma_farkli_yazimlar_eslesir(string a, string b)
    {
        Assert.True(CompanyNameMatch.IsMatch(a, b));
    }

    [Theory]
    [InlineData("Trakya Döküm A.Ş.", "Trakya Cam Sanayii A.Ş.")]
    [InlineData("Bursa Metal Ltd. Şti.", "İzmir Metal Sanayi A.Ş.")]
    [InlineData("Koç Holding", "Sabancı Holding")]
    public void Farkli_firmalar_eslesmez(string a, string b)
    {
        Assert.False(CompanyNameMatch.IsMatch(a, b));
    }

    [Fact]
    public void Cok_kisa_ad_eslesme_uretmez()
    {
        Assert.False(CompanyNameMatch.IsMatch("A.Ş.", "Herhangi A.Ş."));
    }

    [Fact]
    public void Kelime_butunu_iceren_ad_eslesir()
    {
        Assert.True(CompanyNameMatch.IsMatch("Beyçelik Gestamp Otomotiv Sanayi A.Ş.", "Beyçelik Gestamp"));
    }
}
