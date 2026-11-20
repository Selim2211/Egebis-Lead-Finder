using EgebisLeadFinder.Models;
using EgebisLeadFinder.Services.News;
using Xunit;

namespace EgebisLeadFinder.Tests;

/// <summary>Ek haber kaynakları: gazete RSS'leri, Resmî Gazete listesi, GDELT ve akış eşleştirme.</summary>
public class NewsExtraSourcesTests
{
    [Fact]
    public void Gazete_rss_basligi_bolunmez_kaynak_akisin_adidir()
    {
        var xml = """
            <rss><channel><title>Dünya</title>
            <item><title><![CDATA[Vestel - Manisa'ya yeni fabrika]]></title><link>https://www.dunya.com/a</link>
            <pubDate>Thu, 19 Nov 2026 21:33:00 +0300</pubDate><description><![CDATA[<p>Yatırım <b>500 milyon</b></p>]]></description></item>
            </channel></rss>
            """;
        var hit = Assert.Single(NewsFeedReader.ParseRss(xml, "Dünya"));
        Assert.Equal("Vestel - Manisa'ya yeni fabrika", hit.Title);
        Assert.Equal("Dünya", hit.Source);
        Assert.Equal("Yatırım 500 milyon", hit.Snippet);
        Assert.Equal(new DateTime(2026, 11, 19, 18, 33, 0, DateTimeKind.Utc), hit.PublishedUtc);
    }

    [Fact]
    public void Resmi_gazete_gunun_baglantilarini_okur_pdf_ve_tekrari_atlar()
    {
        var html = """
            <a href="https://www.resmigazete.gov.tr/eskiler/2026/11/20261119.pdf" class="btn">PDF Görüntüle</a>
            <a href="https://www.resmigazete.gov.tr/eskiler/2026/11/20261119-1.htm" data-modal="True">–– Yatırımlarda Devlet Yardımları Hakkında Kararda Değişiklik</a>
            <a href="https://www.resmigazete.gov.tr/eskiler/2026/11/20261119-1.htm">–– Yatırımlarda Devlet Yardımları Hakkında Kararda Değişiklik</a>
            <a href="https://www.resmigazete.gov.tr/eskiler/2026/11/20261119-2.htm">–– Kısa</a>
            """;
        var hit = Assert.Single(NewsFeedReader.ParseResmiGazete(html, DateTime.UtcNow));
        Assert.Equal("Yatırımlarda Devlet Yardımları Hakkında Kararda Değişiklik", hit.Title);
        Assert.Equal("Resmî Gazete", hit.Source);
        Assert.Equal(new DateTime(2026, 11, 18, 21, 0, 0, DateTimeKind.Utc), hit.PublishedUtc);
    }

    [Fact]
    public void Gdelt_yaniti_okunur_bos_ya_da_duz_metin_bos_liste_doner()
    {
        var json = """{"articles":[{"url":"https://x.com/a","title":"Arçelik yeni tesis","seendate":"20261119T120000Z","domain":"x.com"},{"url":"","title":"bos"}]}""";
        var hit = Assert.Single(GdeltNewsSource.Parse(json));
        Assert.Equal("Arçelik yeni tesis", hit.Title);
        Assert.Equal(new DateTime(2026, 11, 19, 12, 0, 0, DateTimeKind.Utc), hit.PublishedUtc);
        Assert.Empty(GdeltNewsSource.Parse(""));
        Assert.Empty(GdeltNewsSource.Parse("Please limit requests to one every 5 seconds"));
    }

    [Fact]
    public void Akistaki_haber_takip_edilen_firmaya_ya_da_radar_konusuna_baglanir_digerleri_alinmaz()
    {
        var companies = new List<Company> { new() { Id = 1, Name = "Vestel Elektronik", Domain = "vestel.com.tr" } };
        var topics = new List<RadarTopic> { new() { Id = 5, Query = "yeni fabrika", City = "Manisa" } };

        var byCompany = NewsFeedMatcher.Match(new NewsHit("Vestel Elektronik ihracat rekoru kırdı", "https://a.com/1", "AA", null, null), companies, topics);
        Assert.Equal(1, byCompany.Company?.Id);

        var byTopic = NewsFeedMatcher.Match(new NewsHit("Manisa'da yeni fabrikalar açılıyor", "https://a.com/2", "AA", null, null), companies, topics);
        Assert.Null(byTopic.Company);
        Assert.Equal(5, byTopic.Topic?.Id);

        var none = NewsFeedMatcher.Match(new NewsHit("Altın fiyatları yükseldi", "https://a.com/3", "AA", null, null), companies, topics);
        Assert.Null(none.Company);
        Assert.Null(none.Topic);

        var otherCity = NewsFeedMatcher.Match(new NewsHit("Bursa'da yeni fabrika", "https://a.com/4", "AA", null, null), companies, topics);
        Assert.Null(otherCity.Topic);
    }
}

