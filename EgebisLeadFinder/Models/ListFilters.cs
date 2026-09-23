using EgebisLeadFinder.Data;

namespace EgebisLeadFinder.Models;

/// <summary>Firmalar ve Lead'ler listelerindeki sayfalama bilgisi (_Pager).</summary>
public class PagerModel
{
    public const int DefaultPageSize = 20;

    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = DefaultPageSize;
    public int TotalItems { get; init; }

    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalItems / (double)PageSize));
    public int From => TotalItems == 0 ? 0 : (Page - 1) * PageSize + 1;
    public int To => Math.Min(TotalItems, Page * PageSize);

    /// <summary>Istenen sayfayi gecerli araliga ceker (son sayfadan buyukse son sayfa).</summary>
    public static int Clamp(int page, int totalItems, int pageSize = DefaultPageSize)
    {
        var last = Math.Max(1, (int)Math.Ceiling(totalItems / (double)pageSize));
        return Math.Clamp(page, 1, last);
    }

    /// <summary>Gosterilecek sayfa numaralari; araliklar null (… olarak cizilir).</summary>
    public IEnumerable<int?> Window(int radius = 1)
    {
        var pages = new SortedSet<int> { 1, TotalPages };
        for (var p = Page - radius; p <= Page + radius; p++)
            if (p >= 1 && p <= TotalPages) pages.Add(p);

        int? prev = null;
        foreach (var p in pages)
        {
            if (prev is not null && p - prev > 1) yield return null;
            yield return p;
            prev = p;
        }
    }
}

/// <summary>
/// Ulke/bolge + (Türkiye icin) cografi bolge ve il filtresi. Cografi bolge secmek
/// o bolgenin tum illerini secmek demektir.
/// </summary>
public class GeoFilter
{
    /// <summary>Arama bolgesi anahtarlari (TR, DE...); bos ise tum ulkeler.</summary>
    public List<string> Countries { get; init; } = new();

    public List<string> Regions { get; init; } = new();
    public List<string> Cities { get; init; } = new();

    /// <summary>Il → kayit sayisi (il seciminde yaninda gosterilir).</summary>
    public Dictionary<string, int> CityCounts { get; set; } = new();

    /// <summary>Bolge anahtari → kayit sayisi (ulke secenegi yaninda gosterilir).</summary>
    public Dictionary<string, int> CountryCounts { get; set; } = new();

    public bool Any => Countries.Count > 0 || Regions.Count > 0 || Cities.Count > 0;

    /// <summary>Filtre panelinde ulke bolumu gosterilsin mi (verinin icinde ulke var mi).</summary>
    public bool ShowCountries => CountryCounts.Count > 0;

    /// <summary>Secili bolgelerin veritabanindaki ulke adlari (Company.Country ile eslesir).</summary>
    public List<string> CountryNames() => Countries
        .Select(k => SearchRegions.Get(k).Country)
        .Distinct()
        .ToList();

    /// <summary>Gecersiz bolge/il/ulke adlarini atar (URL'den geldigi icin).</summary>
    public static GeoFilter From(IEnumerable<string>? regions, IEnumerable<string>? cities,
        IEnumerable<string>? countries = null)
    {
        var validCities = TurkishProvinces.All.ToHashSet();
        return new GeoFilter
        {
            Countries = (countries ?? Array.Empty<string>())
                .Where(SearchRegions.IsKnown)
                .Select(k => SearchRegions.Get(k).Key)
                .Distinct()
                .ToList(),
            Regions = (regions ?? Array.Empty<string>())
                .Where(r => TurkishProvinces.Regions.ContainsKey(r)).Distinct().ToList(),
            Cities = (cities ?? Array.Empty<string>())
                .Where(validCities.Contains).Distinct().ToList()
        };
    }

    /// <summary>Filtrelenecek illerin tamami (secili iller + secili bolgelerin illeri).</summary>
    public List<string> EffectiveCities() => Regions
        .SelectMany(r => TurkishProvinces.Regions[r])
        .Concat(Cities)
        .Distinct()
        .ToList();
}

/// <summary>
/// Mevcut sorgu dizesinden yeni URL uretir: filtre ciplerindeki "x" (tek degeri kaldir)
/// ve asama/durum cipleri (bir anahtari degistir) icin. Sayfa her zaman basa doner.
/// </summary>
public static class QueryUrl
{
    public static string Without(Microsoft.AspNetCore.Http.HttpRequest request, string key, string? value = null) =>
        Build(request, (k, v) => !(Eq(k, key) && (value is null || v == value)));

    public static string With(Microsoft.AspNetCore.Http.HttpRequest request, string key, string? value)
    {
        var url = Build(request, (k, _) => !Eq(k, key));
        if (string.IsNullOrEmpty(value)) return url;
        return url + (url.Contains('?') ? "&" : "?") + $"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value)}";
    }

    /// <summary>Ayni filtrelerle baska bir adrese (ör. /Company/Export) giden link.</summary>
    public static string WithPath(Microsoft.AspNetCore.Http.HttpRequest request, string path, string key, string value)
    {
        var current = Build(request, (k, _) => !Eq(k, key));
        var query = current.Contains('?') ? current[current.IndexOf('?')..] + "&" : "?";
        return path + query + $"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value)}";
    }

    private static bool Eq(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static string Build(Microsoft.AspNetCore.Http.HttpRequest request, Func<string, string?, bool> keep)
    {
        var parts = request.Query
            .Where(kv => !Eq(kv.Key, "page"))
            .SelectMany(kv => kv.Value.Where(v => !string.IsNullOrEmpty(v) && keep(kv.Key, v))
                .Select(v => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(v!)}"))
            .ToList();
        return request.Path + (parts.Count > 0 ? "?" + string.Join("&", parts) : "");
    }
}

/// <summary>Liste siralama secenegi.</summary>
public record SortOption(string Key, string Label);

public static class CompanySort
{
    public const string Default = "score";

    public static readonly SortOption[] Options =
    {
        new("score", "Skor (yüksekten düşüğe)"),
        new("score_asc", "Skor (düşükten yükseğe)"),
        new("newest", "En yeni eklenen"),
        new("oldest", "En eski eklenen"),
        new("name", "Ada göre (A → Z)"),
        new("name_desc", "Ada göre (Z → A)"),
        new("contacts", "Kişi sayısı (çoktan aza)"),
        new("city", "Şehre göre (A → Z)")
    };
}

public static class LeadSort
{
    public const string Default = "newest";

    public static readonly SortOption[] Options =
    {
        new("newest", "En yeni oluşturulan"),
        new("oldest", "En eski oluşturulan"),
        new("score", "Skor (yüksekten düşüğe)"),
        new("name", "Kişi adına göre (A → Z)"),
        new("company", "Firmaya göre (A → Z)"),
        new("contacted", "Son iletişim kurulan"),
        new("mailed", "Son mail atılan"),
        new("waiting", "En uzun cevap bekleyen")
    };
}
