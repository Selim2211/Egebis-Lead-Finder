using EgebisLeadFinder.Configuration;
using EgebisLeadFinder.Models;
using EgebisLeadFinder.Services;
using EgebisLeadFinder.Services.CompanyIntel;
using Microsoft.Extensions.Options;

namespace EgebisLeadFinder.Tests;

/// <summary>
/// Deterministik degerlendirici: AI oneri verir, kod karar verir (LeadScoringService gibi).
/// Dogrulanmis risk => Riskli; kanit yok => en fazla Incelenmeli; guclu pozitif => Guclu.
/// </summary>
public class CompanyRatingEvaluatorTests
{
    private static CompanyRatingEvaluator CreateEvaluator() =>
        new(Options.Create(new ResearchOptions()));

    private static IntelSnippet Snippet(string text, IntelKind kind, string? url = "https://haber.example/1") =>
        new() { Text = text, Kind = kind, SourceUrl = url };

    [Fact]
    public void Dogrulanmis_konkordato_AI_guclu_dese_bile_riskli()
    {
        var rating = new CompanyRating
        {
            Signal = "guclu",
            FinancialInfo = "Ciro 500 milyon TL",
            FinancialSource = "KAP",
            GrowthSignals = { "Yeni fabrika yatırımı" },
            RiskSignals =
            {
                new RiskSignal
                {
                    Text = "Firma konkordato ilan etti",
                    Severity = "yuksek",
                    SourceUrl = "https://haber.example/konkordato"
                }
            }
        };

        var result = CreateEvaluator().Evaluate(rating, new List<IntelSnippet>());

        Assert.Equal(RatingSignal.Riskli, result.Signal);
    }

    [Fact]
    public void Ham_risk_haberi_tek_basina_riskli_yapmaz_en_fazla_incelenmeli()
    {
        // Risk-kelimesi sorgusu her firma icin bir seyler getirir; AI doğrulamadan
        // RISKLI demek Ford Otosan gibi firmalara yanlış yapıştırıyordu.
        var rating = new CompanyRating { Signal = "guclu" };
        var snippets = new List<IntelSnippet>
        {
            Snippet("Konkordato haberleri: bu hafta 12 firma başvurdu.", IntelKind.Risk)
        };

        var result = CreateEvaluator().Evaluate(rating, snippets);

        Assert.Equal(RatingSignal.Incelenmeli, result.Signal);
    }

    [Fact]
    public void AI_kaynak_verdi_yuksek_severity_ise_riskli()
    {
        var rating = new CompanyRating
        {
            Signal = "incelenmeli",
            RiskSignals =
            {
                new RiskSignal
                {
                    Text = "Şirket hakkında haciz işlemi başlatıldı",
                    Severity = "yuksek",
                    SourceUrl = "https://haber.example/haciz"
                }
            }
        };

        var result = CreateEvaluator().Evaluate(rating, new List<IntelSnippet>());

        Assert.Equal(RatingSignal.Riskli, result.Signal);
    }

    [Fact]
    public void Kaynagi_olmayan_yuksek_risk_iddiasi_tek_basina_riskli_yapmaz()
    {
        // AI kaynak vermeden "riskli" dedi; deterministik katman buna guvenmez.
        var rating = new CompanyRating
        {
            Signal = "riskli",
            RiskSignals = { new RiskSignal { Text = "Söylentiye göre zor durumda", Severity = "yuksek", SourceUrl = null } }
        };

        var result = CreateEvaluator().Evaluate(rating, new List<IntelSnippet>());

        Assert.NotEqual(RatingSignal.Riskli, result.Signal);
    }

    [Fact]
    public void Hic_veri_yoksa_bilinmiyor()
    {
        var rating = new CompanyRating { Signal = "incelenmeli" };

        var result = CreateEvaluator().Evaluate(rating, new List<IntelSnippet>());

        Assert.Equal(RatingSignal.Bilinmiyor, result.Signal);
    }

