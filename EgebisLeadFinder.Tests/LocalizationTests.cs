using System.Globalization;
using EgebisLeadFinder.Localization;

namespace EgebisLeadFinder.Tests;

/// <summary>Arayüz dili: Türkçe kaynak metin sözlükle İngilizce'ye çevrilir; kullanıcı verisine dokunulmaz.</summary>
[Collection("Culture")]
public class LocalizationTests
{
    static LocalizationTests()
    {
        Loc.Add(new Dictionary<string, string>
        {
            ["Firmalar"] = "Companies",
            ["Kaydet"] = "Save",
            ["{0} firma bulundu"] = "{0} companies found",
            ["Arama: {0}"] = "Search: {0}",
            ["{0} · {1}"] = "{0} · {1}",
            ["Türkiye"] = "Turkey",
            ["Plastik"] = "Plastics",
            ["Şirket profili kaydedildi ({0} segment)."] = "Company profile saved ({0} segments).",
            ["Eski firmaları güncellemek için yeniden analiz edin."] = "Re-analyse to update older companies.",
            ["Tamamlandı"] = "Completed",
            ["{0} seçili"] = "{0} selected"
        });
    }

    private static T InEnglish<T>(Func<T> action)
    {
        var old = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = new CultureInfo("en");
        try { return action(); }
        finally { CultureInfo.CurrentUICulture = old; }
    }

    private static T InTurkish<T>(Func<T> action)
    {
        var old = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = new CultureInfo("tr-TR");
        try { return action(); }
        finally { CultureInfo.CurrentUICulture = old; }
    }

    [Fact]
    public void Turkce_secili_iken_metin_degismez()
    {
        Assert.Equal("Firmalar", InTurkish(() => Loc.T("Firmalar")));
    }

    [Fact]
    public void Ingilizce_birebir_ceviri_ve_kenar_bosluklari_korunur()
    {
        Assert.Equal("Companies", InEnglish(() => Loc.T("Firmalar")));
        Assert.Equal("  Companies\n", InEnglish(() => Loc.T("  Firmalar\n")));
        Assert.Equal("Bilinmeyen metin", InEnglish(() => Loc.T("Bilinmeyen metin")));
    }

    [Fact]
    public void Sablon_degerleri_yakalar_ve_parcalari_ayrica_cevirir()
    {
        Assert.Equal("12 companies found", InEnglish(() => Loc.T("12 firma bulundu")));
        Assert.Equal("Search: Plastics · Turkey", InEnglish(() => Loc.T("Arama: Plastik · Türkiye")));
    }

    [Fact]
    public void Cok_cumleli_mesaj_cumle_cumle_cevrilir()
    {
        var result = InEnglish(() => Loc.T("Şirket profili kaydedildi (3 segment). Eski firmaları güncellemek için yeniden analiz edin."));
        Assert.Equal("Company profile saved (3 segments). Re-analyse to update older companies.", result);
    }

    [Fact]
    public void Html_metin_dugumleri_ve_basliklar_cevrilir_form_degerleri_degismez()
    {
        const string html = "<html lang=\"tr\"><body><h1>Firmalar</h1><input name=\"Kaydet\" value=\"Kaydet\" placeholder=\"Kaydet\">" +
                            "<option value=\"Plastik\">Plastik</option><textarea>Kaydet</textarea><p>Tamamlandı &amp; Kaydet</p></body></html>";

        var result = InEnglish(() => MarkupTranslator.TranslateHtml(html));

        Assert.Contains("lang=\"en\"", result);
        Assert.Contains("<h1>Companies</h1>", result);
        Assert.Contains("value=\"Kaydet\"", result);          // formla gonderilen deger korunur
        Assert.Contains("placeholder=\"Save\"", result);
        Assert.Contains("<option value=\"Plastik\">Plastics</option>", result);
        Assert.Contains("<textarea>Kaydet</textarea>", result); // kullanici metni
    }

    [Fact]
    public void Duzenlenebilir_alanlar_ve_translate_no_icerigi_cevrilmez()
    {
        const string html = "<div contenteditable=\"true\"><p>Kaydet</p><div>Kaydet</div></div><p>Kaydet</p>" +
                            "<span translate=\"no\">Kaydet</span><p>Firmalar</p>";

        var result = InEnglish(() => MarkupTranslator.TranslateHtml(html));

        Assert.Equal("<div contenteditable=\"true\"><p>Kaydet</p><div>Kaydet</div></div><p>Save</p>" +
                     "<span translate=\"no\">Kaydet</span><p>Companies</p>", result);
    }

    [Fact]
    public void Js_dizgeleri_ve_sablon_dizgeleri_cevrilir()
    {
        var js = "toast('Tamamlandı'); const a = \"Firmalar\"; const n = `${count} seçili`; const k = 'Plastik';";
        var result = InEnglish(() => MarkupTranslator.TranslateJs(js));

        Assert.Equal("toast('Completed'); const a = \"Companies\"; const n = `${count} selected`; const k = 'Plastics';", result);
    }

    [Fact]
    public void Json_degerleri_cevrilir_anahtarlar_degismez()
    {
        var result = InEnglish(() => MarkupTranslator.TranslateJson("{\"Firmalar\":\"Firmalar\",\"stage\":\"Tamamlandı\",\"x\":\"başka\"}"));

        Assert.Contains("\"Firmalar\":\"Companies\"", result);
        Assert.Contains("\"stage\":\"Completed\"", result);
        Assert.Contains("\"x\":\"başka\"", result);
    }

    [Fact]
    public void Yapay_zeka_istemi_yalniz_ingilizcede_uyarlanir()
    {
        const string prompt = "Kurallar:\n- \"Sayın {CONTACT_NAME},\" ile başla.\n- Tüm metin alanlarını Türkçe yaz.";

        Assert.Equal(prompt, InTurkish(() => Loc.Prompt(prompt)));

        var english = InEnglish(() => Loc.Prompt(prompt));
        Assert.Contains("Tüm metin alanlarını İngilizce yaz", english);
        Assert.Contains("\"Dear {CONTACT_NAME},\" ile başla", english);
        Assert.Contains("ÇIKTI DİLİ: İngilizce", english);
    }

    [Fact]
    public void Dil_kodu_yalniz_tr_ya_da_en_olur()
    {
        Assert.Equal("en", Loc.NormalizeCode("EN"));
        Assert.Equal("tr", Loc.NormalizeCode("de"));
        Assert.Equal("tr", Loc.NormalizeCode(null));
    }
}

[CollectionDefinition("Culture", DisableParallelization = true)]
public class CultureCollection { }
