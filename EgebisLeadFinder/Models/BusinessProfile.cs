using System.Text;
using System.Text.Json.Serialization;

namespace EgebisLeadFinder.Models;

/// <summary>
/// "Biz ne arıyoruz?" — uygulamayi kullanan sirketin kendi tanimi: ne satiyor, kime satiyor,
/// kimler musterisi degil. Yapay zeka firma analizinde, arastirma raporunda ve e-posta yaziminda
/// bu tanimi kullanir; bos ise eski (Egebis'e ozel) sabit tanimlar gecerlidir.
/// Ayarlar tablosunda JSON olarak tutulur (SettingKeys.BusinessProfile).
/// </summary>
public class BusinessProfile
{
    public const int MaxSegments = 8;

    /// <summary>Sirketin ticari adi ("Egebis Bilişim").</summary>
    [JsonPropertyName("companyName")]
    public string CompanyName { get; set; } = string.Empty;

    [JsonPropertyName("website")]
    public string? Website { get; set; }

    /// <summary>Ne satiyoruz: urun/hizmetler.</summary>
    [JsonPropertyName("offering")]
    public string Offering { get; set; } = string.Empty;

    /// <summary>Musteride hangi sorunu cozuyoruz / hangi faydayi sagliyoruz.</summary>
    [JsonPropertyName("problems")]
    public string? ProblemsWeSolve { get; set; }

    /// <summary>Ideal musteri tanimi (sektor, buyukluk, ozellik).</summary>
    [JsonPropertyName("idealCustomer")]
    public string IdealCustomer { get; set; } = string.Empty;

    /// <summary>Musterimiz OLMAYANLAR: bayi, distributor, dernek...</summary>
    [JsonPropertyName("notCustomers")]
    public string? NotCustomers { get; set; }

    /// <summary>Rakip tanimi: bizimle ayni isi yapan firmalar (musteri degil, elenir).</summary>
    [JsonPropertyName("competitors")]
    public string? Competitors { get; set; }

    /// <summary>Ornek mevcut musteriler (ad veya site).</summary>
    [JsonPropertyName("exampleCustomers")]
    public List<string> ExampleCustomers { get; set; } = new();

    [JsonPropertyName("segments")]
    public List<TargetSegment> Segments { get; set; } = new();

    [JsonPropertyName("updatedAt")]
    public DateTime? UpdatedAt { get; set; }

    [JsonPropertyName("updatedBy")]
    public string? UpdatedBy { get; set; }

    /// <summary>Yapay zekaya verilecek kadar dolu mu? (ne satiyoruz + ideal musteri sart)</summary>
    [JsonIgnore]
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Offering) && !string.IsNullOrWhiteSpace(IdealCustomer);

    /// <summary>Yapay zekanin dondurdugu segment adini profildeki segmentle eslestirir (buyuk/kucuk harf ve Turkce harf farki yok sayilir).</summary>
    public TargetSegment? FindSegment(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var key = EgebisLeadFinder.Services.TurkishText.Normalize(name.Trim());
        return Segments.FirstOrDefault(s => EgebisLeadFinder.Services.TurkishText.Normalize(s.Name.Trim()) == key);
    }

    /// <summary>Profil SAP'den soz ediyor mu? (Evetse firma ekranlarinda SAP etiketleri gosterilir.)</summary>
    [JsonIgnore]
    public bool MentionsSap => ToPromptBlock().Contains("SAP", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(CompanyName) ? "Şirketimiz" : CompanyName.Trim();

    /// <summary>
    /// Yapay zeka talimatlarinin basina konan "biz kimiz" blogu. Tum alanlar kullanici girdisidir;
    /// talimat gibi degil, tanim olarak verilir.
    /// </summary>
    public string ToPromptBlock()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{DisplayName} için potansiyel müşteri çalışması yapıyorsun. Aşağıda {DisplayName} şirketinin kendi tanımı var:");
        sb.AppendLine();
        sb.AppendLine($"NE SATIYORUZ: {Line(Offering)}");
        if (!string.IsNullOrWhiteSpace(ProblemsWeSolve)) sb.AppendLine($"ÇÖZDÜĞÜMÜZ SORUNLAR: {Line(ProblemsWeSolve)}");
        sb.AppendLine($"İDEAL MÜŞTERİMİZ: {Line(IdealCustomer)}");
        if (!string.IsNullOrWhiteSpace(NotCustomers)) sb.AppendLine($"MÜŞTERİMİZ OLMAYANLAR: {Line(NotCustomers)}");
        if (!string.IsNullOrWhiteSpace(Competitors)) sb.AppendLine($"RAKİPLERİMİZ (müşteri değil): {Line(Competitors)}");
        if (ExampleCustomers.Count > 0) sb.AppendLine($"ÖRNEK MEVCUT MÜŞTERİLER: {string.Join(", ", ExampleCustomers.Take(10))}");

        var segments = Segments.Where(s => !string.IsNullOrWhiteSpace(s.Name)).ToList();
        if (segments.Count > 0)
        {
            sb.AppendLine("HEDEF SEGMENTLER:");
            foreach (var s in segments)
                sb.AppendLine($"- {Line(s.Name)}{(string.IsNullOrWhiteSpace(s.Description) ? "" : ": " + Line(s.Description))}");
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>Tek satira indirir ve asiri uzun metni keser (prompt sismesin).</summary>
    private static string Line(string? text)
    {
        var value = (text ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
        return value.Length > 800 ? value[..800] + "…" : value;
    }
}

/// <summary>Bir hedef segment: ayni sirketin farkli urun grubu / musteri kitlesi.</summary>
public class TargetSegment
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

    /// <summary>"Otomotiv yan sanayi"</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Bu segmentte neden/ne satiyoruz, hangi firmalar uygun.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>Firma Ara'da sektor kutusuna yazilacak terim ("Otomotiv yan sanayi").</summary>
    [JsonPropertyName("searchTerm")]
    public string? SearchTerm { get; set; }

    /// <summary>Varsayilan arama bolgesi (SearchRegions anahtari); bos = Ayarlar'daki.</summary>
    [JsonPropertyName("region")]
    public string? RegionKey { get; set; }

    /// <summary>ICP'ye aktarilacak sektor anahtar kelimeleri.</summary>
    [JsonPropertyName("keywords")]
    public List<string> Keywords { get; set; } = new();

    /// <summary>ICP'ye aktarilacak NACE kodlari ("22", "29.32").</summary>
    [JsonPropertyName("nace")]
    public List<string> NaceCodes { get; set; } = new();

    /// <summary>ICP'ye aktarilacak eleme kelimeleri ("bayi", "distribütör").</summary>
    [JsonPropertyName("exclude")]
    public List<string> ExcludeKeywords { get; set; } = new();

    /// <summary>Bu segmentte ulasilacak unvanlar; Apollo aramasinda genel unvanlarin onune konur.</summary>
    [JsonPropertyName("titles")]
    public List<string> TargetTitles { get; set; } = new();

    [JsonIgnore]
    public string EffectiveSearchTerm => string.IsNullOrWhiteSpace(SearchTerm) ? Name : SearchTerm!;
}

/// <summary>Yapay zekanin sirket sitesinden cikardigi taslak (kullanici duzeltip kaydeder).</summary>
public class BusinessProfileDraft
{
    public BusinessProfile Profile { get; set; } = new();

    /// <summary>Onerilen hedef unvanlar (Ayarlar'daki lead unvanlari).</summary>
    public List<string> TargetTitles { get; set; } = new();

    public string? Error { get; set; }
    public bool Success => Error is null;
}