    [Fact]
    public void Snippet_var_ama_pozitif_kanit_yoksa_incelenmeli()
    {
        var rating = new CompanyRating { Signal = "guclu" };
        var snippets = new List<IntelSnippet>
        {
            Snippet("Firma hakkında genel bir tanıtım yazısı.", IntelKind.Haber)
        };

        var result = CreateEvaluator().Evaluate(rating, snippets);

        Assert.Equal(RatingSignal.Incelenmeli, result.Signal);
    }

    [Fact]
    public void KAP_finansali_plus_buyume_risk_yoksa_guclu()
    {
        var rating = new CompanyRating
        {
            Signal = "incelenmeli",
            FinancialInfo = "2024 net kâr 120 milyon TL",
            FinancialSource = "KAP",
            GrowthSignals = { "Kapasite artışı yatırımı duyuruldu" }
        };

        var result = CreateEvaluator().Evaluate(rating, new List<IntelSnippet>());

        Assert.Equal(RatingSignal.Guclu, result.Signal);
    }

    [Fact]
    public void Uc_bagimsiz_pozitif_finansal_olmadan_da_guclu()
    {
        var rating = new CompanyRating
        {
            Signal = "incelenmeli",
            FoundingInfo = "1985 yılında kurulmuş köklü bir firma",
            GrowthSignals = { "Yeni tesis açılışı" },
            Customers = { "Ford Otosan", "TOFAŞ" },
            Projects = { "Avrupa ihracat anlaşması" }
        };

        var result = CreateEvaluator().Evaluate(rating, new List<IntelSnippet>());

        Assert.Equal(RatingSignal.Guclu, result.Signal);
    }

    [Fact]
    public void Orta_seviye_risk_haber_finansali_ile_en_fazla_incelenmeli()
    {
        var rating = new CompanyRating
        {
            Signal = "guclu",
            FinancialInfo = "Ciro 300 milyon TL",
            FinancialSource = "haber",
            GrowthSignals = { "Yeni yatırım" },
            RiskSignals =
            {
                new RiskSignal
                {
                    Text = "Bir tedarikçi icra takibi başlattı",
                    Severity = "orta",
                    SourceUrl = "https://haber.example/icra"
                }
            }
        };

        var result = CreateEvaluator().Evaluate(rating, new List<IntelSnippet>());

        Assert.Equal(RatingSignal.Incelenmeli, result.Signal);
    }

    [Fact]
    public void KAP_finansali_plus_3_pozitif_rutin_dava_ile_yine_guclu()
    {
        // Ford Otosan vakası: İSO 500, KAP denetimli finansal, yatırım haberi +
        // AI'ın bulduğu rutin bir mahkeme kaydı. Bu tek başına 'İncelenmeli'ye çekmemeli.
        var rating = new CompanyRating
        {
            Signal = "guclu",
            FoundingInfo = "1959'da kurulmuş köklü otomotiv üreticisi",
            FinancialInfo = "KAP 2024/12: Hasılat 900 milyar TL",
            FinancialSource = "KAP",
            GrowthSignals = { "Yeni elektrikli araç yatırımı" },
            Projects = { "Ford Trucks ihracat anlaşması" },
            RiskSignals =
            {
                new RiskSignal
                {
                    Text = "Bölge Adliye Mahkemesi kaydında bir dava süreci geçiyor",
                    Severity = "orta",
                    SourceUrl = "https://hukukiris.example/karar"
                }
            }
        };

        var result = CreateEvaluator().Evaluate(rating, new List<IntelSnippet>());

        Assert.Equal(RatingSignal.Guclu, result.Signal);
    }

    [Fact]
    public void Tek_pozitif_kanit_guclu_icin_yetmez()
    {
        var rating = new CompanyRating
        {
            Signal = "guclu",
            GrowthSignals = { "Küçük bir yatırım haberi" }
        };

        var result = CreateEvaluator().Evaluate(rating, new List<IntelSnippet>());

        Assert.Equal(RatingSignal.Incelenmeli, result.Signal);
    }
}
