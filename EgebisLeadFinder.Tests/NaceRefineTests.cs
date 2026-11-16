using EgebisLeadFinder.Services;
using Xunit;

namespace EgebisLeadFinder.Tests;

public class NaceRefineTests
{
    [Fact]
    public void Dort_haneli_kod_urunlere_gore_alti_haneliye_iner()
    {
        var code = NaceCatalog.Refine("27.51", new[] { "Beyaz Eşya ve Ankastre Ürünleri İmalatı", "Ankastre fırın", "ocak", "davlumbaz", "bulaşık makinesi" });
        Assert.Equal("27.51.08", code);
    }

    [Fact]
    public void Ayirt_edici_uyum_yoksa_kod_oldugu_gibi_kalir()
    {
        Assert.Equal("27.51", NaceCatalog.Refine("27.51", new[] { "Beyaz Eşya" }));
        Assert.Equal("27.51", NaceCatalog.Refine("27.51", Array.Empty<string?>()));
    }

    [Fact]
    public void Alti_haneli_ve_bolum_kodlarina_dokunmaz()
    {
        Assert.Equal("27.51.08", NaceCatalog.Refine("27.51.08", new[] { "ocak" }));
        Assert.Equal("27", NaceCatalog.Refine("27", new[] { "ocak" }));
        Assert.Null(NaceCatalog.Refine(null, new[] { "ocak" }));
    }
}
