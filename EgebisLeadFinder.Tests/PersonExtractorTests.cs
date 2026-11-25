using EgebisLeadFinder.Services;

namespace EgebisLeadFinder.Tests;

public class PersonExtractorTests
{
    [Fact]
    public void Ayni_satirdaki_isim_ve_unvani_bulur()
    {
        var text = """
            Yönetim Kadromuz
            Ahmet Yılmaz - Bilgi Teknolojileri Müdürü
            Mehmet Demir - Üretim Müdürü
            """;

        var people = PersonExtractor.Extract(text);

        Assert.Contains(people, p => p.Name == "Ahmet Yılmaz" && p.Title.Contains("Bilgi Teknolojileri Müdürü"));
        Assert.Contains(people, p => p.Name == "Mehmet Demir" && p.Title.Contains("Üretim Müdürü"));
    }

    [Fact]
    public void Isim_ve_unvan_ayri_satirlardaysa_eslestirir()
    {
        var text = """
            Ekibimiz
            Ayşe Kaya
            IT Manager
            """;

        var people = PersonExtractor.Extract(text);

        Assert.Contains(people, p => p.Name == "Ayşe Kaya");
    }

    [Fact]
    public void Menu_bloklarini_kisi_saymaz()
    {
        // Duz metne cevrilen menuler bitisik basliklar halinde gelir.
        var text = "HakkımızdaTarihçeKurucuCEO’nun MesajıVizyonMisyonDeğerler";

        var people = PersonExtractor.Extract(text);

        Assert.Empty(people);
    }

    [Fact]
    public void Isimsiz_unvan_satirini_atlar()
    {
        var text = """
            Kurumsal
            CEO’nun Mesajı
            """;

        var people = PersonExtractor.Extract(text);

        Assert.Empty(people);
    }

    [Fact]
    public void Orta_uzunluktaki_satirda_unvani_kisaltir()
    {
        // 60-120 karakter arasi satirlar okunur ama unvan alanina tamami yazilmaz.
        var text = "Ahmet Yılmaz 2015 yılından beri bilgi işlem müdürü olarak görev yapıyor.";

        var people = PersonExtractor.Extract(text);

        var person = Assert.Single(people);
        Assert.Equal("Ahmet Yılmaz", person.Name);
        Assert.True(person.Title.Length <= 60, $"Unvan kısaltılmalıydı: '{person.Title}'");
        Assert.Contains("bilgi işlem müdürü", person.Title);
    }

    [Fact]
    public void Uzun_paragraflari_kisi_kaynagi_saymaz()
    {
        // 120 karakteri asan satirlar metin govdesidir, kisi listesi degil.
        var text = "Şirketimizin kurucularından Ahmet Yılmaz uzun yıllardır bilgi işlem müdürü olarak görev yapmakta ve ekibi başarıyla yönetmektedir.";

        var people = PersonExtractor.Extract(text);

        Assert.Empty(people);
    }

    [Fact]
    public void Site_dugmesi_isim_cumle_de_unvan_sanilmaz()
    {
        // Gercek vaka (MEKA Global): "Sosyal Medyadan Ulaşın" kisi adi, yarim cumle unvan olarak kaydedilmisti.
        var text = """
            Sosyal Medyadan Ulaşın
            Halen Yönetim Kurulu Başkanı olarak MEKA’yı global ölçekte sürdürülebilir büyümeye taşıyor.
            Bize Ulaşın
            """;

        Assert.Empty(PersonExtractor.Extract(text));
    }

    [Fact]
    public void Cumle_icindeki_unvan_yalnizca_unvan_olarak_yazilir()
    {
        var text = """
            Ali Rıza Kaya
            Halen Yönetim Kurulu Başkanı olarak MEKA’yı global ölçekte sürdürülebilir büyümeye taşıyor.
            """;

        var person = Assert.Single(PersonExtractor.Extract(text));
        Assert.Equal("Ali Rıza Kaya", person.Name);
        Assert.Equal("Yönetim Kurulu Başkanı", person.Title);
    }

    [Fact]
    public void En_uzun_unvan_secilir()
    {
        var person = Assert.Single(PersonExtractor.Extract("Zeynep Arslan - Genel Müdür Yardımcısı"));
        Assert.Equal("Zeynep Arslan", person.Name);
        Assert.Contains("Genel Müdür Yardımcısı", person.Title);
    }

    [Theory]
    [InlineData("Sosyal Medyadan Ulaşın", false)]
    [InlineData("Bize Ulaşın", false)]
    [InlineData("Halen Yönetim Kurulu", false)]
    [InlineData("Meka Grup", false)]
    [InlineData("Ahmet Yılmaz", true)]
    [InlineData("Ayşe Nur Öztürk", true)]
    public void Isim_olabilirlik_kontrolu(string value, bool expected) =>
        Assert.Equal(expected, PersonExtractor.IsPlausibleName(value));
}
