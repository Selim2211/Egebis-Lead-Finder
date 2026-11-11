using EgebisLeadFinder.Models;
using EgebisLeadFinder.Services;
using EgebisLeadFinder.Services.CompanyIntel;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace EgebisLeadFinder.Tests;

/// <summary>Canliya cikis oncesi kod incelemesinde bulunan hatalarin duzeltmelerini kanitlar.</summary>
public class LaunchFixesTests
{
    // ---- Firma adi anahtari: "sti/tic/san/ltd" firma adinin icinde gecince adi bozmamali

    [Theory]
    [InlineData("Eren Plastik", "eren plastik")]
    [InlineData("Arsan Tekstil", "arsan tekstil")]
    [InlineData("Aksan Ltd. Şti.", "aksan")]
    [InlineData("Elastik Sanayi ve Ticaret A.Ş.", "elastik")]
    [InlineData("Trakya Cam Sanayii A.Ş.", "trakya cam")]
    [InlineData("TRAKYA CAM SANAYİİ A.Ş.", "trakya cam")]
    public void Company_name_key_removes_only_whole_noise_words(string name, string expected) =>
        Assert.Equal(expected, CompanyNameMatch.Key(name));

    [Fact]
    public void News_about_a_company_whose_name_contains_noise_letters_is_still_matched()
    {
        var company = new Company { Name = "Eren Plastik", Domain = "erenplastik.com.tr", Industry = "Plastik enjeksiyon" };

        Assert.True(SnippetRelevance.IsAbout(company, "Eren Plastik yeni fabrika yatırımı yaptı", "https://haber.example/1"));
    }

    // ---- Engelli site listesi: alan adi kaydi yalnizca o alan adini ve alt alanlarini kapsar

    [Theory]
    [InlineData("polyflex.com.tr", false)]
    [InlineData("alfaflex.com.tr", false)]
    [InlineData("tekfix.com.tr", false)]
    [InlineData("x.com", true)]
    [InlineData("mobile.x.com", true)]
    public void A_domain_entry_does_not_block_other_sites_that_merely_contain_it(string domain, bool blocked) =>
        Assert.Equal(blocked, DomainHelper.IsBlocked(domain, new[] { "x.com" }));

    [Theory]
    [InlineData("haberler.com", true)]
    [InlineData("ebay.com.tr", true)]
    [InlineData("ornek.com", false)]
    public void Keyword_entries_without_an_extension_still_match_as_parts(string domain, bool blocked) =>
        Assert.Equal(blocked, DomainHelper.IsBlocked(domain, new[] { "haber", "ebay." }));

    [Fact]
    public void Blacklist_wildcard_and_single_word_name_block_the_site()
    {
        var list = SearchBlacklist.Parse("*.ilan-sitesi.com\nTolon");

        Assert.Contains("ilan-sitesi.com", list.Domains);
        Assert.True(list.MatchesDomain("shop.ilan-sitesi.com"));
        Assert.True(list.MatchesDomain("tolon.com.tr"));   // tek kelimelik ad = alan adinin ana etiketi
        Assert.False(list.MatchesDomain("tolonya.com"));
    }

    // ---- Oturum denetimi "statik dosya" hilesiyle atlanamaz

    [Theory]
    [InlineData("/css/site.css", true)]
    [InlineData("/js/app.js", true)]
    [InlineData("/lib/bootstrap/dist/js/x.js", true)]
    [InlineData("/favicon.ico", true)]
    [InlineData("/Users/Index/x.js", false)]
    [InlineData("/Settings/Index/x.css", false)]
    [InlineData("/Audit/Index/y.map", false)]
    [InlineData("/Users/Create/z.png", false)]
    [InlineData("/Users", false)]
    public void Only_real_static_paths_skip_session_validation(string path, bool isStatic) =>
        Assert.Equal(isStatic, StaticPaths.IsStatic(new PathString(path)));

    // ---- "Kanit bulunamadi" notu alinti sayilmaz

    [Fact]
    public void The_no_evidence_note_is_never_accepted_as_a_site_quote()
    {
        var brief = IcpService.EvidenceBrief(new Dictionary<Company, EgebisLeadFinder.Services.ProducerEvidence>(), new Company { Name = "X" })
                    + " | Sitede somut üretim kanıtı bulunamadı";

        Assert.Null(IcpService.VerifyQuote("Sitede somut üretim kanıtı bulunamadı", brief));
        Assert.Equal("Kapalı alanı 5.000 m² olan fabrikamız", IcpService.VerifyQuote("«Kapalı alanı 5.000 m² olan fabrikamız»", brief + " | Kapalı alanı 5.000 m² olan fabrikamız"));
    }
}
