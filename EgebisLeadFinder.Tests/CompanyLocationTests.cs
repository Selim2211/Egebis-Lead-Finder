using EgebisLeadFinder.Models;

namespace EgebisLeadFinder.Tests;

/// <summary>Firma detayindaki harita karti: Google Haritalar sorgusu ve linkleri.</summary>
public class CompanyLocationTests
{
    [Fact]
    public void Acik_adres_varsa_ad_ve_adresle_tam_konum_aranir()
    {
        var location = CompanyLocation.For(new Company
        {
            Name = "Akme Döküm", Address = "Atatürk OSB 10001 Sk. No:5, Çiğli/İzmir, Türkiye", City = "İzmir", Country = "Türkiye"
        })!;

        Assert.True(location.IsExact);
        Assert.Equal("Atatürk OSB 10001 Sk. No:5, Çiğli/İzmir, Türkiye", location.Label);
        Assert.Equal("Akme Döküm, Atatürk OSB 10001 Sk. No:5, Çiğli/İzmir, Türkiye", location.Query); // ulke tekrar eklenmez
        Assert.StartsWith("https://www.google.com/maps/search/?api=1&query=Akme%20D%C3%B6k%C3%BCm%2C", location.OpenUrl);
        Assert.Contains("output=embed", location.EmbedUrl);
        Assert.Contains("z=16", location.EmbedUrl);
        Assert.StartsWith("https://www.google.com/maps/dir/?api=1&destination=", location.DirectionsUrl);
    }

    [Fact]
    public void Adreste_ulke_yoksa_eklenir()
    {
        var location = CompanyLocation.For(new Company { Name = "Werk GmbH", Address = "Industriestraße 4, 80331 München", Country = "Deutschland" })!;

        Assert.Equal("Werk GmbH, Industriestraße 4, 80331 München, Deutschland", location.Query);
        Assert.Equal("Industriestraße 4, 80331 München", location.Label);
    }

    [Fact]
    public void Adres_yoksa_ad_ve_sehirle_yaklasik_konum()
    {
        var location = CompanyLocation.For(new Company { Name = "Akme", City = "Bursa", Country = "Türkiye" })!;

        Assert.False(location.IsExact);
        Assert.Equal("Akme, Bursa, Türkiye", location.Query);
        Assert.Equal("Bursa, Türkiye", location.Label);
        Assert.Contains("z=13", location.EmbedUrl);
    }

    [Fact]
    public void Hic_konum_bilgisi_yoksa_harita_yok()
    {
        Assert.Null(CompanyLocation.For(new Company { Name = "Akme" }));
        Assert.Null(CompanyLocation.For(new Company { Name = "Akme", Address = "  ", City = " " }));
    }
}
