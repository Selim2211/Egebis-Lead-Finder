using EgebisLeadFinder.Configuration;
using EgebisLeadFinder.Models;
using EgebisLeadFinder.Services;
using EgebisLeadFinder.Services.CompanyIntel;
using Microsoft.Extensions.Options;

namespace EgebisLeadFinder.Tests;

/// <summary>
/// Uygulamayi farkli isler yapan sirketler kullanir: arama, analiz, arastirma ve taslaklar
/// "Biz ne arıyoruz?" profiline gore calisir; Egebis (SAP/uretici) varsayimlari yalnizca profil yokken.
/// </summary>
public class ProfileDrivenTests
{
    /// <summary>Boru/vana toptancisi: musterileri tesisat ve insaat firmalari, SAP ile ilgisi yok.</summary>
    private static BusinessProfile PipeSupplier() => new()
    {
        CompanyName = "Sey Boru",
        Offering = "Endüstriyel boru, vana ve fittings tedariki",
        IdealCustomer = "Tesisat, inşaat ve sanayi tesisleri; mekanik tesisat müteahhitleri",
        BuyingSignals = { "tesisat projesi", "yeni şantiye", "ihale" },
        Segments = { new TargetSegment { Id = "tesisat1", Name = "Mekanik tesisat", Keywords = { "tesisat" } } }
    };

    [Fact]
    public void Musteri_turu_secimden_ya_da_tanimdan_cikarilir()
    {
        Assert.False(PipeSupplier().TargetsManufacturers);
        Assert.True(new BusinessProfile { IdealCustomer = "50-1000 çalışanlı üretici firmalar" }.TargetsManufacturers);
        Assert.True(new BusinessProfile { IdealCustomer = "Tesisat", CustomerKind = BusinessProfile.KindManufacturer }.TargetsManufacturers);
        Assert.False(new BusinessProfile { IdealCustomer = "Fabrikalar", CustomerKind = BusinessProfile.KindAny }.TargetsManufacturers);
    }

    [Fact]
    public void SAP_yalnizca_kelime_olarak_gecerse_sayilir()
    {
        Assert.False(new BusinessProfile { Offering = "Ön muhasebe ve hesap yazılımı", IdealCustomer = "KOBİ" }.MentionsSap);
        Assert.True(new BusinessProfile { Offering = "SAP danışmanlığı", IdealCustomer = "Fabrikalar" }.MentionsSap);
        Assert.True(new BusinessProfile { Offering = "SAP'ye geçiş", IdealCustomer = "Fabrikalar" }.MentionsSap);
    }

    [Fact]
    public void Profil_blogu_musteri_turu_ve_ihtiyac_sinyallerini_yapay_zekaya_verir()
    {
        var block = PipeSupplier().ToPromptBlock();

        Assert.Contains("MÜŞTERİ TÜRÜ: her tür şirket", block);
        Assert.Contains("İHTİYAÇ SİNYALLERİ", block);
        Assert.Contains("tesisat projesi", block);
    }

    [Fact]
    public void Uretici_olmayan_musteride_arama_firmalari_sirketleri_ile_yapilir_SAP_sorgusu_olmaz()
    {
        var criteria = new SearchCriteria { Industry = "Mekanik tesisat", RegionKey = "TR", City = "İzmir" };
        SearchPlanService.ApplyProfile(criteria, PipeSupplier());

        var web = SearchQueryBuilder.Build(criteria);
        var places = SearchQueryBuilder.BuildPlaceQueries(criteria);

        Assert.False(criteria.ManufacturerQueries);
        Assert.Contains(web, q => q.Contains("firmaları"));
        Assert.DoesNotContain(web, q => q.Contains("üreticileri") || q.Contains("fabrikası") || q.Contains("SAP"));
        Assert.Contains("Mekanik tesisat İzmir", places);
    }

    [Fact]
    public void Profil_yokken_eski_uretici_ve_SAP_sorgulari_kalir()
    {
        var criteria = new SearchCriteria { Industry = "Otomotiv", RegionKey = "TR" };
        SearchPlanService.ApplyProfile(criteria, new BusinessProfile());

        var web = SearchQueryBuilder.Build(criteria);

        Assert.Contains(web, q => q.Contains("üreticileri"));
        Assert.Contains(web, q => q.Contains("\"SAP\" kariyer"));
    }

    [Fact]
    public void Uretici_musterili_ama_SAP_disi_profilde_SAP_kariyer_sorgusu_yapilmaz()
    {
        var criteria = new SearchCriteria { Industry = "Plastik", RegionKey = "TR" };
        SearchPlanService.ApplyProfile(criteria, new BusinessProfile { Offering = "Robot hücresi", IdealCustomer = "Üretici fabrikalar" });

        var web = SearchQueryBuilder.Build(criteria);

        Assert.Contains(web, q => q.Contains("üreticileri"));
        Assert.DoesNotContain(web, q => q.Contains("SAP"));
    }

