using EgebisLeadFinder.Data;

namespace EgebisLeadFinder.Tests;

public class TurkishProvincesTests
{
    [Fact]
    public void Seksen_bir_il_her_biri_tek_bir_bolgede()
    {
        var all = TurkishProvinces.Regions.Values.SelectMany(v => v).ToList();

        Assert.Equal(81, all.Count);
        Assert.Equal(81, all.Distinct().Count());
        Assert.Equal(81, TurkishProvinces.All.Count);
        Assert.Equal(7, TurkishProvinces.Regions.Count);
    }

    [Fact]
    public void Iller_turkce_alfabetik_sirali()
    {
        // "Çanakkale" C'den sonra, "İzmir" I'dan sonra gelmeli (ordinal siralamada sona duserdi).
        var list = TurkishProvinces.All.ToList();
        Assert.True(list.IndexOf("Çanakkale") > list.IndexOf("Bursa"));
        Assert.True(list.IndexOf("Çanakkale") < list.IndexOf("Denizli"));
        Assert.True(list.IndexOf("İzmir") < list.IndexOf("Kars"));
    }
}
