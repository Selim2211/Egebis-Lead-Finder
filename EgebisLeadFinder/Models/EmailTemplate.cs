using System.ComponentModel.DataAnnotations;

namespace EgebisLeadFinder.Models;

/// <summary>
/// E-mail sablonu. Govde {CONTACT_NAME}, {COMPANY_NAME}, {INDUSTRY} yer tutuculari icerir.
/// </summary>
public class EmailTemplate
{
    public int Id { get; set; }

    [Required, MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    /// <summary>AI'in onerdigi sablon anahtari ile eslesir (orn: "SAP").</summary>
    [MaxLength(50)]
    public string? Key { get; set; }

    [Required, MaxLength(300)]
    public string Subject { get; set; } = string.Empty;

    [Required]
    public string Body { get; set; } = string.Empty;

    /// <summary>
    /// Taslak Duzenleyici'de hazirlanan bicimli govde (gorsel, kalin yazi...). Null ise
    /// duz metin <see cref="Body"/> kullanilir. Yer tutucular burada da gecerlidir.
    /// </summary>
    public string? BodyHtml { get; set; }

    public bool Active { get; set; } = true;

    public DateTime? UpdatedAt { get; set; }

    /// <summary>Taslakta kullanilabilen yer tutucular (Taslak Duzenleyici'de listelenir).</summary>
    public static readonly (string Token, string Label, string Sample)[] Placeholders =
    {
        ("{CONTACT_NAME}", "Kişi adı", "Ahmet Yılmaz"),
        ("{CONTACT_TITLE}", "Kişi unvanı", "Bilgi İşlem Müdürü"),
        ("{COMPANY_NAME}", "Firma adı", "Örnek Makine A.Ş."),
        ("{INDUSTRY}", "Sektör", "makine imalatı"),
        ("{CITY}", "Şehir", "İzmir")
    };

    /// <summary>Cevap gelmeyen lead'e atilan hatirlatma sablonunun anahtari.</summary>
    public const string FollowUpKey = "TAKIP";

    /// <summary>Genel tanitim sablonunun anahtari (eslesme bulunmazsa bu kullanilir).</summary>
    public const string GeneralKey = "GENEL";

    /// <summary>"Biz ne arıyoruz?" segmentine eslenen sablonun anahtari: "SEG:{segmentId}".</summary>
    public const string SegmentKeyPrefix = "SEG:";

    public static string SegmentKey(string segmentId) => SegmentKeyPrefix + segmentId;

    /// <summary>
    /// Taslak Duzenleyici'deki "Yapay zeka eşlemesi" secenekleri. Sirket profili doluysa genel tanitim,
    /// her hedef segment ve takip maili; eski Egebis (SAP/MES) anahtarlari yalnizca profil yokken ya da
    /// profil SAP'den soz ediyorsa listelenir.
    /// </summary>
    public static List<(string Key, string Label)> AiKeysFor(BusinessProfile? profile)
    {
        if (profile is not { IsConfigured: true }) return AiKeys.ToList();

        var keys = new List<(string Key, string Label)> { (GeneralKey, "Genel tanıtım") };
        keys.AddRange(profile.Segments
            .Where(seg => !string.IsNullOrWhiteSpace(seg.Name))
            .Select(seg => (SegmentKey(seg.Id), $"Segment: {seg.Name}")));
        if (profile.MentionsSap)
            keys.AddRange(AiKeys.Where(k => k.Key != GeneralKey && k.Key != FollowUpKey));
        keys.Add((FollowUpKey, "Takip maili (cevap gelmeyen lead)"));
        return keys;
    }

    /// <summary>Kaydedilebilir eslesme anahtari mi (profil secenekleri ya da eski sabit anahtarlar)?</summary>
    public static bool IsKnownKey(string? key, BusinessProfile? profile) =>
        !string.IsNullOrWhiteSpace(key)
        && (AiKeys.Any(k => k.Key == key) || AiKeysFor(profile).Any(k => k.Key == key));

    /// <summary>
    /// Sablonun rolu: ilk bes anahtari yapay zeka firma analizinde onerir,
    /// "TAKIP" ise cevap gelmeyen lead'de otomatik one cikar.
    /// </summary>
    public static readonly (string Key, string Label)[] AiKeys =
    {
        ("GENEL", "Genel tanıtım"),
        ("SAP", "SAP danışmanlığı"),
        ("SAP_ENTEGRASYON", "SAP entegrasyonu"),
        ("URETIM_YAZILIMI", "Üretim yazılımı"),
        ("MES", "MES / üretim takip"),
        (FollowUpKey, "Takip maili (cevap gelmeyen lead)")
    };
}
