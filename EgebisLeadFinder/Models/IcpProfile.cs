using System.Text.Json.Serialization;

namespace EgebisLeadFinder.Models;

/// <summary>
/// Ideal musteri profili (ICP). Ayarlar ekranindan tanimlanir; bossa puanlama
/// appsettings'teki sabit agirliklarla eskisi gibi calisir.
/// </summary>
public class IcpProfile
{
    /// <summary>Hedef NACE bolumleri (2 hane, ör. "22") veya siniflar ("22.19").</summary>
    [JsonPropertyName("nace")]
    public List<string> NaceCodes { get; set; } = new();

    /// <summary>Sektor metninde aranacak kelimeler ("otomotiv", "plastik").</summary>
    [JsonPropertyName("industries")]
    public List<string> IndustryKeywords { get; set; } = new();

    [JsonPropertyName("cities")]
    public List<string> Cities { get; set; } = new();

    [JsonPropertyName("countries")]
    public List<string> Countries { get; set; } = new();

    /// <summary>Asgari calisan sayisi; 0 = sart yok.</summary>
    [JsonPropertyName("minEmployees")]
    public int MinEmployees { get; set; }

    [JsonPropertyName("requireManufacturer")]
    public bool RequireManufacturer { get; set; } = true;

    /// <summary>Firma adi/sektor/urunlerde gecerse firma elenir ("bayi", "distribütör").</summary>
    [JsonPropertyName("exclude")]
    public List<string> ExcludeKeywords { get; set; } = new();

    [JsonPropertyName("locationWeight")]
    public int LocationWeight { get; set; } = 10;

    [JsonIgnore]
    public bool HasIndustryCriteria => NaceCodes.Count > 0 || IndustryKeywords.Count > 0;

    [JsonIgnore]
    public bool HasLocationCriteria => Cities.Count > 0 || Countries.Count > 0;

    [JsonIgnore]
    public bool IsActive =>
        HasIndustryCriteria || HasLocationCriteria || MinEmployees > 0 || ExcludeKeywords.Count > 0;

    /// <summary>NACE kodu ICP'deki bir bolum/sinifla eslesiyor mu? "22" -> "22.19" eslesir.</summary>
    public bool MatchesNace(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return false;
        var normalized = code.Trim();
        return NaceCodes.Any(n => normalized.StartsWith(n.Trim(), StringComparison.Ordinal));
    }
}
