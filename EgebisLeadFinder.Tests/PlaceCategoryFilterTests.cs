using EgebisLeadFinder.Models;
using EgebisLeadFinder.Services;

namespace EgebisLeadFinder.Tests;

public class PlaceCategoryFilterTests
{
    [Theory]
    // Google Haritalar'da gercekten gorulen uretici kategorileri.
    [InlineData("Otomobil Parçası Üreticisi")]
    [InlineData("Araba Fabrikası")]
    [InlineData("Makine Üreticisi")]
    [InlineData("Metal İşleme")]
    [InlineData("Plastik Üretici")]
    public void Uretici_kategorileri_gecer(string category)
    {
        Assert.True(PlaceCategoryFilter.IsRelevant(category));
        Assert.True(PlaceCategoryFilter.IsManufacturerCategory(category));
    }

    [Theory]
    // Ayni bolgede cikan ama hedef disi kucuk isletmeler.
    [InlineData("Oto Tamircisi")]
    [InlineData("Araba Galerisi")]
    [InlineData("Lastikçi")]
    [InlineData("Oto Yıkama")]
    [InlineData("Araç Kiralama")]
    [InlineData("Akaryakıt İstasyonu")]
    [InlineData("Restoran")]
    public void Hedef_disi_kategoriler_elenir(string category)
    {
        Assert.False(PlaceCategoryFilter.IsRelevant(category));
    }

    [Fact]
    public void Kategori_bossa_elenmez()
    {
        // Haritalar her kayda kategori yazmiyor; gercek bir fabrikayi
        // kategori eksikligi yuzunden kaybetmek istemiyoruz.
        Assert.True(PlaceCategoryFilter.IsRelevant(null));
        Assert.True(PlaceCategoryFilter.IsRelevant(""));
    }

    [Fact]
    public void Uretici_isareti_eleme_listesini_gecersiz_kilar()
    {
        // "Yedek Parça Satış" eleme listesinde ama "Üretici" ifadesi kazanmali.
        Assert.True(PlaceCategoryFilter.IsRelevant("Otomotiv Yedek Parça Üreticisi"));
    }

    [Fact]
    public void Turkce_karakter_farki_elemeyi_engellemez()
    {
        Assert.False(PlaceCategoryFilter.IsRelevant("OTO TAMİRCİSİ"));
        Assert.False(PlaceCategoryFilter.IsRelevant("oto tamircisi"));
    }
}

public class PlaceQueryBuilderTests
{
    [Fact]
    public void Haritalar_sorgulari_tirnaksiz_ve_konumlu_uretilir()
    {
        var queries = SearchQueryBuilder.BuildPlaceQueries(new SearchCriteria
        {
            Industry = "Otomotiv",
            City = "Bursa",
            Country = "Türkiye"
        });

        Assert.NotEmpty(queries);
        // Haritalar bir isletme dizini; tirnakli tam ifade aramasi burada ise yaramaz.
        Assert.All(queries, q => Assert.DoesNotContain("\"", q));
        Assert.All(queries, q => Assert.Contains("Bursa", q));
    }

    [Fact]
    public void Coklu_sehir_ayri_sorgulara_bolunur()
    {
        var queries = SearchQueryBuilder.BuildPlaceQueries(new SearchCriteria
        {
            Industry = "Makina",
            City = "Bursa, Kocaeli"
        });

        Assert.Contains(queries, q => q.Contains("Bursa"));
        Assert.Contains(queries, q => q.Contains("Kocaeli"));
    }

    [Fact]
    public void Sektor_bossa_sorgu_uretilmez()
    {
        Assert.Empty(SearchQueryBuilder.BuildPlaceQueries(new SearchCriteria { Industry = "" }));
    }

    [Fact]
    public void Sehir_yoksa_ulke_konum_olarak_kullanilir()
    {
        var queries = SearchQueryBuilder.BuildPlaceQueries(new SearchCriteria
        {
            Industry = "Tekstil",
            City = null,
            Country = "Türkiye"
        });

        Assert.NotEmpty(queries);
        Assert.All(queries, q => Assert.Contains("Türkiye", q));
    }
}
