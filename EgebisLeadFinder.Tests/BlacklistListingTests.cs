using EgebisLeadFinder.Services;
using Xunit;

namespace EgebisLeadFinder.Tests;

public class BlacklistListingTests
{
    private static readonly List<string> Entries =
        new() { "bir.com", "Çelik Makina", "ikinci.com.tr", "Zeta Plastik", "uc.com" };

    [Fact]
    public void En_yeni_siralamasi_son_eklenen_basta()
    {
        var (items, total, _) = BlacklistListing.Query(Entries, null, null, null, 1);

        Assert.Equal(5, total);
        Assert.Equal("uc.com", items[0]);
        Assert.Equal("bir.com", items[^1]);
    }

    [Fact]
    public void Ada_gore_siralama_turkce_harfleri_dikkate_alir_ve_arama_calisir()
    {
        var (az, _, _) = BlacklistListing.Query(Entries, null, null, BlacklistListing.AZ, 1);
        Assert.Equal("bir.com", az[0]);

        var (found, total, _) = BlacklistListing.Query(Entries, "celik", null, null, 1);
        Assert.Equal(1, total);
        Assert.Equal("Çelik Makina", found[0]);
    }

    [Fact]
    public void Tur_filtresi_alan_adi_ve_firma_adini_ayirir()
    {
        Assert.Equal(3, BlacklistListing.Query(Entries, null, BlacklistListing.TypeDomain, null, 1).Total);
        Assert.Equal(2, BlacklistListing.Query(Entries, null, BlacklistListing.TypeName, null, 1).Total);
    }

    [Fact]
    public void Sayfalama_ondalik_gruplar_ve_son_sayfaya_sabitlenir()
    {
        var many = Enumerable.Range(1, 23).Select(i => $"site{i:00}.com").ToList();

        var (p1, total, _) = BlacklistListing.Query(many, null, null, BlacklistListing.Oldest, 1);
        var (p3, _, page) = BlacklistListing.Query(many, null, null, BlacklistListing.Oldest, 99);

        Assert.Equal(23, total);
        Assert.Equal(10, p1.Count);
        Assert.Equal(3, page);
        Assert.Equal(3, p3.Count);
        Assert.Equal("site21.com", p3[0]);
    }
}
