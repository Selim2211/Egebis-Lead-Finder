using System.Text.Json;

namespace EgebisLeadFinder.Services.Quality;

/// <summary>
/// Olcu seti kalemi: bir NACE kodu icin elle bilinen gercek ureticiler ve ureticisi olmadigi bilinen firmalar (alan adi).
/// </summary>
public class GoldItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = string.Empty;
    public string NaceCode { get; set; } = string.Empty;

    /// <summary>Aramada denenecek firma sayisi.</summary>
    public int MaxCompanies { get; set; } = 30;

    /// <summary>Bu kod icin gercekten uretici oldugu bilinen alan adlari.</summary>
    public List<string> Producers { get; set; } = new();

    /// <summary>Uretici olmadigi bilinen (bayi, e-ticaret, rehber...) alan adlari.</summary>
    public List<string> NotProducers { get; set; } = new();
}

/// <summary>Bir aramanin olcu setine gore sonucu.</summary>
public class EvalResult
{
    public int Returned { get; set; }

    /// <summary>Dogru bulunan: donen ve "uretici" etiketli.</summary>
    public List<string> Found { get; set; } = new();

    /// <summary>Yanlis bulunan: donen ama "uretici degil" etiketli.</summary>
    public List<string> Wrong { get; set; } = new();

    /// <summary>Kacirilan: "uretici" etiketli ama donmeyen.</summary>
    public List<string> Missed { get; set; } = new();

    /// <summary>Donen ama henuz etiketlenmemis: dogrulugu bilinmiyor, etiketlenmeli.</summary>
    public List<string> Unlabeled { get; set; } = new();

    /// <summary>Etiketli donen firmalarin ne kadari gercekten uretici: Found / (Found + Wrong). Etiketli firma yoksa null.</summary>
    public double? Precision => Found.Count + Wrong.Count == 0 ? null : Found.Count / (double)(Found.Count + Wrong.Count);

    /// <summary>Bilinen ureticilerin ne kadari bulundu: Found / (Found + Missed). Bilinen uretici yoksa null.</summary>
    public double? Recall => Found.Count + Missed.Count == 0 ? null : Found.Count / (double)(Found.Count + Missed.Count);
}

public static class ProducerEval
{
    /// <summary>"https://www.Arcelik.com.tr/yol" → "arcelik.com.tr".</summary>
    public static string NormalizeDomain(string? value)
    {
        var d = (value ?? string.Empty).Trim().ToLowerInvariant();
        var scheme = d.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0) d = d[(scheme + 3)..];
        var slash = d.IndexOfAny(new[] { '/', '?', '#', ' ' });
        if (slash >= 0) d = d[..slash];
        return d.StartsWith("www.", StringComparison.Ordinal) ? d[4..] : d;
    }

    /// <summary>Ayni site mi? Alt alan adi da sayilir ("tr.beko.com" = "beko.com").</summary>
    public static bool SameSite(string a, string b)
    {
        a = NormalizeDomain(a); b = NormalizeDomain(b);
        if (a.Length == 0 || b.Length == 0) return false;
        return a == b || a.EndsWith("." + b, StringComparison.Ordinal) || b.EndsWith("." + a, StringComparison.Ordinal);
    }

    public static EvalResult Evaluate(GoldItem gold, IEnumerable<string> returnedDomains)
    {
        var returned = returnedDomains.Select(NormalizeDomain).Where(d => d.Length > 0).Distinct().ToList();
        var result = new EvalResult { Returned = returned.Count };

        foreach (var domain in returned)
        {
            if (gold.Producers.Any(p => SameSite(p, domain))) result.Found.Add(domain);
            else if (gold.NotProducers.Any(n => SameSite(n, domain))) result.Wrong.Add(domain);
            else result.Unlabeled.Add(domain);
        }

        foreach (var producer in gold.Producers.Select(NormalizeDomain).Where(p => p.Length > 0).Distinct())
            if (!returned.Any(r => SameSite(producer, r))) result.Missed.Add(producer);

        return result;
    }

    // ---- Olcu seti saklama (Ayarlar tablosunda tek JSON; ayri tablo/migration gerekmez)

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, WriteIndented = false };

    public static List<GoldItem> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<GoldItem>();
        try { return JsonSerializer.Deserialize<List<GoldItem>>(json, Json) ?? new List<GoldItem>(); }
        catch (JsonException) { return new List<GoldItem>(); }
    }

    public static string Serialize(IEnumerable<GoldItem> items) => JsonSerializer.Serialize(items, Json);

    /// <summary>Satirlardan/virgullerden alan adi listesi: temizlenir, tekillestirilir.</summary>
    public static List<string> ParseDomains(string? text) =>
        (text ?? string.Empty)
            .Split(new[] { '\n', '\r', ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeDomain)
            .Where(d => d.Contains('.'))
            .Distinct()
            .ToList();

    /// <summary>Hic olcu seti yokken gosterilen baslangic ornegi: yalnizca kesin bilinen buyuk ureticiler. Kullanici genisletir.</summary>
    public static List<GoldItem> Starter() => new()
    {
        new GoldItem
        {
            Id = "ornek",
            Name = "Beyaz eşya (bulaşık / çamaşır makinesi)",
            NaceCode = "27.51.08",
            MaxCompanies = 30,
            Producers = new List<string> { "arcelik.com.tr", "beko.com.tr", "vestel.com.tr" }
        }
    };
}
