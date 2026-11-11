using System.Text.Json;
using System.Text.RegularExpressions;

namespace EgebisLeadFinder.Services;

public record NaceDivision(string Code, string Name, string SectionCode, string SectionName);

/// <summary>Firma NACE kodunun aranan kodla karsilastirma sonucu (bkz. NaceCatalog.Compare).</summary>
public enum NaceFit { No, Partial, Yes }

/// <summary>
/// NACE Rev.2 bolumleri (2 haneli) ve kisimlari (harf). Veri Data/reference/nace-rev2-tr.json'dan
/// okunur. Firmanin 4 haneli kodu AI'dan gelir; filtre ve ICP bolum (ilk 2 hane) duzeyindedir.
/// </summary>
public static class NaceCatalog
{
    private static readonly Lazy<IReadOnlyList<NaceDivision>> Data = new(Load);
    private static readonly Lazy<IReadOnlyDictionary<string, string>> SixDigit = new(LoadSixDigit);

    public static IReadOnlyList<NaceDivision> Divisions => Data.Value;

    public static NaceDivision? Division(string? code)
    {
        var d = DivisionCode(code);
        return d is null ? null : Divisions.FirstOrDefault(x => x.Code == d);
    }

    /// <summary>
    /// Resmi NACE Rev.2 6 haneli faaliyet kodlari (anahtar "275108", deger Turkce faaliyet adi). Kaynak: GIB/TUIK
    /// "NACE Rev.2 Altili" listesi (Data/reference/nace-tr-6.json, 2.201 kod).
    /// </summary>
    public static IReadOnlyDictionary<string, string> Activities => SixDigit.Value;

    /// <summary>6 haneli kodun resmi adi ("27.51.08" ya da "275108" verilebilir); listede yoksa null.</summary>
    public static string? Activity(string? code)
    {
        var d = new string((code ?? string.Empty).Where(char.IsDigit).ToArray());
        return d.Length == 6 && Activities.TryGetValue(d, out var name) ? name : null;
    }

    /// <summary>Verilen onekle ("27", "275", "2751") baslayan resmi 6 haneli faaliyetler, kod sirasinda.</summary>
    public static List<KeyValuePair<string, string>> ActivitiesUnder(string? prefix)
    {
        var d = new string((prefix ?? string.Empty).Where(char.IsDigit).ToArray());
        return d.Length < 2 ? new() : Activities.Where(a => a.Key.StartsWith(d, StringComparison.Ordinal)).ToList();
    }

    /// <summary>
    /// Kod resmi listede gercekten var mi? 2 haneli: bolum; 3-6 haneli: bu onekle baslayan en az bir resmi faaliyet.
    /// Yapay zekanin urettigi "20.22", "28.32" gibi var olmayan siniflari yakalar.
    /// </summary>
    public static bool Exists(string? code)
    {
        var d = new string((code ?? string.Empty).Where(char.IsDigit).ToArray());
        if (d.Length == 2) return Division(d) is not null;
        return d.Length is >= 3 and <= 6 && Activities.Keys.Any(k => k.StartsWith(d, StringComparison.Ordinal));
    }

    /// <summary>
    /// Yapay zekanin verdigi firma/sektor kodunu en ayrintili gecerli bicime getirir: resmi 6 haneli kod oldugu gibi;
    /// resmi listede olmayan (uydurma) 6 haneli kod 4 haneli sinifa (varsa) iner; diger kodlar resmi listede varsa kalir.
    /// Hicbiri gecerli degilse null.
    /// </summary>
    public static string? ResolveCode(string? raw)
    {
        var n = Normalize(raw);
        if (n is null) return null;
        var digits = new string(n.Where(char.IsDigit).ToArray());
        if (digits.Length == 6)
        {
            if (Activities.ContainsKey(digits)) return n;
            n = $"{digits[..2]}.{digits[2..4]}";
        }
        return Exists(n) ? n : null;
    }

    /// <summary>Kodun okunur adi: 6 haneli resmi faaliyet adi, yoksa bolum adi.</summary>
    public static string? NameOf(string? code) => Activity(code) ?? Division(code)?.Name;

