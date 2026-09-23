using System.Text;
using ClosedXML.Excel;
using EgebisLeadFinder.Configuration;
using EgebisLeadFinder.Models;
using EgebisLeadFinder.Services;
using EgebisLeadFinder.Services.Sequences;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace EgebisLeadFinder.Tests;

/// <summary>Rakip acigi kapatan ozellikler: disa aktarim, e-posta dogrulama, AI mail, ICP/NACE, dizi, cevap algilama.</summary>
public class CompetitorFeatureTests
{
    // ---------------- Disa aktarim ----------------

    [Fact]
    public void Csv_bom_noktali_virgul_ve_formul_korumasi()
    {
        var table = new ExportTable("T", new[] { "Ad", "Not" }, new List<object?[]>
        {
            new object?[] { "Örnek; A.Ş.", "=HYPERLINK(\"x\")" },
            new object?[] { "Satır\nsonu", 42 }
        });

        var bytes = ExportService.ToCsv(table);
        var text = Encoding.UTF8.GetString(bytes);

        Assert.Equal(Encoding.UTF8.GetPreamble(), bytes.Take(3).ToArray());
        Assert.Contains("\"Örnek; A.Ş.\"", text);
        Assert.Contains("\"'=HYPERLINK(\"\"x\"\")\"", text);
        Assert.Contains("\"Satır\nsonu\";42", text);
    }

    [Fact]
    public void Xlsx_firma_ve_kisi_sayfalarini_uretir()
    {
        var company = new Company
        {
            Name = "Haksan", City = "Bursa", Score = 72, NaceCode = "22.19",
            Contacts = { new Contact { Name = "Ali Veli", Email = "ali@haksan.com", EmailStatus = EmailStatus.Valid } }
        };

        var bytes = ExportService.ToXlsx(ExportService.CompanyTables(new[] { company }));

        using var wb = new XLWorkbook(new MemoryStream(bytes));
        Assert.Equal("Haksan", wb.Worksheet("Firmalar").Cell(2, 1).GetString());
        Assert.Equal("22.19", wb.Worksheet("Firmalar").Cell(2, 6).GetString());
        Assert.Equal("Geçerli", wb.Worksheet("Kişiler").Cell(2, 5).GetString());
    }

    // ---------------- E-posta dogrulama ----------------

    private static EmailVerificationService Verifier(MxLookup result) =>
        new(new FakeMx(result), new MemoryCache(new MemoryCacheOptions()));

    [Theory]
    [InlineData("ali.veli@firma.com.tr", MxLookup.HasMail, EmailStatus.Valid)]
    [InlineData("info@firma.com.tr", MxLookup.HasMail, EmailStatus.Role)]
    [InlineData("satis@firma.com", MxLookup.HasMail, EmailStatus.Role)]
    [InlineData("ali@firma.com", MxLookup.NoMail, EmailStatus.Invalid)]
    [InlineData("ali@yokboyle.xyz", MxLookup.NoDomain, EmailStatus.Invalid)]
    [InlineData("ali@firma.com", MxLookup.Unknown, EmailStatus.Risky)]
    [InlineData("ali@mailinator.com", MxLookup.HasMail, EmailStatus.Risky)]
    [InlineData("a***@firma.com", MxLookup.HasMail, EmailStatus.Invalid)]
    [InlineData("gecersiz", MxLookup.HasMail, EmailStatus.Invalid)]
    public async Task Eposta_durumu_dogru_belirlenir(string email, MxLookup mx, EmailStatus expected)
    {
        var result = await Verifier(mx).VerifyAsync(email);
        Assert.Equal(expected, result.Status);
    }

    [Fact]
    public async Task Mx_sonucu_alan_adi_basina_onbellege_alinir()
    {
        var mx = new FakeMx(MxLookup.HasMail);
        var verifier = new EmailVerificationService(mx, new MemoryCache(new MemoryCacheOptions()));

        await verifier.VerifyAsync("a@firma.com");
        await verifier.VerifyAsync("b@firma.com");

        Assert.Equal(1, mx.Calls);
    }

    // ---------------- AI mail ----------------

    [Fact]
    public void Ai_metni_html_kodlanarak_paragraflanir()
    {
        var html = EmailDraftService.ToHtml("Sayın Ali Bey,\n\n<script>x</script> satır\nikinci");

        Assert.Equal("<p>Sayın Ali Bey,</p><p>&lt;script&gt;x&lt;/script&gt; satır<br>ikinci</p>", html);
    }

