using EgebisLeadFinder.Services;

namespace EgebisLeadFinder.Tests;

public class LinkedInTitleParserTests
{
    [Fact]
    public void Tam_formati_ayristirir()
    {
        var profile = LinkedInTitleParser.Parse(
            "Ahmet Yılmaz - Bilgi İşlem Müdürü - Beyçelik Gestamp | LinkedIn",
            "https://www.linkedin.com/in/ahmet-yilmaz");

        Assert.NotNull(profile);
        Assert.Equal("Ahmet Yılmaz", profile!.Name);
        Assert.Equal("Bilgi İşlem Müdürü", profile.Title);
        Assert.Equal("Beyçelik Gestamp", profile.Company);
        Assert.Equal("https://www.linkedin.com/in/ahmet-yilmaz", profile.ProfileUrl);
    }

    [Fact]
    public void Firma_kismi_eksikse_null_doner()
    {
        var profile = LinkedInTitleParser.Parse("Ahmet Yılmaz - IT Manager | LinkedIn", "url");

        Assert.NotNull(profile);
        Assert.Equal("IT Manager", profile!.Title);
        Assert.Null(profile.Company);
    }

    [Fact]
    public void Sadece_isim_varsa_unvan_null_doner()
    {
        var profile = LinkedInTitleParser.Parse("Ahmet Yılmaz | LinkedIn", "url");

        Assert.NotNull(profile);
        Assert.Null(profile!.Title);
        Assert.Null(profile.Company);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Bos_baslikta_null_doner(string? title)
    {
        Assert.Null(LinkedInTitleParser.Parse(title, "url"));
    }

    [Fact]
    public void Iki_kelimelik_ismi_ad_soyad_olarak_boler()
    {
        var profile = LinkedInTitleParser.Parse("Ahmet Yılmaz - IT Müdürü | LinkedIn", "url")!;

        var (first, last) = profile.SplitName();

        Assert.Equal("Ahmet", first);
        Assert.Equal("Yılmaz", last);
    }

    [Fact]
    public void Tek_kelimelik_isimde_soyad_bos_doner()
    {
        var profile = LinkedInTitleParser.Parse("Ahmet | LinkedIn", "url")!;

        var (first, last) = profile.SplitName();

        Assert.Equal("Ahmet", first);
        Assert.Equal(string.Empty, last);
    }

    [Fact]
    public void Uc_kelimelik_isimde_soyad_kalan_kismi_alir()
    {
        // "Ahmet Kemal Yılmaz" -> ilk kelime ad, kalani soyad. Apollo'ya gonderilecek
        // deger tam dogru olmayabilir ama en azindan bos donmez.
        var profile = LinkedInTitleParser.Parse("Ahmet Kemal Yılmaz - CTO | LinkedIn", "url")!;

        var (first, last) = profile.SplitName();

        Assert.Equal("Ahmet", first);
        Assert.Equal("Kemal Yılmaz", last);
    }

    // ----- Kirpilmis unvani ozetten tamamlama -----

    [Fact]
    public void Kirpilmis_unvan_ozetten_tamamlanir()
    {
        // Gercek vaka: Google basligi kirpip "..." koymus, tam hali ozette var.
        var profile = LinkedInTitleParser.Parse(
            "Emrah Arslan - TRAKYA DÖKÜM SAN. TİC. A.Ş şirketinde Sap ... | LinkedIn",
            "url",
            "TRAKYA DÖKÜM SAN. TİC. A.Ş şirketinde Sap Danışmanı · Deneyim: 8 yıl")!;

        Assert.Equal("Sap Danışmanı", profile.Title);
        Assert.DoesNotContain("...", profile.Title);
    }

    [Fact]
    public void Gercek_vaka_firma_onekli_kirpik_unvan_ozetten_tamamlanir()
    {
        // Trakya Döküm / Emrah Arslan — Serper'in donduruldugu gercek veri.
        // Baslikta firma adi unvanin onune yapismis ve unvan kirpilmis; ozetin
        // yapisi ise tamamen farkli. Once firma oneki ayrilmali, sonra kalan
        // cekirdek ("Sap") ozette aranmali.
        var profile = LinkedInTitleParser.Parse(
            "Emrah Arslan - TRAKYA DÖKÜM SAN. TİC. A.Ş şirketinde Sap ...",
            "url",
            "Deneyim ; Sap Advanced Business Application Programming Developer. " +
            "TRAKYA DÖKÜM SAN. TİC. A.Ş. Eyl 2017 ; Oracle Database Administrator. InspireIT Information ...")!;

        Assert.Equal("Emrah Arslan", profile.Name);
        Assert.Equal("Sap Advanced Business Application Programming Developer", profile.Title);
    }

    [Fact]
    public void Ozetteki_ellipsis_sonrasi_metin_unvana_yapistirilmaz()
    {
        // Gercek vaka: ORAU / Yusuf Aksoy. Ozet de kirpik oldugu icin kurtarma
        // devamindaki sertifika cumlelerini unvan sanmisti. Ellipsis sinir sayilinca
        // kurtarma basarisiz olur ve kirpik hali korunur — uydurma metin uretilmez.
        var profile = LinkedInTitleParser.Parse(
            "Yusuf Aksoy - Kıdemli Kalite ...",
            "url",
            "Kıdemli Kalite ... SAP PP Module. Orhan Holding. Kas 2018 tarihinde verildi. Kariyer Zirvesi")!;

        Assert.Equal("Kıdemli Kalite ...", profile.Title);
        Assert.DoesNotContain("Orhan Holding", profile.Title);
        Assert.DoesNotContain("Kariyer Zirvesi", profile.Title);
    }

    [Fact]
    public void Tamamen_kirpilmis_baslikta_unvan_null_doner()
    {
        // Gercek vaka: Google basligi tamamen kirpmis, geriye sadece "..." kalmis.
        // Bu bir unvan degildir; aday unvansiz sayilip elenmeli.
        var profile = LinkedInTitleParser.Parse("Semih Güney - ...", "url", "Alakasız özet")!;

        Assert.Equal("Semih Güney", profile.Name);
        Assert.Null(profile.Title);
    }

    [Fact]
    public void Kurtarma_kelime_ortasinda_eslesmez()
    {
        // "Sap" cekirdegi "Sapanca" icinde gecse de unvan oradan tamamlanmamali.
        var profile = LinkedInTitleParser.Parse(
            "Ali Veli - Sap ...", "url",
            "Konum: Sapanca, Sakarya bölgesinde yaşıyor.")!;

        Assert.Equal("Sap ...", profile.Title);
    }

    [Fact]
    public void Cok_uzun_kurtarma_reddedilir()
    {
        // Cumlecik siniri yakalanamazsa unvan yerine koca bir cumle kaydedebilirdik.
        var longSentence = "Sap " + string.Join(" ", Enumerable.Repeat("kelime", 40));

        var profile = LinkedInTitleParser.Parse("Ali Veli - Sap ...", "url", longSentence)!;

        Assert.Equal("Sap ...", profile.Title);
    }

    [Fact]
    public void Ozette_bulunamazsa_kirpilmis_hali_korunur()
    {
        // Uydurma yapilmaz: ozet eslesmiyorsa elde ne varsa o kalir.
        var profile = LinkedInTitleParser.Parse(
            "Emrah Arslan - Kıdemli Sistem ... | LinkedIn",
            "url",
            "Tamamen alakasız bir özet metni.")!;

        Assert.Equal("Kıdemli Sistem ...", profile.Title);
    }

    [Fact]
    public void Ozet_verilmezse_baslik_oldugu_gibi_kalir()
    {
        var profile = LinkedInTitleParser.Parse("Emrah Arslan - Sap ... | LinkedIn", "url")!;

        Assert.Equal("Sap ...", profile.Title);
    }

    // ----- "<Firma> şirketinde <Ünvan>" kalibi -----

    [Fact]
    public void Sirketinde_kalibinda_sadece_unvan_alinir()
    {
        // Listede kisinin gorevi okunabilsin diye firma adi unvanin onunden ayrilir.
        var profile = LinkedInTitleParser.Parse(
            "Ayşe Kaya - Beyçelik Gestamp şirketinde Bilgi İşlem Müdürü | LinkedIn", "url")!;

        Assert.Equal("Bilgi İşlem Müdürü", profile.Title);
        Assert.Equal("Beyçelik Gestamp", profile.Company);
    }

    [Fact]
    public void Sirketinde_kalibi_yoksa_unvan_degismez()
    {
        var profile = LinkedInTitleParser.Parse(
            "Ayşe Kaya - Bilgi İşlem Müdürü - Beyçelik | LinkedIn", "url")!;

        Assert.Equal("Bilgi İşlem Müdürü", profile.Title);
        Assert.Equal("Beyçelik", profile.Company);
    }
}
