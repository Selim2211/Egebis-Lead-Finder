using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace EgebisLeadFinder.Models;

/// <summary>Firma Arama ekranindan gelen kriterler.</summary>
public class SearchCriteria
{
    public string Industry { get; set; } = string.Empty;

    /// <summary>
    /// Sorgu kaliplari: true = uretici/fabrika ("üreticileri", "fabrikası"), false = her tur firma
    /// ("firmaları", "şirketleri"). Formdan gelmez; arama oncesi "Şirketimiz" profilinden doldurulur.
    /// </summary>
    [Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
    public bool ManufacturerQueries { get; set; } = true;

    /// <summary>"SAP kariyer" sorgusu eklensin mi? Profil SAP'den soz ediyorsa (ya da profil yoksa) evet.</summary>
    [Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
    public bool SapQueries { get; set; } = true;
    public string Country { get; set; } = "Türkiye";

    /// <summary>Arama bolgesi anahtari (bkz. SearchRegions); bos ise Türkiye.</summary>
    public string? RegionKey { get; set; }
    public string? City { get; set; }
    public string? CompanySize { get; set; }
    public string? TargetPosition { get; set; }

    /// <summary>Islenecek azami firma sayisi (MVP: 20).</summary>
    public int MaxCompanies { get; set; } = 20;

    /// <summary>
    /// Arama Profili penceresinde girilen profil adi. Bu isimde bir SearchProfile
    /// bulunur/olusturulur (bkz. CompanyController.SaveProfile).
    /// </summary>
    public string? ProfileName { get; set; }

    /// <summary>
    /// Aramada: secilen profilin Id'si (zorunlu). Profil penceresinde: duzenlenen kaydin Id'si;
    /// doluysa profil adi da dahil tum alanlar bu kaydin uzerine yazilir.
    /// </summary>
    public int? ProfileId { get; set; }

    /// <summary>Kaydedilen arama sablonu tum kullanicilara acik mi (sonuclar yine kisiye ozel).</summary>
    public bool ProfilePublic { get; set; }

    /// <summary>
    /// Doluysa sektor yerine belirli bir firma aranir ("Toyota", "Egebis"). Sorgular
    /// firma adina gore kurulur ve sadece birkac aday site islenir.
    /// </summary>
    public string? CompanyName { get; set; }

    public bool IsNameSearch => !string.IsNullOrWhiteSpace(CompanyName);

    /// <summary>
    /// Opsiyonel NACE kodu (ör. "27.51.08"). Doluysa arama terimleri kodun faaliyetinden uretilir
    /// ve sonuc, firmanin NACE kodu acikca baska olanlar elenerek suzulur. Kontrolor kanonik bicime getirir.
    /// </summary>
    public string? NaceCode { get; set; }

    /// <summary>
    /// Formdan: NACE kodunun urunlerinden kullanicinin sectikleri (virgullu, ör. "bulaşık makinesi, çamaşır makinesi").
    /// Kontrolor resmi urun listesiyle kesistirip duzeltir; bossa tum urunler.
    /// </summary>
    public string? NaceProducts { get; set; }

    /// <summary>Aranacak urunler (kodun resmi urunlerinden secilenler). Formdan gelmez. Arama bunlarla yapilir; sonucta bu
    /// urunlerden en az birini URETEN firmalar kalir.</summary>
    [BindNever]
    public List<string> ProductList { get; set; } = new();

    /// <summary>"NACE 27.51.08: Elektrikli ev aletleri imalatı" gibi yapay zeka istemlerine eklenen baglam. Formdan gelmez.</summary>
    [BindNever]
    public string? NaceContext { get; set; }

    /// <summary>Yapay zeka istemlerinde kullanilacak sektor metni: sektor + (varsa) NACE baglami.</summary>
    [BindNever]
    public string AiIndustry => string.IsNullOrWhiteSpace(NaceContext) ? Industry : $"{Industry} ({NaceContext})";

    /// <summary>
    /// On eleme (siteler okunmadan) icin kisa sektor metni: uzun NACE aciklamasi modeli fazla katilastirip tum adaylari
    /// eleyebildiginden yalnizca kod eklenir.
    /// </summary>
    [BindNever]
    public string ScreenIndustry => ProductList.Count == 0
        ? Industry
        : $"{Industry} (şu ürünlerden en az birini üreten firmalar: {string.Join(", ", ProductList)})";

    /// <summary>
    /// Akilli arama: yapay zeka sektore gore ek arama terimleri onerir ve siteler okunmadan once
    /// hedef disi adaylari eler (bkz. SearchPlanService). Her aramada acik; formdan gelmez.
    /// </summary>
    [BindNever]
    public bool SmartSearch { get; set; } = true;

    /// <summary>
    /// Sorgu kaliplarinda sektor yerine kullanilacak terimler. Formdan gelmez; SearchPlanService
    /// doldurur. Bossa yalnizca Industry kullanilir.
    /// </summary>
    [BindNever]
    public List<string> SearchTerms { get; set; } = new();

    /// <summary>
    /// Aramada toplanacak aday sayisi. On eleme yapilacaksa MaxCompanies'den biraz fazla
    /// toplanir ki elenenlerin yerine aday kalsin. Bossa MaxCompanies.
    /// </summary>
    [BindNever]
    public int? CollectTarget { get; set; }

    [BindNever]
    public int EffectiveCollectTarget => Math.Max(CollectTarget ?? MaxCompanies, MaxCompanies);
}

/// <summary>Search API'den donen tek bir sonuc.</summary>
public class SearchResult
{
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public string? Snippet { get; set; }

    /// <summary>Google'in sonuca ilistirdigi tarih ("3 gün önce", "12 Mar 2026"); yoksa bos.</summary>
    public string? Date { get; set; }

    // --- Asagidaki alanlar yalnizca Google Haritalar (Places) aramasindan gelir;
    // organik aramada bos kalirlar. Fabrikalarin SEO'su genelde zayif oldugu icin
    // organik sonuclarda ust siralara cikamiyorlar, Haritalar kaydi ise neredeyse
    // hepsinde var ve telefon/adres bilgisini kazima yapmadan veriyor.

    /// <summary>Haritalar kaydindaki telefon numarasi.</summary>
    public string? Phone { get; set; }

    /// <summary>Haritalar kaydindaki acik adres.</summary>
    public string? Address { get; set; }

    /// <summary>
    /// Haritalar isletme kategorisi ("Otomobil Parcasi Ureticisi", "Oto Tamircisi" vb.).
    /// Uretici olmayan isletmeleri elemek icin kullanilir.
    /// </summary>
    public string? Category { get; set; }
}