    [Fact]
    public void Ai_bilgi_notu_firsat_ve_takip_bilgisini_icerir()
    {
        var brief = GeminiAiService.BuildEmailBrief(new EmailDraftInput
        {
            Company = new Company { Name = "Haksan", City = "Bursa" },
            Contact = new Contact { Name = "Tevfik Ezik", Title = "Genel Müdür" },
            Rating = new CompanyRating
            {
                SalesApproach = "MES ile yaklaşın",
                Opportunities = { new Opportunity { Text = "MES", Reason = "Yeni fabrika" } }
            },
            IsFollowUp = true,
            PreviousEmails = { "01.09.2026 — SAP tanıtım" }
        });

        Assert.Contains("takip", brief);
        Assert.Contains("Fırsat: MES — Yeni fabrika", brief);
        Assert.Contains("Tevfik Ezik", brief);
        Assert.Contains("SAP tanıtım", brief);
    }

    // ---------------- NACE + ICP ----------------

    [Theory]
    [InlineData("22.19", "22.19")]
    [InlineData("2219", "22.19")]
    [InlineData("C22", "22")]
    [InlineData("22.1", "22.1")]
    [InlineData("yok", null)]
    public void Nace_kodu_normalize_edilir(string input, string? expected) =>
        Assert.Equal(expected, NaceCatalog.Normalize(input));

    [Fact]
    public void Nace_katalogu_imalat_bolumlerini_icerir()
    {
        Assert.Equal(88, NaceCatalog.Divisions.Count);
        Assert.Equal("C", NaceCatalog.Division("22.19")!.SectionCode);
    }

    private static LeadScoringService Scoring() => new(Options.Create(new ScoringOptions
    {
        Manufacturer = 30, TargetIndustry = 20, SapFound = 20, LargeCompany = 10, ItManagerFound = 10, EmailFound = 10,
        TargetIndustryKeywords = new List<string> { "otomotiv" }
    }));

    private static CompanyAnalysis Analysis(string nace = "22.19", string size = "450 çalışan") => new()
    {
        Industry = "Kauçuk ve plastik", NaceCode = nace, Manufacturer = true, Potential = true, EmployeeSizeHint = size
    };

    [Fact]
    public void Icp_nace_ve_bolge_eslesirse_puan_ve_uyum_verilir()
    {
        var icp = new IcpProfile { NaceCodes = { "22" }, Cities = { "Bursa" }, MinEmployees = 100 };
        var company = new Company { Name = "Haksan", City = "Bursa" };

        var score = Scoring().ScoreCompany(Analysis(), null, null, icp, company);

        Assert.True(score.IcpMatch);
        Assert.Contains(score.Items, i => i.Reason.Contains("NACE 22.19"));
        Assert.Contains(score.Items, i => i.Reason == "ICP: hedef bölge");
    }

    [Fact]
    public void Nace_kodu_olmayan_eski_firma_sektor_puanini_kaybetmez()
    {
        var icp = new IcpProfile { NaceCodes = { "22" } };
        var analysis = Analysis(nace: null!);
        analysis.Industry = "Otomotiv yan sanayi";

        var withIcp = Scoring().ScoreCompany(analysis, null, null, icp, new Company());
        var without = Scoring().ScoreCompany(analysis, null, null);

        Assert.Equal(without.Total, withIcp.Total);
        Assert.False(withIcp.IcpMatch);
    }

    [Fact]
    public void Icp_bolge_disindaki_firma_uymaz()
    {
        var icp = new IcpProfile { NaceCodes = { "22" }, Cities = { "Kocaeli" } };

        var score = Scoring().ScoreCompany(Analysis(), null, null, icp, new Company { City = "Bursa" });

        Assert.False(score.IcpMatch);
    }

    [Fact]
    public void Icp_haric_kelimesi_firmayi_eler()
    {
        var icp = new IcpProfile { ExcludeKeywords = { "bayi" } };

        var score = Scoring().ScoreCompany(Analysis(), null, null, icp, new Company { Name = "Plastik Bayi Ltd" });

        Assert.Equal(0, score.Total);
    }

    [Fact]
    public void Bos_icp_eski_puanlamayla_ayni()
    {
        var old = Scoring().ScoreCompany(Analysis(), null, null);
        var withEmpty = Scoring().ScoreCompany(Analysis(), null, null, new IcpProfile(), new Company());

        Assert.Equal(old.Total, withEmpty.Total);
        Assert.False(withEmpty.IcpMatch);
    }

