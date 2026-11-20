using EgebisLeadFinder.Data;
using EgebisLeadFinder.Models;
using EgebisLeadFinder.Services.News;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EgebisLeadFinder.Tests;

public class NewsSimilarityTests
{
    private static readonly DateTime T0 = new(2026, 11, 11, 8, 0, 0, DateTimeKind.Utc);

    // Gerçek akıştan alınmış başlıklar (Id, saat farkı, başlık, özet).
    private static readonly (long Id, int H, string Title, string Summary)[] Real =
    {
        (1, -40, "Honda'dan ABD'de 3 milyar dolarlık yeni fabrika yatırımı", ""),
        (2, -22, "Honda'dan ABD'de binlerce kişiye istihdam sağlayacak yeni fabrika yatırımı", ""),
        (3, -46, "Uludağ İçecek'ten Malatya'ya dev fabrika yatırımı! 110'un üzerinde kişiye istihdam sağlayacak", ""),
        (4, -32, "Honda, ABD'de yeni bir fabrika açacak ve 3 milyar dolarlık yatırım yapacağını duyurdu.", ""),
        (5, -22, "Honda ABD’de 3 milyar dolarlık hibrit araç fabrikası kuracağını açıkladı", "Honda, ABD'de 3 milyar dolarlık hibrit araç fabrikası kuracağını duyurdu."),
        (7, 2, "malatya'ya aselsan üssü: 66 dönüm, 5 fabrika ve yeni sanayi ekosistemi", "ASELSAN, Malatya’da 66 dönüm alana 5 fabrika ve yeni bir sanayi ekosistemi kuruyor."),
        (8, 2, "Malatya’da ASELSAN devri başlıyor! 5 fabrika için tarih belli oldu", "Malatya’da ASELSAN’ın 5 fabrika için devri gerçekleşti, tarih belirlendi."),
        (9, 3, "ASELSAN’dan 5 fabrikalık yatırım! Cumhurbaşkanı Erdoğan talimatı verdi", "ASELSAN, 5 yeni fabrika yatırımıyla kapasitesini artırıyor."),
        (10, 3, "ASELSAN'dan Malatya'ya dev yatırım: 66 dönüm alana 5 fabrika kurulacak", ""),
        (11, 6, "Cumhurbaşkanı Erdoğan talimat verdi: ASELSAN Malatya'ya 5 fabrika kuruyor", "Cumhurbaşkanı talimatıyla ASELSAN, Malatya’da 5 fabrika kuruyor."),
        (12, 6, "Bursa'ya 43,9 milyar TL'lik dev fabrika yatırımı! 744 kişiye yeni iş imkânı...", ""),
        (13, 7, "ASELSAN Malatya’ya 5 fabrika kuruyor: Erdoğan talimat verdi", "ASELSAN, Malatya’da 5 fabrika kurarak bölgeye büyük bir savunma sanayi yatırımı yapıyor."),
        (14, 9, "ASELSAN’dan Malatya’ya dev yatırım: Ankara’dan sonraki en büyük teknoloji üssü!", "ASELSAN Malatya’da büyük bir teknoloji üssü kuruyor, 5 fabrika ve yeni sanayi ekosistemi oluşturacak."),
        (15, 18, "Erdoğan talimatı verdi: ASELSAN'dan Malatya'ya 5 fabrikalık yatırım", ""),
        (16, 23, "ASELSAN'dan yeni yatırım müjdesi: 5 fabrika kurulacak!", ""),
        (17, 24, "Doğan Holding, Romanya’daki iştirakini 109 milyon euroya satıyor haberi", "Doğan Holding, Romanya’daki iştirakini 109 milyon Euro’ya satıyor."),
        (18, 25, "Güney Koreli bir gıda devi, Vietnam'da bir fabrika kurmak için 4,2 trilyon VND yatırım yapmaya hazırlanıyor.", ""),
        (19, 26, "Tesla Türkiye’ye Fabrika Kurar mı? BYD ve Chery İçin Fırsat-Risk Hesabı", ""),
        (20, 20, "Türkiye'nin Tekstil Devi İflas Etti! Çok Sayıda Ülkeye İhracat Yapıyordu!", ""),
        (21, -30, "Satışları düşen PepsiCo, yaklaşık 1000 kişinin işine son verecek", ""),
        (30, 30, "Alman otomotiv devinden kritik karar! Yatırımlar durduruldu, fabrikalar tehlikede", "Alman otomotiv devinden kritik karar! Yatırımlar durduruldu, fabrikalar tehlikede Bursada Bugün"),
        (31, 30, "Otomotiv devi Mercedes'ten şok karar! Yeni yatırımlar durduruldu: Fabrikalar kapanabilir", "Mercedes, yeni yatırımları durdurdu ve fabrikaların kapanma ihtimali ortaya çıktı."),
        (32, 33, "Alman otomotiv devi frene bastı! Tüm yeni yatırımlar durduruldu", "Alman otomotiv devi frene bastı! Tüm yeni yatırımlar durduruldu Bursa Hayat Gazetesi"),
        (33, 40, "Mercedes - Benz Almanya'daki tüm yeni yatırımları durdurdu: Fabrika kapatmaları gündemde", ""),
        (34, 42, "Dünyaca ünlü otomotiv devinden flaş karar! Yeni yatırımlar askıya alındı", "Dünyaca ünlü otomotiv devinden flaş karar! Yeni yatırımlar askıya alındı kayserihaber.com"),
        (35, 42, "Mercedes Almanya'da yatırımları durdurdu: İki fabrika riskte", ""),
        (36, 43, "Mercedes-Benz iki fabrikasını kapatmayı değerlendiriyor: Yeni yatırımlar askıya alındı", "")
    };