    /// <summary>Anahtar kelimeye ya da koda uyan resmi 6 haneli faaliyetler (en cok kelimesi tutan once).</summary>
    public static List<KeyValuePair<string, string>> SearchActivities(string? query, int take = 15)
    {
        var q = (query ?? string.Empty).Trim();
        if (q.Length < 2) return new();
        if (q.All(ch => char.IsDigit(ch) || ch == '.' || ch == ' '))
            return ActivitiesUnder(q).Take(take).ToList();

        var words = TurkishText.Normalize(q)
            .Split(new[] { ' ', ',', '-', '/' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 3).Select(w => w.Length > 5 ? w[..5] : w).Distinct().ToList();
        if (words.Count == 0) return new();
        return Activities
            .Select(a =>
            {
                var name = " " + TurkishText.Normalize(a.Value);
                return (Item: a, Hits: words.Count(w => name.Contains(" " + w, StringComparison.Ordinal)));
            })
            .Where(x => x.Hits > 0)
            .OrderByDescending(x => x.Hits).ThenBy(x => x.Item.Key, StringComparer.Ordinal)
            .Take(take).Select(x => x.Item).ToList();
    }

    /// <summary>"275108" -> "27.51.08".</summary>
    public static string FormatSixDigit(string digits) => $"{digits[..2]}.{digits[2..4]}.{digits[4..6]}";

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
        // Tam 6 haneli kod ("27.51.08" / "275108") oldugu gibi korunur; serbest metinden yalniz ilk 4 hane alinir.
        if (Regex.IsMatch(code.Trim(), @"^\d{2}\.?\d{2}\.?\d{2}$")) return NormalizeFull(code);
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
        var activity = Activity(n);
        return activity is not null ? $"{n} · {activity}" : d is null ? n : $"{n} · {d.Name}";
    }

    /// <summary>
    /// Kullanicinin girdigi kodu kanonik bicime getirir: "275108" / "27.51.08" -> "27.51.08",
    /// "2751" -> "27.51", "275" -> "27.5", "27" -> "27". 2, 3, 4 ve 6 hane gecerlidir (Turkiye NACE Rev.2
    /// 6 haneye kadar iner); gecersizse null.
    /// </summary>
    public static string? NormalizeFull(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var digits = new string(code.Where(char.IsDigit).ToArray());
        return digits.Length switch
        {
            2 => digits,
            3 => $"{digits[..2]}.{digits[2]}",
            4 => $"{digits[..2]}.{digits[2..4]}",
            6 => $"{digits[..2]}.{digits[2..4]}.{digits[4..6]}",
            _ => null
        };
    }

    /// <summary>
    /// Firmanin (yapay zekanin atadigi) NACE kodu aranan kodla uyusuyor mu? Firma kodlari en cok 4
    /// haneli (sinif) oldugundan karsilastirma en cok ilk 4 hanede yapilir. Firmanin kodu bilinmiyorsa
    /// ya da aranandan daha genelse (ör. yalnizca "27") elenmez: yalnizca acikca baska kodda olanlar elenir.
    /// </summary>
    public static bool MatchesTarget(string? companyCode, string? targetCode) =>
        Compare(companyCode, targetCode) != NaceFit.No;

    /// <summary>
    /// Firma kodu ile aranan kod karsilastirmasi (6 haneye kadar, onek esasli):
    /// No = acikca baska kod; Yes = firma kodu aranan kodla ayni ya da daha ayrintili; Partial = firma kodu aranandan daha
    /// genel ve ayni dalda (ör. firma "27.51", aranan "27.51.08": alt faaliyeti ayrica belirlenmeli) ya da firma kodu bilinmiyor.
    /// </summary>
    public static NaceFit Compare(string? companyCode, string? targetCode)
    {
        var target = new string((targetCode ?? string.Empty).Where(char.IsDigit).ToArray());
        if (target.Length < 2) return NaceFit.Yes;
        if (target.Length > 6) target = target[..6];

        var company = new string((companyCode ?? string.Empty).Where(char.IsDigit).ToArray());
        if (company.Length < 2) return NaceFit.Partial;
        if (company.Length > 6) company = company[..6];

        var n = Math.Min(company.Length, target.Length);
        if (company[..n] != target[..n]) return NaceFit.No;
        return company.Length >= target.Length ? NaceFit.Yes : NaceFit.Partial;
    }

    private static IReadOnlyDictionary<string, string> LoadSixDigit()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Data", "reference", "nace-tr-6.json");
        if (!File.Exists(path)) return new Dictionary<string, string>();
        return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? new Dictionary<string, string>();
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
