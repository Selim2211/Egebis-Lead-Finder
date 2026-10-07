using EgebisLeadFinder.Data;

namespace EgebisLeadFinder.Tests;

public class RegionSubdivisionTests
{
    [Fact]
    public void Bolge_listeleri_gecerli_ulke_anahtarlarina_aittir_ve_bos_degildir()
    {
        foreach (var (key, groups) in RegionSubdivisions.All)
        {
            Assert.True(SearchRegions.IsKnown(key), key);
            Assert.NotEmpty(groups);
            Assert.All(groups, g => Assert.NotEmpty(g.Items));
        }
    }

    [Fact]
    public void Bir_ulkede_ayni_bolge_iki_kez_gecmez_ve_adlar_virgul_icermez()
    {
        foreach (var (key, groups) in RegionSubdivisions.All)
        {
            var all = groups.SelectMany(g => g.Items).ToList();
            Assert.Equal(all.Count, all.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.All(all, name => Assert.DoesNotContain(',', name));
        }
    }

    [Fact]
    public void Almanyada_on_alti_eyalet_ve_Bayern_vardir_Turkiye_listesi_ayridir()
    {
        Assert.Equal(16, RegionSubdivisions.For("DE").Sum(g => g.Items.Length));
        Assert.Contains("Bayern", RegionSubdivisions.For("DE").SelectMany(g => g.Items));
        Assert.False(RegionSubdivisions.Has("TR"));
        Assert.False(RegionSubdivisions.Has("US"));
    }
}