    private static List<StoryItem> Items() =>
        Real.Select(r => new StoryItem(r.Id, T0.AddHours(r.H), NewsSimilarity.Print(r.Title, r.Summary))).ToList();

    [Fact]
    public void Gercek_akista_ayni_olaylar_tek_grupta_toplanir_farkli_olaylar_karismaz()
    {
        var groups = NewsSimilarity.Clusters(Items())
            .Select(g => g.Select(s => s.Id).OrderBy(i => i).ToArray()).OrderBy(g => g[0]).ToList();

        Assert.Equal(3, groups.Count);
        Assert.Equal(new long[] { 1, 2, 4, 5 }, groups[0]);                               // Honda ABD fabrikası
        Assert.Equal(new long[] { 7, 8, 9, 10, 11, 13, 14, 15, 16 }, groups[1]);           // ASELSAN Malatya
        Assert.Equal(new long[] { 30, 31, 32, 33, 34, 35, 36 }, groups[2]);                // Mercedes: bazı başlıklarda şirket adı hiç geçmez
    }

    [Fact]
    public void Ayni_sehir_ve_fabrika_kelimesi_tek_basina_ayni_haber_yapmaz()
    {
        var items = Items();
        var idf = NewsSimilarity.Idf(items.Select(i => i.Print));
        StoryItem Get(long id) => items.Single(i => i.Id == id);
        Assert.False(NewsSimilarity.SameStory(Get(3), Get(10), idf));   // Uludağ Malatya ≠ ASELSAN Malatya
        Assert.False(NewsSimilarity.SameStory(Get(2), Get(3), idf));    // Honda istihdam ≠ Uludağ istihdam
        Assert.False(NewsSimilarity.SameStory(Get(12), Get(15), idf));  // Bursa yatırımı ≠ ASELSAN
    }

