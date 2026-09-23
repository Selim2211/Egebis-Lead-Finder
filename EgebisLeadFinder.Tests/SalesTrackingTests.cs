using System.Text.Json;
using EgebisLeadFinder.Models;
using EgebisLeadFinder.Services;

namespace EgebisLeadFinder.Tests;

public class SalesTrackingTests
{
    private static readonly DateOnly Today = new(2026, 9, 19);

    [Theory]
    [InlineData("2023-07-01", "3 yıl 2 ay")]
    [InlineData("2026-03-10", "6 ay")]
    [InlineData("2024-09-19", "2 yıl")]
    [InlineData("2026-09-05", "1 aydan az")]
    public void Calisma_suresi_okunur_metne_cevrilir(string start, string expected)
    {
        var contact = new Contact { EmploymentStartDate = DateOnly.Parse(start) };

        Assert.Equal(expected, contact.TenureText(Today));
    }

    [Fact]
    public void Baslama_tarihi_yoksa_sure_bos()
    {
        Assert.Null(new Contact().TenureText(Today));
    }

    [Fact]
    public void Apollo_mevcut_is_kaydindan_baslama_tarihi_okunur()
    {
        using var doc = JsonDocument.Parse("""
            {"employment_history":[
              {"organization_name":"Eski A.Ş.","start_date":"2015-01-01","end_date":"2019-02-01","current":false},
              {"organization_name":"TOFAŞ","start_date":"2019-03-01","end_date":null,"current":true}
            ]}
            """);

        Assert.Equal(new DateOnly(2019, 3, 1), ApolloPersonEmailFinder.CurrentEmploymentStart(doc.RootElement));
    }

    [Fact]
    public void Apollo_is_gecmisi_yoksa_tarih_bos()
    {
        using var doc = JsonDocument.Parse("""{"name":"Ali Veli"}""");

        Assert.Null(ApolloPersonEmailFinder.CurrentEmploymentStart(doc.RootElement));
    }

    [Fact]
    public void Firma_adi_alan_adinda_gecen_site_one_alinir()
    {
        var official = new SearchResult { Domain = "toyota.com.tr", Title = "Toyota Türkiye" };
        var news = new SearchResult { Domain = "haberler.com", Title = "Toyota yeni modelini tanıttı" };
        var unrelated = new SearchResult { Domain = "ornek.com", Title = "Otomotiv sektörü" };

        var officialScore = SerperSearchService.NameRelevance(official, "Toyota");

        Assert.True(officialScore > SerperSearchService.NameRelevance(news, "Toyota"));
        Assert.Equal(0, SerperSearchService.NameRelevance(unrelated, "Toyota"));
    }

    [Fact]
    public void Firma_adindaki_turkce_karakterler_esitlenir()
    {
        var result = new SearchResult { Domain = "arcelik.com.tr", Title = "Arçelik" };

        Assert.True(SerperSearchService.NameRelevance(result, "Arçelik") >= 10);
    }

    [Theory]
    [InlineData("Ahmet Yılmaz", "Ahmet Yılmaz")]
    [InlineData("AHMET YILMAZ", "Ahmet Yılmaz")]
    [InlineData("ismail çelik", "İsmail Çelik")]
    [InlineData("Mustafa At***n", "Mustafa")]
    [InlineData("Ayşe Nur Kaya", "Ayşe Nur Kaya")]
    public void Hitap_lead_ad_soyadini_kullanir(string name, string expected)
    {
        Assert.Equal(expected, PersonDisplay.SalutationName(new Contact { Name = name }));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("info@firma.com")]
    [InlineData("(isim yok)")]
    public void Isim_yoksa_sayin_yetkili_kullanilir(string? name)
    {
        Assert.Equal("Yetkili", EmailTemplateService.ContactSalutation(new Contact { Name = name }));
    }

    [Fact]
    public void Sablon_hitabi_kisinin_adiyla_doldurulur()
    {
        var template = new EmailTemplate { Subject = "{COMPANY_NAME} için", Body = "Sayın {CONTACT_NAME}," };
        var (_, body) = new EmailTemplateService().Render(template,
            new Company { Name = "TOFAŞ" }, new Contact { Name = "MEHMET DEMİR" });

        Assert.Equal("Sayın Mehmet Demir,", body);
    }

    [Fact]
    public void Bicimli_taslak_degiskenleri_html_guvenli_doldurulur()
    {
        var template = new EmailTemplate
        {
            Body = "düz",
            BodyHtml = "<b>Sayın {CONTACT_NAME}</b>,<br><img src=\"/Email/Image/4\" width=\"120\">{COMPANY_NAME}"
        };

        var html = new EmailTemplateService().RenderHtml(template,
            new Company { Name = "A&B <Makine>" }, new Contact { Name = "Ali Veli" });

        Assert.Contains("<b>Sayın Ali Veli</b>", html);
        Assert.Contains("A&amp;B &lt;Makine&gt;", html);
        Assert.Contains("src=\"/Email/Image/4\"", html);
    }

