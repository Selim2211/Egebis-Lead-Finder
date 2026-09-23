using System.Text.Json;
using System.Text.RegularExpressions;

namespace EgebisLeadFinder.Services;

public record NaceDivision(string Code, string Name, string SectionCode, string SectionName);

/// <summary>
/// NACE Rev.2 bolumleri (2 haneli) ve kisimlari (harf). Veri Data/reference/nace-rev2-tr.json'dan
/// okunur. Firmanin 4 haneli kodu AI'dan gelir; filtre ve ICP bolum (ilk 2 hane) duzeyindedir.
/// </summary>
public static class NaceCatalog
{
    private static readonly Lazy<IReadOnlyList<NaceDivision>> Data = new(Load);

    public static IReadOnlyList<NaceDivision> Divisions => Data.Value;

    public static NaceDivision? Division(string? code)
    {
        var d = DivisionCode(code);
        return d is null ? null : Divisions.FirstOrDefault(x => x.Code == d);
    }

    /// <summary>"22.19" -> "22", "2219" -> "22"; gecersizse null.</summary>
    public static string? DivisionCode(string? code)
    {
        var n = Normalize(code);
        return n is { Length: >= 2 } ? n[..2] : null;
    }

    /// <summary>AI ciktisini "22", "22.1" veya "22.19" bicimine getirir; gecersizse null.</summary>
    public static string? Normalize(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var m = Regex.Match(code, @"(\d{2})\.?(\d{1,2})?");
        if (!m.Success) return null;
        return m.Groups[2].Success ? $"{m.Groups[1].Value}.{m.Groups[2].Value}" : m.Groups[1].Value;
    }

    /// <summary>"22.19 · Kauçuk ve plastik ürünlerin imalatı" gibi okunur ad.</summary>
    public static string? Label(string? code)
    {
        var n = Normalize(code);
        if (n is null) return null;
        var d = Division(n);
        return d is null ? n : $"{n} · {d.Name}";
    }

    private static IReadOnlyList<NaceDivision> Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Data", "reference", "nace-rev2-tr.json");
        if (!File.Exists(path)) return Array.Empty<NaceDivision>();

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var list = new List<NaceDivision>();
        foreach (var section in doc.RootElement.GetProperty("sections").EnumerateArray())
        {
            var sCode = section.GetProperty("code").GetString()!;
            var sName = section.GetProperty("name").GetString()!;
            foreach (var div in section.GetProperty("divisions").EnumerateArray())
                list.Add(new NaceDivision(div.GetProperty("code").GetString()!, div.GetProperty("name").GetString()!, sCode, sName));
        }
        return list;
    }
}
