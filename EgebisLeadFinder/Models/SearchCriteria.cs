using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace EgebisLeadFinder.Models;

/// <summary>Firma Arama ekranindan gelen kriterler.</summary>
public class SearchCriteria
{
    public string Industry { get; set; } = string.Empty;
    public string Country { get; set; } = "Türkiye";

    /// <summary>Arama bolgesi anahtari (bkz. SearchRegions); bos ise Türkiye.</summary>
    public string? RegionKey { get; set; }
    public string? City { get; set; }
    public string? CompanySize { get; set; }
    public string? TargetPosition { get; set; }

    /// <summary>Islenecek azami firma sayisi (MVP: 20).</summary>
    public int MaxCompanies { get; set; } = 20;

    /// <summary>
    /// "Gelismis Ayarlar" altinda girilen arama profili adi. Doluysa bu isimde bir
    /// SearchProfile bulunur/olusturulur; arama sonucundaki firmalar kullanici
    /// "Kaydet" dedikten sonra bu profilin listesine eklenir (bkz. CompanyController).
    /// </summary>
    public string? ProfileName { get; set; }

    /// <summary>
    /// Duzenlenen kayitli profilin Id'si. Doluysa profil adi da dahil tum alanlar bu
    /// kaydin uzerine yazilir (ad degisikligi yeni profil olusturmaz).
    /// </summary>
    public int? ProfileId { get; set; }

    /// <summary>
    /// Doluysa sektor yerine belirli bir firma aranir ("Toyota", "Egebis"). Sorgular
    /// firma adina gore kurulur ve sadece birkac aday site islenir.
    /// </summary>
    public string? CompanyName { get; set; }

    public bool IsNameSearch => !string.IsNullOrWhiteSpace(CompanyName);

    /// <summary>Firma Ara'da tiklanan "Biz ne arıyoruz?" segmenti (BusinessProfile.Segments[].Id).</summary>
    public string? SegmentId { get; set; }

    /// <summary>
    /// Akilli arama: yapay zeka sektore/segmente gore ek arama terimleri onerir ve
    /// siteler okunmadan once hedef disi adaylari eler (bkz. SearchPlanService).
    /// </summary>
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