    [Fact]
    public void Model_listesinde_yalnizca_metin_ureten_gemini_modelleri_kalir()
    {
        using var doc = JsonDocument.Parse("""
            {"models":[
              {"name":"models/gemini-3.6-flash","displayName":"Gemini 3.6 Flash","supportedGenerationMethods":["generateContent","countTokens"]},
              {"name":"models/text-embedding-004","supportedGenerationMethods":["embedContent"]},
              {"name":"models/gemini-embedding-001","supportedGenerationMethods":["embedContent"]},
              {"name":"models/gemini-2.5-flash-image","supportedGenerationMethods":["generateContent"]},
              {"name":"models/gemma-3-27b-it","supportedGenerationMethods":["generateContent"]},
              {"name":"models/gemini-3.5-transcribe","supportedGenerationMethods":["generateContent"]},
              {"name":"models/gemini-2.5-computer-use-preview-10-2025","supportedGenerationMethods":["generateContent"]}
            ]}
            """);

        var ids = doc.RootElement.GetProperty("models").EnumerateArray()
            .Select(GeminiModelCatalog.Parse).Where(m => m is not null).Select(m => m!.Id).ToList();

        Assert.Equal(new[] { "gemini-3.6-flash" }, ids);
    }

    [Theory]
    [InlineData("gemini-3.6-flash", true)]
    [InlineData("gemini-2.5-pro-preview-06-05", true)]
    [InlineData("../../x", false)]
    [InlineData("gemini flash", false)]
    [InlineData(null, false)]
    public void Model_adi_dogrulanir(string? id, bool valid)
    {
        Assert.Equal(valid, GeminiModelCatalog.IsValidModelId(id));
    }

    private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0 };

    [Fact]
    public void Html_yoksa_duz_metin_gider()
    {
        var body = SmtpEmailSender.BuildBody("Merhaba", null, null);

        Assert.IsType<MimeKit.TextPart>(body);
    }

    [Fact]
    public void Metne_yerlestirilen_gorsel_ayni_yerde_cid_ile_gider()
    {
        var images = new List<EmailImage>
        {
            new() { Id = 7, Name = "SAP", ContentType = "image/png", Data = Png }
        };
        var html = EmailHtml.Sanitize(
            "Sayın Ali,<br><img src=\"http://localhost:5087/Email/Image/7\" width=\"120\" style=\"float:right;width:120px\">Metin"
            + "<img src=\"/Email/Image/7\"><img src=\"/Email/Image/99\">");

        var message = new MimeKit.MimeMessage { Body = SmtpEmailSender.BuildBody("Sayın Ali,\nMetin", html, images) };

        var sent = message.HtmlBody!;
        // Ayni gorsel iki kez kullanilsa da tek ek; bilinmeyen gorsel (99) atilir.
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(sent, "cid:").Count);
        Assert.Single(message.BodyParts.OfType<MimeKit.MimePart>().Where(p => p.ContentId is not null));
        Assert.True(sent.IndexOf("cid:") > sent.IndexOf("Sayın Ali") && sent.IndexOf("cid:") < sent.IndexOf("Metin"));
        Assert.Contains("float:right", sent);
        Assert.DoesNotContain("localhost", sent);
    }

    [Fact]
    public void Editor_html_i_temizlenir()
    {
        var clean = EmailHtml.Sanitize(
            "<p onclick=\"x()\">Merhaba <script>alert(1)</script><b>kalın</b></p>"
            + "<img src=\"https://kotu.com/a.png\"><img src=\"/Email/Image/3\" alt='a\" onerror=\"x()' style=\"width:90px;background:url(javascript:x)\">"
            + "<a href=\"javascript:x()\">link</a><font color=\"red\">renk</font><iframe src=\"x\"></iframe>");

        Assert.DoesNotContain("script", clean);
        Assert.DoesNotContain("onclick", clean);
        Assert.DoesNotContain("kotu.com", clean);
        Assert.DoesNotContain("javascript", clean);
        Assert.DoesNotContain("iframe", clean);
        Assert.DoesNotContain("onerror=\"", clean);
        Assert.Contains("<b>kalın</b>", clean);
        Assert.Contains("renk", clean);
        Assert.Contains("src=\"/Email/Image/3\"", clean);
        Assert.Contains("width:90px", clean);
        Assert.Equal(new List<int> { 3 }, EmailHtml.ImageIds(clean));
    }

    [Fact]
    public void Gorsel_imzasi_kontrol_edilir()
    {
        Assert.True(EgebisLeadFinder.Controllers.EmailController.LooksLikeImage(Png));
        Assert.False(EgebisLeadFinder.Controllers.EmailController.LooksLikeImage("<html>hack</html>"u8.ToArray()));
    }

    [Fact]
    public void Bas_harfler_ve_sabit_renk_uretilir()
    {
        Assert.Equal("AY", PersonDisplay.Initials("Ahmet Yılmaz"));
        Assert.Equal("İ", PersonDisplay.Initials("ismail"));
        Assert.Equal("?", PersonDisplay.Initials(null));
        Assert.Equal("?", PersonDisplay.Initials("(kişi seçilmedi)"));
        Assert.Equal("I", PersonDisplay.Initials("info@orau.com.tr"));
        Assert.Equal(PersonDisplay.Hue("Egebis"), PersonDisplay.Hue("Egebis"));
        Assert.InRange(PersonDisplay.Hue("Egebis"), 0, 5);
    }
}
