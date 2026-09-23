using System.Text.Json.Serialization;

namespace EgebisLeadFinder.Models;

/// <summary>
/// AI #1 ciktisi: firma web sitesi metninden uretilen yapisal analiz.
/// Gemini'ye responseSchema olarak bu sekil verilir.
/// </summary>
public class CompanyAnalysis
{
    /// <summary>Metinden okunan gercek firma adi. Arama sonucu basligi cogu zaman
    /// haber veya ilan basligi oldugu icin firma adi buradan alinir.</summary>
    [JsonPropertyName("companyName")]
    public string? CompanyName { get; set; }

    [JsonPropertyName("industry")]
    public string? Industry { get; set; }

    [JsonPropertyName("manufacturer")]
    public bool Manufacturer { get; set; }

    [JsonPropertyName("products")]
    public List<string> Products { get; set; } = new();

    /// <summary>"yes" | "likely" | "no" | "unknown"</summary>
    [JsonPropertyName("sap")]
    public string Sap { get; set; } = "unknown";

    /// <summary>SAP kullanimina dair metinden alinti. Kanit yoksa bos.</summary>
    [JsonPropertyName("sapEvidence")]
    public string? SapEvidence { get; set; }

    /// <summary>
    /// Firma SAP danismanligi/entegrasyonu satiyor mu? Boyleyse Egebis'in
    /// rakibidir, musterisi degil.
    /// </summary>
    [JsonPropertyName("sapVendor")]
    public bool SapVendor { get; set; }

    [JsonPropertyName("employeeSizeHint")]
    public string? EmployeeSizeHint { get; set; }

    [JsonPropertyName("potential")]
    public bool Potential { get; set; }

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    /// <summary>EmailTemplate.Key ile eslesir.</summary>
    [JsonPropertyName("recommendedTemplate")]
    public string? RecommendedTemplate { get; set; }

    /// <summary>SAP kullandigina dair somut kanit var mi?</summary>
    [JsonIgnore]
    public bool UsesSap => Sap.Equals("yes", StringComparison.OrdinalIgnoreCase);

    /// <summary>SAP kullanma ihtimali var mi? (kesin kanit veya guclu isaret)</summary>
    [JsonIgnore]
    public bool LikelyUsesSap =>
        UsesSap || Sap.Equals("likely", StringComparison.OrdinalIgnoreCase);
}
