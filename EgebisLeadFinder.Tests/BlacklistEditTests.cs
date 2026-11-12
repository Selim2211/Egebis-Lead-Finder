using EgebisLeadFinder.Services;
using Xunit;

namespace EgebisLeadFinder.Tests;

public class BlacklistEditTests
{
    [Fact]
    public void Ekleme_adresi_alan_adina_cevirir_ve_tekrar_etmez()
    {
        var text = SearchBlacklist.Add("ornek.com", "https://www.Yeni-Firma.com/iletisim");
        text = SearchBlacklist.Add(text, "YENI-FIRMA.com");

        Assert.Equal(new[] { "ornek.com", "yeni-firma.com" }, SearchBlacklist.Entries(text));
    }

    [Fact]
    public void Firma_adi_oldugu_gibi_eklenir_ve_bos_deger_yok_sayilir()
    {
        var text = SearchBlacklist.Add(null, "Örnek Makina");
        text = SearchBlacklist.Add(text, "   ");

        Assert.Equal(new[] { "Örnek Makina" }, SearchBlacklist.Entries(text));
    }

    [Fact]
    public void Cikarma_buyuk_kucuk_harf_gozetmez()
    {
        var text = SearchBlacklist.Remove("ornek.com\nÖrnek Makina\nbaska.com", "ÖRNEK makina");

        Assert.Equal(new[] { "ornek.com", "baska.com" }, SearchBlacklist.Entries(text));
    }

    [Fact]
    public void Eklenen_alan_adi_siteyi_engeller()
    {
        var blacklist = SearchBlacklist.Parse(SearchBlacklist.Add(null, "https://www.yeni-firma.com/"));

        Assert.True(blacklist.MatchesDomain("shop.yeni-firma.com"));
    }
}
