using System.Text.Json;
using EgebisLeadFinder.Models;
using EgebisLeadFinder.Services.CompanyIntel;
using Xunit;

namespace EgebisLeadFinder.Tests;

public class SnippetRelevanceTests
{
    private static Company Ugur() => new()
    {
        Name = "Uğur",
        Domain = "ugur.com.tr",
        Industry = "Beyaz Eşya ve Ev Aletleri Üretimi",
        AiAnalysis = JsonSerializer.Serialize(new CompanyAnalysis { Products = { "Derin Dondurucu", "Buzdolabı", "Klima" } })
    };

    [Fact]
    public void Another_company_with_the_same_first_name_is_rejected()
    {
        // Gercek olay: bu ilan "UGC UĞUR GÜRBÜZ CIVATA" icin; Uğur Soğutma'ya "Riskli" kaynak diye eklenmisti.
        const string ilan = "Konkordato kesin mühletin kaldırılması. Davacılar AYBERK GÜRBÜZ, UĞUR GÜRBÜZ, UGC UĞUR GÜRBÜZ CIVATA ANONİM ŞİRKETİ konkordato talebinin kabulü";

        Assert.False(SnippetRelevance.IsAbout(Ugur(), ilan, "https://ilan.gov.tr/ilan/123"));
    }

    [Fact]
    public void News_with_a_company_specific_clue_is_accepted()
    {
        Assert.True(SnippetRelevance.IsAbout(Ugur(), "Uğur Soğutma yeni derin dondurucu yatırımı yaptı", "https://haber.example/1"));
        Assert.True(SnippetRelevance.IsAbout(Ugur(), "Uğur beyaz eşya ihracatını artırdı", "https://haber.example/2"));
    }

    [Fact]
    public void Own_site_or_a_mention_of_the_domain_is_always_accepted()
    {
        Assert.True(SnippetRelevance.IsAbout(Ugur(), "Sürdürülebilirlik raporu", "https://cdn.ugur.com.tr/rapor.pdf"));
        Assert.True(SnippetRelevance.IsAbout(Ugur(), "Kullanım kılavuzu için ugur.com.tr adresini ziyaret edin", "https://x.example"));
    }

    [Fact]
    public void A_result_that_does_not_mention_the_name_is_rejected()
    {
        Assert.False(SnippetRelevance.IsAbout(Ugur(), "Derin dondurucu fiyatları ve modelleri", "https://x.example"));
    }

    [Fact]
    public void A_multi_word_name_needs_no_extra_clue()
    {
        var company = new Company { Name = "Trakya Cam Sanayii A.Ş.", Domain = "trakyacam.com.tr" };

        Assert.True(SnippetRelevance.IsAbout(company, "Trakya Cam yeni fırın yatırımı", "https://haber.example"));
        Assert.False(SnippetRelevance.IsAbout(company, "Trakya bölgesinde cam fiyatları", "https://haber.example"));
    }

    [Fact]
    public void Name_inside_a_longer_word_does_not_count()
    {
        Assert.False(SnippetRelevance.IsAbout(Ugur(), "Uğurlar olsun derin dondurucu kampanyası", "https://x.example"));
    }
}
