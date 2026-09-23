using EgebisLeadFinder.Models;
using EgebisLeadFinder.Services;

namespace EgebisLeadFinder.Tests;

public class ContactMergerTests
{
    [Fact]
    public void Ayni_kisi_tek_kayda_indirilir()
    {
        var existing = new List<Contact> { new() { Name = "Ahmet Yılmaz", TitleScore = 50 } };
        var incoming = new List<Contact> { new() { Name = "ahmet yılmaz", TitleScore = 90 } };

        var merged = ContactMerger.Merge(existing, incoming);

        Assert.Single(merged);
        Assert.Equal(90, merged[0].TitleScore);
    }

    [Fact]
    public void Farkli_kisiler_ayri_kalir()
    {
        var existing = new List<Contact> { new() { Name = "Ahmet Yılmaz", TitleScore = 90 } };
        var incoming = new List<Contact> { new() { Name = "Ayşe Kaya", TitleScore = 80 } };

        var merged = ContactMerger.Merge(existing, incoming);

        Assert.Equal(2, merged.Count);
        // Unvan puanina gore siralanir.
        Assert.Equal("Ahmet Yılmaz", merged[0].Name);
    }

    [Fact]
    public void Eposta_eski_kayittan_tam_unvan_yeni_kayittan_alinir()
    {
        // Asil senaryo: eski kayitta e-posta var ama unvani kirpilmis; yeni
        // kayitta unvan tam ama e-posta yok. Ikisi de korunmali.
        var existing = new List<Contact>
        {
            new()
            {
                Name = "Emrah Arslan",
                Title = "TRAKYA DÖKÜM SAN. TİC. A.Ş şirketinde Sap ...",
                Email = "emrah@trakyadokum.com.tr",
                TitleScore = 90
            }
        };

        var incoming = new List<Contact>
        {
            new() { Name = "Emrah Arslan", Title = "SAP Danışmanı", Email = null, TitleScore = 90 }
        };

        var merged = ContactMerger.Merge(existing, incoming);

        var contact = Assert.Single(merged);
        Assert.Equal("emrah@trakyadokum.com.tr", contact.Email);
        Assert.Equal("SAP Danışmanı", contact.Title);
    }

    [Fact]
    public void Kirpilmamis_unvan_daha_kisa_olsa_bile_tercih_edilir()
    {
        var existing = new List<Contact>
        {
            new() { Name = "Ali Veli", Title = "Çok Uzun Bir Kırpılmış Ünvan Metni ...", TitleScore = 60 }
        };
        var incoming = new List<Contact>
        {
            new() { Name = "Ali Veli", Title = "CTO", TitleScore = 95 }
        };

        var merged = ContactMerger.Merge(existing, incoming);

        Assert.Equal("CTO", Assert.Single(merged).Title);
    }

    [Fact]
    public void Ikisi_de_kirpilmamissa_daha_uzun_unvan_secilir()
    {
        var existing = new List<Contact> { new() { Name = "Ali Veli", Title = "Müdür", TitleScore = 60 } };
        var incoming = new List<Contact>
        {
            new() { Name = "Ali Veli", Title = "Bilgi İşlem Müdürü", TitleScore = 95 }
        };

        var merged = ContactMerger.Merge(existing, incoming);

        Assert.Equal("Bilgi İşlem Müdürü", Assert.Single(merged).Title);
    }

    [Fact]
    public void Eksik_telefon_ve_profil_adresi_digerinden_tamamlanir()
    {
        var existing = new List<Contact>
        {
            new() { Name = "Ali Veli", Email = "ali@test.com", Phone = null, SourceUrl = null, TitleScore = 90 }
        };
        var incoming = new List<Contact>
        {
            new()
            {
                Name = "Ali Veli",
                Phone = "+90 555 111 22 33",
                SourceUrl = "https://linkedin.com/in/ali",
                TitleScore = 90
            }
        };

        var contact = Assert.Single(ContactMerger.Merge(existing, incoming));

        Assert.Equal("ali@test.com", contact.Email);
        Assert.Equal("+90 555 111 22 33", contact.Phone);
        Assert.Equal("https://linkedin.com/in/ali", contact.SourceUrl);
    }

    [Fact]
    public void ApolloId_ayniysa_isim_farkli_olsa_da_tek_kayda_iner()
    {
        // "api_search" ayni kisiyi her cagrida obfuscated isimle dondurur
        // ("Eren Ak***y"); e-posta acildiktan sonra Name gercek isimle degisir
        // ("Eren Aksoy"). Isme gore gruplasaydik bu ikinci "Lead'leri Bul"da
        // mukerrer kayit olurdu; ApolloId kimligi sabit kaldigi icin olmamali.
        var existing = new List<Contact>
        {
            new() { Name = "Eren Aksoy", Email = "eren@test.com", ApolloId = "abc123", TitleScore = 90 }
        };
        var incoming = new List<Contact>
        {
            new() { Name = "Eren Ak***y", ApolloId = "abc123", TitleScore = 90 }
        };

        var merged = ContactMerger.Merge(existing, incoming);

        var contact = Assert.Single(merged);
        Assert.Equal("Eren Aksoy", contact.Name);
        Assert.Equal("eren@test.com", contact.Email);
    }

    [Fact]
    public void Azami_kisi_sinirinin_uzeri_kesilir()
    {
        // Apollo listelemesi kredisiz oldugu icin sinir yuksek tutulur (60);
        // burada sinirin var oldugunu ve en yuksek puanlilarin kaldigini dogrularz.
        var existing = Enumerable.Range(1, 40)
            .Select(i => new Contact { Name = $"Kişi {i}", TitleScore = i })
            .ToList();

        var incoming = Enumerable.Range(41, 40)
            .Select(i => new Contact { Name = $"Kişi {i}", TitleScore = i })
            .ToList();

        var merged = ContactMerger.Merge(existing, incoming);

        Assert.Equal(60, merged.Count);
        // En yuksek unvan puanlilar tutulur.
        Assert.Equal(80, merged[0].TitleScore);
    }
}