    [Theory]
    [InlineData("500+ çalışan", 500)]
    [InlineData("10.000'in üzerinde", 10000)]
    [InlineData("50-100 kişi", 50)]
    [InlineData("bilinmiyor", 0)]
    public void Calisan_sayisi_okunur(string hint, int expected) =>
        Assert.Equal(expected, LeadScoringService.EmployeeCount(hint));

    // ---------------- Dizi zamanlama ----------------

    private static DateTime Tr(int y, int mo, int d, int h, int mi = 0) =>
        TimeZoneInfo.ConvertTimeToUtc(new DateTime(y, mo, d, h, mi, 0), SequenceScheduler.Istanbul);

    [Fact]
    public void Pencere_hafta_ici_09_18()
    {
        Assert.True(SequenceScheduler.IsInWindow(Tr(2026, 9, 23, 10)));   // Carsamba
        Assert.False(SequenceScheduler.IsInWindow(Tr(2026, 9, 23, 18)));
        Assert.False(SequenceScheduler.IsInWindow(Tr(2026, 9, 26, 11)));  // Cumartesi
    }

    [Fact]
    public void Aksam_eklenen_ilk_adim_ertesi_is_gunu_sabah_gider()
    {
        var next = SequenceScheduler.NextSendAt(Tr(2026, 9, 25, 20), 0); // Cuma 20:00
        Assert.Equal(Tr(2026, 9, 28, 9), next);                            // Pazartesi 09:00
    }

    [Fact]
    public void Gun_eklenince_hafta_sonu_atlanir()
    {
        var next = SequenceScheduler.NextSendAt(Tr(2026, 9, 24, 14), 2); // Persembe + 2 = Cumartesi
        Assert.Equal(Tr(2026, 9, 28, 9), next);
    }

    [Fact]
    public void Dizi_atlama_nedenleri()
    {
        var ok = new Lead { Contact = new Contact { Email = "a@b.com" } };
        Assert.Null(SequenceService.SkipReason(ok, false));
        Assert.Equal("zaten bir dizide", SequenceService.SkipReason(ok, true));
        Assert.Equal("cevap vermiş", SequenceService.SkipReason(new Lead { RepliedAt = DateTime.UtcNow, Contact = ok.Contact }, false));
        Assert.Equal("e-posta adresi yok", SequenceService.SkipReason(new Lead { Contact = new Contact() }, false));
        Assert.Equal("e-posta geçersiz", SequenceService.SkipReason(
            new Lead { Contact = new Contact { Email = "a@b.com", EmailStatus = EmailStatus.Invalid } }, false));
    }

    // ---------------- Cevap algilama ----------------

    private static InboundMessage Msg(string from, string subject, string? auto = null, string? body = null) =>
        new(from, subject, DateTime.UtcNow, new List<string>(), auto, body);

    [Fact]
    public void Gelen_mail_siniflandirilir()
    {
        Assert.Equal(InboundKind.Reply, ReplyMatcher.Classify(Msg("ali@firma.com", "Re: SAP entegrasyonu")));
        Assert.Equal(InboundKind.AutoReply, ReplyMatcher.Classify(Msg("ali@firma.com", "Otomatik yanıt: izindeyim")));
        Assert.Equal(InboundKind.AutoReply, ReplyMatcher.Classify(Msg("ali@firma.com", "Re: SAP", auto: "auto-replied")));
        Assert.Equal(InboundKind.Bounce, ReplyMatcher.Classify(Msg("MAILER-DAEMON@mx.google.com", "Delivery Status Notification (Failure)")));
    }

    [Fact]
    public void Geri_donen_mailde_alici_bulunur()
    {
        var m = Msg("postmaster@firma.com", "Undeliverable", body: "Your message to ali@firma.com couldn't be delivered.");

        Assert.Equal("ali@firma.com", ReplyMatcher.BouncedRecipient(m, new[] { "ALI@firma.com", "veli@x.com" }), ignoreCase: true);
    }

    [Fact]
    public void Message_id_normalize()
    {
        Assert.Equal("abc@egebis.com", ReplyMatcher.NormalizeMessageId(" <ABC@egebis.com> "));
    }

    private class FakeMx : IMxResolver
    {
        private readonly MxLookup _result;
        public FakeMx(MxLookup result) => _result = result;
        public int Calls { get; private set; }

        public Task<MxLookup> LookupAsync(string domain, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(_result);
        }
    }
}