public class NewsRegionTests
{
    [Theory]
    [InlineData("https://www.dunya.com/a", "Başlık", "tr")]
    [InlineData("https://www.reuters.com/a", "Tesla expands Berlin plant", "global")]
    [InlineData("https://www.reuters.com/a", "Arçelik yeni fabrika", "tr")]
    [InlineData("https://www.aa.com.tr/x", "Plain title", "tr")]
    public void Bolge_adres_ve_basliktan_tahmin_edilir(string url, string title, string expected) =>
        Assert.Equal(expected, EgebisLeadFinder.Models.NewsRegions.Classify(url, title));

    [Fact]
    public void Gdelt_ulke_ve_dil_bilgisinden_bolge_verir()
    {
        var json = """{"articles":[{"url":"https://a.de/1","title":"Vestel expands","seendate":"20261119T120000Z","sourcecountry":"Germany","language":"German"},{"url":"https://b.com/2","title":"Vestel","seendate":"20261119T120000Z","sourcecountry":"Turkey","language":"Turkish"}]}""";
        var hits = GdeltNewsSource.Parse(json);
        Assert.Equal("global", hits[0].Region);
        Assert.Equal("tr", hits[1].Region);
    }

    [Theory]
    [InlineData("tr", "tr")]
    [InlineData("GLOBAL", "global")]
    [InlineData("all", null)]
    [InlineData(null, null)]
    public void Secim_degeri_normalize_edilir(string? value, string? expected) =>
        Assert.Equal(expected, EgebisLeadFinder.Models.NewsRegions.Normalize(value));
}

public class NewsRegionRuleAndNaceVerifyTests
{
    [Theory]
    [InlineData("Tesla'dan Almanya'ya 3 milyar euroluk yatırım", "global")]
    [InlineData("İzmir'den Avrupa'ya 10 milyon euroluk sanayi çıkarması", "tr")]
    [InlineData("Tarkim'in Turgutlu fabrikası için üretim izni başvurusu", null)]
    [InlineData("Makfry Group'tan 300 milyon TL'lik yeni üretim tesisi yatırımı", "tr")]
    [InlineData("Manisa'da yeni fabrika", "tr")]
    [InlineData("Vantilatör üretimi arttı", null)]
    public void Bolge_haberin_konusuna_gore_belirlenir(string text, string? expected) =>
        Assert.Equal(expected, EgebisLeadFinder.Services.News.NewsRegionRules.Decide(text));

    [Fact]
    public void Firma_ulkesi_varsa_bolgeyi_o_belirler()
    {
        Assert.Equal("tr", EgebisLeadFinder.Services.News.NewsRegionRules.Decide("Tesla haberi", "Türkiye"));
        Assert.Equal("global", EgebisLeadFinder.Services.News.NewsRegionRules.Decide("Manisa", "Almanya"));
    }

    [Fact]
    public void Kablo_ureticisine_verilen_beyaz_esya_kodu_kablo_koduna_duzeltilir()
    {
        var code = EgebisLeadFinder.Services.NaceCatalog.Verify("27.51.08", new[] { "Elektrik kablo üretimi", "kablo gruplama", "fason hizmetleri" });
        Assert.Equal("27.32.03", code);
    }

    [Fact]
    public void Dogru_kod_korunur()
    {
        Assert.Equal("27.51.08", EgebisLeadFinder.Services.NaceCatalog.Verify("27.51.08",
            new[] { "Beyaz Eşya ve Ankastre Ürünleri İmalatı", "Ankastre fırın", "ocak", "davlumbaz" }));
        Assert.Equal("27.51", EgebisLeadFinder.Services.NaceCatalog.Verify("27.51", Array.Empty<string?>()));
    }
}
