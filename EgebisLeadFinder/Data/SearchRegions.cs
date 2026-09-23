namespace EgebisLeadFinder.Data;

/// <summary>
/// Sorgu kaliplari. {0} = sektor, {1} = konum (sehir/ulke). Her bolge kendi
/// dilindeki kaliplari kullanir: "makina üreticileri İzmir" ile
/// "machinery manufacturers Bavaria" ayni sonucu vermez.
/// </summary>
public record QueryPack(string Code, string[] Web, string[] Places);

/// <summary>
/// Arama bolgesi: hangi ulkede, hangi Google ulke/dil kodlariyla ve hangi dildeki
/// sorgularla arama yapilacagi. Ayarlar ekranindan secilir.
/// </summary>
public record SearchRegion(
    string Key,
    string Name,
    string Country,
    string Gl,
    string Hl,
    string PackCode,
    string Group,
    bool HasProvinces = false)
{
    public QueryPack Pack => SearchRegions.Pack(PackCode);
}

public static class SearchRegions
{
    public const string DefaultKey = "TR";

    private static readonly QueryPack Turkish = new("tr",
        Web: new[]
        {
            "\"{0} üreticileri\" {1}",
            "\"{0} yan sanayi\" {1} firma",
            "{0} fabrikası {1}",
            "{0} üretim tesisi {1}",
            "\"organize sanayi\" {0} {1} üretici",
            "{0} imalat sanayi {1} \"A.Ş.\"",
            "\"SAP\" kariyer {0} {1} fabrika"
        },
        Places: new[]
        {
            "{0} yan sanayi {1}",
            "{0} fabrikası {1}",
            "{0} üretici {1}",
            "{0} sanayi {1}",
            "{1} organize sanayi bölgesi {0}"
        });

    private static readonly QueryPack English = new("en",
        Web: new[]
        {
            "\"{0} manufacturers\" {1}",
            "\"{0} supplier\" {1} company",
            "{0} factory {1}",
            "{0} production plant {1}",
            "industrial zone {0} {1} manufacturer",
            "{0} manufacturing company {1} \"Ltd\"",
            "\"SAP\" careers {0} {1} factory"
        },
        Places: new[]
        {
            "{0} manufacturer {1}",
            "{0} factory {1}",
            "{0} supplier {1}",
            "{0} industry {1}",
            "{1} industrial park {0}"
        });

    private static readonly QueryPack German = new("de",
        Web: new[]
        {
            "\"{0} Hersteller\" {1}",
            "\"{0} Zulieferer\" {1} Unternehmen",
            "{0} Fabrik {1}",
            "{0} Produktionsstandort {1}",
            "Industriegebiet {0} {1} Hersteller",
            "{0} Fertigung {1} \"GmbH\"",
            "\"SAP\" Karriere {0} {1} Werk"
        },
        Places: new[]
        {
            "{0} Hersteller {1}",
            "{0} Fabrik {1}",
            "{0} Zulieferer {1}",
            "{0} Industrie {1}",
            "{1} Industriegebiet {0}"
        });

