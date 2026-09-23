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