    [Fact]
    public void Harita_kategorisi_aranan_terimle_eslesirse_elenmez()
    {
        Assert.False(PlaceCategoryFilter.IsRelevant("Restoran"));
        Assert.True(PlaceCategoryFilter.IsRelevant("Restoran", new[] { "Restoran" }));
        Assert.True(PlaceCategoryFilter.IsRelevant("Oto Servis", new[] { "oto servisleri" }));
        Assert.False(PlaceCategoryFilter.IsRelevant("Otel", new[] { "Mekanik tesisat" }));
    }

    [Fact]
    public void SAP_disi_profilde_analiz_SAP_tespiti_istemez_ve_sablon_segmentten_secilir()
    {
        var prompt = GeminiAiService.AnalysisPrompt(PipeSupplier());

        Assert.DoesNotContain("SAP tespiti kuralları", prompt);
        Assert.Contains("sap alanı bu çalışmada kullanılmıyor", prompt);
        Assert.Contains("recommendedTemplate alanına \"GENEL\" yaz", prompt);
        Assert.DoesNotContain("SAP_ENTEGRASYON", prompt);

        var sapProfile = new BusinessProfile { Offering = "SAP danışmanlığı", IdealCustomer = "Fabrikalar" };
        Assert.Contains("SAP tespiti kuralları", GeminiAiService.AnalysisPrompt(sapProfile));
    }

    [Fact]
    public void Arastirma_sorgulari_ihtiyac_sinyalleri_ve_hedef_unvanlarla_yapilir()
    {
        var queries = CompanyResearchQueryBuilder.Build(new Company { Name = "Akme İnşaat" }, new ResearchOptions(),
            PipeSupplier(), new[] { "Satın Alma Müdürü", "Proje Müdürü" });

        Assert.DoesNotContain(queries, q => q.Contains("SAP") || q.Contains("Netsis"));
        Assert.Contains(queries, q => q.Contains("\"tesisat projesi\"") && q.Contains("ihale"));
        Assert.Contains(queries, q => q.Contains("\"Satın Alma Müdürü\""));
        Assert.True(queries.Count <= new ResearchOptions().MaxNewsQueries);

        var legacy = CompanyResearchQueryBuilder.Build(new Company { Name = "Akme" }, new ResearchOptions());
        Assert.Contains(legacy, q => q.Contains("SAP"));
    }

    [Fact]
    public void Profil_modunda_hedef_sektor_puani_segmentten_gelir()
    {
        var analysis = new CompanyAnalysis { Industry = "İnşaat", Potential = true, FitScore = 70, Segment = "Mekanik tesisat" };
        var breakdown = new LeadScoringService(Options.Create(new ScoringOptions { TargetIndustry = 15, TargetIndustryKeywords = { "inşaat" } }))
            .ScoreCompany(analysis, null, null, null, new Company(), new FitContext(true, Array.Empty<string>()));

        Assert.Contains(breakdown.Items, i => i.Reason == "Hedef segment: Mekanik tesisat" && i.Points == 15);
        Assert.DoesNotContain(breakdown.Items, i => i.Reason == "Hedef sektör");
    }

    [Fact]
    public void Taslak_eslemesi_profil_segmentlerini_listeler_SAP_anahtarlarini_yalnizca_SAP_profilinde()
    {
        var keys = EmailTemplate.AiKeysFor(PipeSupplier()).Select(k => k.Key).ToList();

        Assert.Equal(new[] { "GENEL", "SEG:tesisat1", "TAKIP" }, keys);
        Assert.True(EmailTemplate.IsKnownKey("SEG:tesisat1", PipeSupplier()));
        Assert.False(EmailTemplate.IsKnownKey("SEG:yok", PipeSupplier()));

        var sapKeys = EmailTemplate.AiKeysFor(new BusinessProfile { Offering = "SAP", IdealCustomer = "Fabrika" }).Select(k => k.Key);
        Assert.Contains("SAP_ENTEGRASYON", sapKeys);
        Assert.Equal(EmailTemplate.AiKeys.Length, EmailTemplate.AiKeysFor(null).Count);
    }

    [Fact]
    public void Profilden_taslak_yaniti_bilinmeyen_ve_tekrar_eden_anahtarlari_almaz()
    {
        var keys = GeminiAiService.TemplateKeysFor(PipeSupplier()).Select(k => k.Key).ToList();
        Assert.Equal(new[] { "GENEL", "SEG:tesisat1", "TAKIP" }, keys);

        var json = """
            {"templates":[
              {"key":"GENEL","name":"Genel Tanıtım","subject":"Boru ve vana tedariki","body":"Sayın {CONTACT_NAME}, ..."},
              {"key":"GENEL","name":"Tekrar","subject":"x","body":"y"},
              {"key":"SAP","name":"Yanlış","subject":"x","body":"y"},
              {"key":"SEG:tesisat1","name":"Mekanik tesisat","subject":"Tesisat projeleriniz için","body":"..."},
              {"key":"TAKIP","name":"Takip","subject":"","body":"boş konu"}
            ]}
            """;
        var result = GeminiAiService.ParseTemplateDrafts(json, keys);

        Assert.Null(result.Error);
        Assert.Equal(new[] { "GENEL", "SEG:tesisat1" }, result.Templates.Select(t => t.Key));
        Assert.Contains("{CONTACT_NAME}", GeminiAiService.TemplateWriterPrompt(PipeSupplier()));
    }
}