    private static readonly Dictionary<string, QueryPack> Packs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["tr"] = Turkish,
        ["en"] = English,
        ["de"] = German
    };

    /// <summary>Bilinmeyen dil kodu Ingilizce kaliplara duser.</summary>
    public static QueryPack Pack(string code) => Packs.TryGetValue(code, out var p) ? p : English;

    /// <summary>
    /// Desteklenen bolgeler. Dili kaliplari olmayan ulkelerde Ingilizce sorgu kullanilir;
    /// B2B sitelerinin cogunda Ingilizce sayfa bulunur.
    /// </summary>
    public static readonly IReadOnlyList<SearchRegion> All = new List<SearchRegion>
    {
        new("TR", "Türkiye", "Türkiye", "tr", "tr", "tr", "Türkiye", HasProvinces: true),

        new("DE", "Almanya", "Deutschland", "de", "de", "de", "Avrupa"),
        new("AT", "Avusturya", "Österreich", "at", "de", "de", "Avrupa"),
        new("CH", "İsviçre", "Schweiz", "ch", "de", "de", "Avrupa"),
        new("NL", "Hollanda", "Netherlands", "nl", "en", "en", "Avrupa"),
        new("BE", "Belçika", "Belgium", "be", "en", "en", "Avrupa"),
        new("FR", "Fransa", "France", "fr", "fr", "en", "Avrupa"),
        new("IT", "İtalya", "Italy", "it", "it", "en", "Avrupa"),
        new("ES", "İspanya", "Spain", "es", "es", "en", "Avrupa"),
        new("GB", "Birleşik Krallık", "United Kingdom", "gb", "en", "en", "Avrupa"),
        new("PL", "Polonya", "Poland", "pl", "pl", "en", "Avrupa"),
        new("CZ", "Çekya", "Czechia", "cz", "cs", "en", "Avrupa"),
        new("RO", "Romanya", "Romania", "ro", "ro", "en", "Avrupa"),
        new("BG", "Bulgaristan", "Bulgaria", "bg", "bg", "en", "Avrupa"),
        new("GR", "Yunanistan", "Greece", "gr", "el", "en", "Avrupa"),
        new("SE", "İsveç", "Sweden", "se", "sv", "en", "Avrupa"),

        new("AE", "Birleşik Arap Emirlikleri", "United Arab Emirates", "ae", "en", "en", "Orta Doğu & Afrika"),
        new("SA", "Suudi Arabistan", "Saudi Arabia", "sa", "en", "en", "Orta Doğu & Afrika"),
        new("QA", "Katar", "Qatar", "qa", "en", "en", "Orta Doğu & Afrika"),
        new("EG", "Mısır", "Egypt", "eg", "en", "en", "Orta Doğu & Afrika"),
        new("MA", "Fas", "Morocco", "ma", "fr", "en", "Orta Doğu & Afrika"),

        new("AZ", "Azerbaycan", "Azerbaijan", "az", "az", "en", "Asya & Avrasya"),
        new("KZ", "Kazakistan", "Kazakhstan", "kz", "ru", "en", "Asya & Avrasya"),
        new("RU", "Rusya", "Russia", "ru", "ru", "en", "Asya & Avrasya"),
        new("IN", "Hindistan", "India", "in", "en", "en", "Asya & Avrasya"),

        new("US", "Amerika Birleşik Devletleri", "United States", "us", "en", "en", "Amerika"),
        new("CA", "Kanada", "Canada", "ca", "en", "en", "Amerika"),
        new("MX", "Meksika", "Mexico", "mx", "es", "en", "Amerika"),
        new("BR", "Brezilya", "Brazil", "br", "pt", "en", "Amerika")
    };

    public static SearchRegion Default => Get(DefaultKey);

    /// <summary>Bilinmeyen/bos anahtar Türkiye'ye duser.</summary>
    public static SearchRegion Get(string? key) =>
        All.FirstOrDefault(r => string.Equals(r.Key, key, StringComparison.OrdinalIgnoreCase))
        ?? All.First(r => r.Key == DefaultKey);

    public static bool IsKnown(string? key) =>
        key is not null && All.Any(r => string.Equals(r.Key, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>Ulke adindan bolgeyi bulur (eski kayitlarda yalnizca ulke adi var).</summary>
    public static SearchRegion? ByCountry(string? country) =>
        string.IsNullOrWhiteSpace(country)
            ? null
            : All.FirstOrDefault(r =>
                string.Equals(r.Country, country, StringComparison.OrdinalIgnoreCase)
                || string.Equals(r.Name, country, StringComparison.OrdinalIgnoreCase));

    /// <summary>Ayarlar ekranindaki gruplu acilir liste icin.</summary>
    public static IEnumerable<IGrouping<string, SearchRegion>> Grouped() => All.GroupBy(r => r.Group);
}
