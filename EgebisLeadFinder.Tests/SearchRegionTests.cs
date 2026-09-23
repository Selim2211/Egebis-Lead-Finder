using EgebisLeadFinder.Data;
using EgebisLeadFinder.Models;
using EgebisLeadFinder.Services;

namespace EgebisLeadFinder.Tests;

/// <summary>
/// Arama bolgesi: sorgular bolgenin dilinde kurulmali, bilinmeyen bolge
/// Türkiye'ye dusmeli ve eski kayitlar (yalnizca ulke adi) taninmali.
/// </summary>
public class SearchRegionTests
{
    [Fact]
    public void Bilinmeyen_bolge_turkiyeye_duser()
    {
        Assert.Equal("TR", SearchRegions.Get(null).Key);
        Assert.Equal("TR", SearchRegions.Get("XX").Key);
        Assert.False(SearchRegions.IsKnown("XX"));
        Assert.True(SearchRegions.IsKnown("de"));
    }

    [Fact]
    public void Eski_kayitlardaki_ulke_adindan_bolge_bulunur()
    {
        Assert.Equal("TR", SearchRegions.ByCountry("Türkiye")?.Key);
        Assert.Equal("DE", SearchRegions.ByCountry("Deutschland")?.Key);
        Assert.Equal("DE", SearchRegions.ByCountry("Almanya")?.Key);
        Assert.Null(SearchRegions.ByCountry("Atlantis"));
    }

    [Fact]
    public void Turkiye_disindaki_bolgelerde_il_secimi_yok()
    {
        Assert.True(SearchRegions.Get("TR").HasProvinces);
        Assert.False(SearchRegions.Get("DE").HasProvinces);
    }

    [Fact]
    public void Sorgular_bolgenin_dilinde_kurulur()
    {
        var tr = SearchQueryBuilder.Build(new SearchCriteria
        {
            Industry = "makina", RegionKey = "TR", Country = "Türkiye", City = "İzmir"
        });

        var de = SearchQueryBuilder.Build(new SearchCriteria
        {
            Industry = "Maschinenbau", RegionKey = "DE", Country = "Deutschland", City = "Bayern"
        });

        var uk = SearchQueryBuilder.Build(new SearchCriteria
        {
            Industry = "machinery", RegionKey = "GB", Country = "United Kingdom"
        });

        Assert.Contains(tr, q => q.Contains("üreticileri"));
        Assert.Contains(tr, q => q.Contains("İzmir Türkiye"));
        Assert.Contains(de, q => q.Contains("Hersteller"));
        Assert.Contains(de, q => q.Contains("Bayern Deutschland"));
        Assert.DoesNotContain(de, q => q.Contains("üreticileri"));
        Assert.Contains(uk, q => q.Contains("manufacturers"));
        Assert.Contains(uk, q => q.Contains("United Kingdom"));
    }

    [Fact]
    public void Bolge_belirtilmezse_ulke_adindan_dil_secilir()
    {
        var queries = SearchQueryBuilder.Build(new SearchCriteria
        {
            Industry = "Maschinenbau", Country = "Deutschland"
        });

        Assert.Contains(queries, q => q.Contains("Hersteller"));
    }

    [Fact]
    public void Haritalar_sorgusunda_sehir_tek_basina_kullanilir()
    {
        var places = SearchQueryBuilder.BuildPlaceQueries(new SearchCriteria
        {
            Industry = "plastik", RegionKey = "TR", Country = "Türkiye", City = "Bursa"
        });

        Assert.Contains(places, q => q == "plastik fabrikası Bursa");
        Assert.DoesNotContain(places, q => q.Contains("Bursa Türkiye"));
    }
}
