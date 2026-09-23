using EgebisLeadFinder.Services;

namespace EgebisLeadFinder.Tests;

public class LinkedInRelevanceFilterTests
{
    [Theory]
    [InlineData("Toksan Otomotiv A.Ş.", "Toksan")]
    [InlineData("Beyçelik Gestamp Otomotiv Sanayi A.Ş.", "Beyçelik")]
    [InlineData("Valeo", "Valeo")]
    [InlineData("Y.S.L. Otomotiv Yan San. A.Ş", "Y.S.L")]
    public void Ayirt_edici_kelimeyi_dogru_secer(string companyName, string expected)
    {
        Assert.Equal(expected, LinkedInRelevanceFilter.ExtractDistinctiveKeyword(companyName));
    }

    [Fact]
    public void Tum_kelimeler_jenerikse_null_doner()
    {
        Assert.Null(LinkedInRelevanceFilter.ExtractDistinctiveKeyword("Sanayi Ticaret A.Ş."));
    }

    [Fact]
    public void Bos_firma_adinda_null_doner()
    {
        Assert.Null(LinkedInRelevanceFilter.ExtractDistinctiveKeyword(""));
        Assert.Null(LinkedInRelevanceFilter.ExtractDistinctiveKeyword(null));
    }

    [Fact]
    public void Gercek_vaka_alakasiz_kisi_elenir()
    {
        // Google "site:linkedin.com/in/ \"Toksan Otomotiv A.Ş.\" (...\"SAP\"...)" sorgusuna
        // Toksan ile hicbir ilgisi olmayan, sadece "SAP" sertifikasi olan birini dondurmustu.
        var mentions = LinkedInRelevanceFilter.MentionsCompany(
            "Toksan Otomotiv A.Ş.",
            "Erdem Bulut - Freelance SAP PS PPM Senior Consultant",
            "Lisanslar ve Sertifikalar. Project System with SAP ERP 6.0 EHP4. SAP. Eki 2017 tarihinde verildi.");

        Assert.False(mentions);
    }

    [Fact]
    public void Firma_adi_ozette_geciyorsa_kabul_eder()
    {
        var mentions = LinkedInRelevanceFilter.MentionsCompany(
            "Toksan Otomotiv A.Ş.",
            "Buse Var - Industrial Engineering",
            "Industrial Engineering · Toksan Otomotiv A.Ş. Üretim Planlama ve Kontrol Uzman Yardımcısı");

        Assert.True(mentions);
    }

    [Fact]
    public void Firma_adi_baslikta_geciyorsa_kabul_eder()
    {
        var mentions = LinkedInRelevanceFilter.MentionsCompany(
            "Valeo", "Ahmet Yılmaz - CTO - Valeo Türkiye", "IT ve SAP süreçlerinden sorumlu.");

        Assert.True(mentions);
    }

    [Fact]
    public void Turkce_karakter_farki_eslesmeyi_engellemez()
    {
        var mentions = LinkedInRelevanceFilter.MentionsCompany(
            "Beyçelik Gestamp Otomotiv Sanayi A.Ş.",
            "Ahmet Yılmaz - IT Müdürü",
            "beycelik gestamp bünyesinde IT müdürü olarak görev yapıyor."); // aksansiz yazilmis

        Assert.True(mentions);
    }

    [Fact]
    public void Ayirt_edici_kelime_bulunamiyorsa_bloklamaz()
    {
        // Firma adi tamamen jenerik kelimelerden olusuyorsa dogrulama yapilamaz;
        // yanlislikla gecerli adaylari elememek icin gecirilir.
        var mentions = LinkedInRelevanceFilter.MentionsCompany(
            "Sanayi Ticaret A.Ş.", "Ahmet Yılmaz - CTO", "Alakasız bir özet metni.");

        Assert.True(mentions);
    }
}