    [Theory]
    [InlineData("Otomotiv devi Mercedes'ten şok karar! Yeni yatırımlar durduruldu: Fabrikalar kapanabilir")]
    [InlineData("Dünyaca ünlü otomotiv devinden flaş karar! Yeni yatırımlar askıya alındı")]
    [InlineData("Mercedes-Benz iki fabrikasını kapatmayı değerlendiriyor: Yeni yatırımlar askıya alındı")]
    public void Yatirimi_durduran_fabrika_kapatan_haber_olumlu_sayilmaz(string title)
    {
        var (category, importance) = NewsRuleClassifier.Classify(title, new EgebisLeadFinder.Configuration.ResearchOptions());
        Assert.Equal(NewsCategories.Risk, category);
        Assert.Equal(4, importance);
        Assert.Equal(NewsCategories.Negative, NewsCategories.Sentiment(category));
    }

    [Fact]
    public void Yetmis_iki_saatten_uzak_haberler_ayni_sayilmaz()
    {
        var a = new StoryItem(1, T0, NewsSimilarity.Print("ASELSAN Malatya'ya 5 fabrika kuruyor", null));
        var b = new StoryItem(2, T0.AddDays(5), NewsSimilarity.Print("ASELSAN Malatya'ya 5 fabrika kuruyor", null));
        var idf = NewsSimilarity.Idf(new[] { a.Print, b.Print });
        Assert.False(NewsSimilarity.SameStory(a, b, idf));
        Assert.True(NewsSimilarity.SameStory(a, b with { PublishedAt = T0.AddHours(30) }, idf));
    }

    [Fact]
    public void Turkce_ekler_atilir_sayilar_ayri_tutulur()
    {
        var t = NewsSimilarity.Tokens("ASELSAN'dan 5 fabrikalık yatırım ve 43,9 milyar");
        Assert.Contains("asels", t);
        Assert.Contains("fabri", t);
        Assert.Contains("#5", t);
        Assert.Contains("#439", t);
        Assert.DoesNotContain("ve", t);
    }

    [Fact]
    public void Kalacak_haber_firmaya_bagli_onemli_ve_ilk_yayimlanandir()
    {
        var keeper = NewsDuplicateCleaner.PickKeeper(new[]
        {
            new NewsDuplicateCleaner.Candidate(1, null, 5, true, T0),
            new NewsDuplicateCleaner.Candidate(2, 7, 3, false, T0.AddHours(2)),
            new NewsDuplicateCleaner.Candidate(3, 7, 4, true, T0.AddHours(3)),
            new NewsDuplicateCleaner.Candidate(4, 7, 4, true, T0.AddHours(1))
        });
        Assert.Equal(4, keeper.Id);
    }

    [Fact]
    public async Task Temizlik_kopyalari_siler_birini_tutar_en_yuksek_onemi_ona_tasir()
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var now = T0.AddDays(1);
        NewsFeedItem N(long id, string title, int imp, string cat = NewsCategories.Investment) => new()
        {
            Id = id, Title = title, Url = "https://x/" + id, UrlHash = "h" + id, TitleKey = "k" + id,
            PublishedAt = T0.AddHours(id), FetchedAt = T0.AddHours(id), Importance = imp, Category = cat, Origin = NewsOrigins.Radar
        };
        db.NewsItems.AddRange(
            N(1, "Erdoğan talimatı verdi: ASELSAN'dan Malatya'ya 5 fabrikalık yatırım", 3),
            N(2, "ASELSAN Malatya’ya 5 fabrika kuruyor: Erdoğan talimat verdi", 5),
            N(3, "Cumhurbaşkanı Erdoğan talimat verdi: ASELSAN Malatya'ya 5 fabrika kuruyor", 2),
            N(4, "Satışları düşen PepsiCo, yaklaşık 1000 kişinin işine son verecek", 3, NewsCategories.Risk));
        await db.SaveChangesAsync();

        var removed = await NewsDuplicateCleaner.RunAsync(db, now, CancellationToken.None);

        Assert.Equal(2, removed);
        var left = await db.NewsItems.OrderBy(n => n.Id).ToListAsync();
        Assert.Equal(new long[] { 2, 4 }, left.Select(n => n.Id));
        Assert.Equal(5, left[0].Importance);
    }
}
