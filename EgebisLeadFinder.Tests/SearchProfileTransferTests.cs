using EgebisLeadFinder.Models;
using EgebisLeadFinder.Services;
using Xunit;

namespace EgebisLeadFinder.Tests;

public class SearchProfileTransferTests
{
    [Fact]
    public void Sablon_kendi_ornek_satirlarini_hatasiz_okur()
    {
        var bytes = SearchProfileTransfer.Build(Array.Empty<SearchProfile>(), template: true);

        var result = SearchProfileTransfer.Parse(new MemoryStream(bytes));

        Assert.Null(result.FileError);
        Assert.Equal(3, result.Rows.Count);
        Assert.All(result.Rows, r => Assert.Null(r.Error));
        Assert.Equal("US", result.Rows[2].RegionKey);
        Assert.True(result.Rows[2].IsPublic);
    }

    [Fact]
    public void Disa_aktarilan_profil_geri_ice_aktarilabilir()
    {
        var profile = new SearchProfile
        {
            Name = "Ege Otomotiv", Industry = "Otomotiv", RegionKey = "TR", City = "İzmir, Manisa", MaxCompanies = 20, IsPublic = true
        };

        var bytes = SearchProfileTransfer.Build(new[] { profile }, template: false);
        var row = Assert.Single(SearchProfileTransfer.Parse(new MemoryStream(bytes)).Rows);

        Assert.Null(row.Error);
        Assert.Equal("Ege Otomotiv", row.Name);
        Assert.Equal("TR", row.RegionKey);
        Assert.Equal("İzmir, Manisa", row.City);
        Assert.Equal(20, row.MaxCompanies);
        Assert.True(row.IsPublic);
    }

    [Theory]
    [InlineData("", "Otomotiv", "", "", "", "", "Profil adı boş")]
    [InlineData("A", "", "", "", "", "", "Sektör boş")]
    [InlineData("A", "Otomotiv", "99.99.99", "", "", "", "NACE kodu geçersiz")]
    [InlineData("A", "Otomotiv", "", "Atlantis", "", "", "Ülke / bölge tanınmadı")]
    [InlineData("A", "Otomotiv", "", "", "", "65", "En fazla firma")]
    [InlineData("A", "Otomotiv", "", "", "", "abc", "En fazla firma")]
    public void Gecersiz_satirlar_hata_verir(string name, string industry, string nace, string region, string city, string max, string expected)
    {
        var row = SearchProfileTransfer.ParseRow(2, name, industry, nace, region, city, max, "");

        Assert.NotNull(row.Error);
        Assert.Contains(expected, row.Error);
    }

    [Fact]
    public void Bozuk_dosya_anlasilir_hata_verir()
    {
        var result = SearchProfileTransfer.Parse(new MemoryStream(new byte[] { 1, 2, 3 }));

        Assert.NotNull(result.FileError);
        Assert.Empty(result.Rows);
    }

    [Fact]
    public void ABD_eyaletleri_bolge_listesinde_var()
    {
        Assert.True(EgebisLeadFinder.Data.RegionSubdivisions.Has("US"));
    }
}
