using EgebisLeadFinder.Services;

namespace EgebisLeadFinder.Tests;

public class TextExtractorTests
{
    [Theory]
    [InlineData("İletişim: info@firma.com.tr adresinden", "info@firma.com.tr")]
    [InlineData("satis@ornek.com", "satis@ornek.com")]
    [InlineData("ahmet.yilmaz@abc-metal.com.tr", "ahmet.yilmaz@abc-metal.com.tr")]
    public void Gecerli_e_postalari_bulur(string text, string expected)
    {
        Assert.Contains(expected, TextExtractor.FindEmails(text));
    }

    [Fact]
    public void E_postaya_bitisik_metni_yutmaz()
    {
        // HTML duz metne cevrilirken kelimeler birlesebiliyor.
        var emails = TextExtractor.FindEmails("info@dmrmakina.com.trbinbirsoft tarafından").ToList();

        Assert.Contains("info@dmrmakina.com", emails);
        Assert.DoesNotContain(emails, e => e.Contains("binbirsoft"));
    }

    [Fact]
    public void Gorsel_dosya_adlarini_e_posta_saymaz()
    {
        Assert.Empty(TextExtractor.FindEmails("logo@2x.png ve icon@3x.jpg"));
    }

    [Fact]
    public void Mailto_ve_tel_baglantilarini_toplar()
    {
        const string html = """
            <a href="mailto:satis@firma.com.tr?subject=Teklif">Bize yazın</a>
            <a href="tel:+902241234567">Ara</a>
            <a href="/hakkimizda">Hakkımızda</a>
            """;

        var (emails, phones) = TextExtractor.ExtractContactLinks(html);

        Assert.Equal(new[] { "satis@firma.com.tr" }, emails);
        Assert.Equal(new[] { "+902241234567" }, phones);
    }

    [Fact]
    public void Script_ve_style_iceriklerini_metne_katmaz()
    {
        const string html = """
            <html><head><style>body{color:red}</style></head>
            <body><script>var x = 'gizli';</script><p>Görünen metin</p></body></html>
            """;

        var text = TextExtractor.ToPlainText(html);

        Assert.Contains("Görünen metin", text);
        Assert.DoesNotContain("gizli", text);
        Assert.DoesNotContain("color:red", text);
    }

    [Fact]
    public void Turkiye_telefon_formatlarini_bulur()
    {
        var phones = TextExtractor.FindPhones("Tel: 0224 270 06 00 Faks: +90 216 300 16 00").ToList();

        Assert.Equal(2, phones.Count);
    }
}
