namespace EgebisLeadFinder.Data;

/// <summary>
/// Sorgu kaliplari. {0} = sektor, {1} = konum (sehir/ulke). Her bolge kendi
/// dilindeki kaliplari kullanir: "makina üreticileri İzmir" ile
/// "machinery manufacturers Bavaria" ayni sonucu vermez.
/// Web/Places uretici odakli kaliplardir ("üreticileri", "fabrikası"); GeneralWeb/GeneralPlaces
/// her tur sirket icin ("firmaları", "şirketleri"): musterisi uretici olmayan kullanicilar
/// (toptanci, hizmet firmasi, insaat tedarikcisi...) bunlari kullanir.
/// </summary>
public record QueryPack(string Code, string[] Web, string[] Places, string[] GeneralWeb, string[] GeneralPlaces);

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
            "{0} imalatı {1}",
            "{0} üretim tesisi {1}",
            "fason {0} üretimi {1}",
            "{0} manufacturer {1}",
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
        },
        GeneralWeb: new[]
        {
            "\"{0}\" firmaları {1}",
            "{0} şirketleri {1}",
            "{0} firması {1} iletişim",
            "{0} {1} \"Ltd. Şti.\"",
            "{0} {1} \"A.Ş.\"",
            "{0} hizmetleri {1}"
        },
        GeneralPlaces: new[]
        {
            "{0} {1}",
            "{0} firmaları {1}",
            "{0} şirketi {1}"
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
        },
        GeneralWeb: new[]
        {
            "\"{0}\" companies {1}",
            "{0} company {1} contact",
            "{0} {1} \"Ltd\"",
            "{0} services {1}",
            "{0} firms {1}"
        },
        GeneralPlaces: new[]
        {
            "{0} {1}",
            "{0} company {1}"
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
        },
        GeneralWeb: new[]
        {
            "{0} Unternehmen {1}",
            "{0} Firma {1} Kontakt",
            "{0} {1} \"GmbH\"",
            "{0} Dienstleister {1}"
        },
        GeneralPlaces: new[]
        {
            "{0} {1}",
            "{0} Firma {1}"
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
        new("PT", "Portekiz", "Portugal", "pt", "pt", "en", "Avrupa"),
        new("GB", "Birleşik Krallık", "United Kingdom", "gb", "en", "en", "Avrupa"),
        new("IE", "İrlanda", "Ireland", "ie", "en", "en", "Avrupa"),
        new("PL", "Polonya", "Poland", "pl", "pl", "en", "Avrupa"),
        new("CZ", "Çekya", "Czechia", "cz", "cs", "en", "Avrupa"),
        new("SK", "Slovakya", "Slovakia", "sk", "sk", "en", "Avrupa"),
        new("HU", "Macaristan", "Hungary", "hu", "hu", "en", "Avrupa"),
        new("SI", "Slovenya", "Slovenia", "si", "sl", "en", "Avrupa"),
        new("HR", "Hırvatistan", "Croatia", "hr", "hr", "en", "Avrupa"),
        new("RS", "Sırbistan", "Serbia", "rs", "sr", "en", "Avrupa"),
        new("RO", "Romanya", "Romania", "ro", "ro", "en", "Avrupa"),
        new("BG", "Bulgaristan", "Bulgaria", "bg", "bg", "en", "Avrupa"),
        new("GR", "Yunanistan", "Greece", "gr", "el", "en", "Avrupa"),
        new("SE", "İsveç", "Sweden", "se", "sv", "en", "Avrupa"),
        new("DK", "Danimarka", "Denmark", "dk", "da", "en", "Avrupa"),
        new("NO", "Norveç", "Norway", "no", "no", "en", "Avrupa"),
        new("FI", "Finlandiya", "Finland", "fi", "fi", "en", "Avrupa"),
        new("UA", "Ukrayna", "Ukraine", "ua", "uk", "en", "Avrupa"),
        new("RU", "Rusya", "Russia", "ru", "ru", "en", "Avrupa"),
        new("AZ", "Azerbaycan", "Azerbaijan", "az", "az", "en", "Asya"),
        new("GE", "Gürcistan", "Georgia", "ge", "ka", "en", "Asya"),
        new("KZ", "Kazakistan", "Kazakhstan", "kz", "ru", "en", "Asya"),
        new("UZ", "Özbekistan", "Uzbekistan", "uz", "ru", "en", "Asya"),
        new("IN", "Hindistan", "India", "in", "en", "en", "Asya"),
        new("PK", "Pakistan", "Pakistan", "pk", "en", "en", "Asya"),
        new("CN", "Çin", "China", "cn", "en", "en", "Asya"),
        new("JP", "Japonya", "Japan", "jp", "en", "en", "Asya"),
        new("KR", "Güney Kore", "South Korea", "kr", "en", "en", "Asya"),
        new("VN", "Vietnam", "Vietnam", "vn", "en", "en", "Asya"),
        new("TH", "Tayland", "Thailand", "th", "en", "en", "Asya"),
        new("MY", "Malezya", "Malaysia", "my", "en", "en", "Asya"),
        new("ID", "Endonezya", "Indonesia", "id", "en", "en", "Asya"),
        new("SG", "Singapur", "Singapore", "sg", "en", "en", "Asya"),
        new("AE", "Birleşik Arap Emirlikleri", "United Arab Emirates", "ae", "en", "en", "Orta Doğu"),
        new("SA", "Suudi Arabistan", "Saudi Arabia", "sa", "en", "en", "Orta Doğu"),
        new("QA", "Katar", "Qatar", "qa", "en", "en", "Orta Doğu"),
        new("KW", "Kuveyt", "Kuwait", "kw", "en", "en", "Orta Doğu"),
        new("OM", "Umman", "Oman", "om", "en", "en", "Orta Doğu"),
        new("JO", "Ürdün", "Jordan", "jo", "en", "en", "Orta Doğu"),
        new("IQ", "Irak", "Iraq", "iq", "en", "en", "Orta Doğu"),
        new("IL", "İsrail", "Israel", "il", "en", "en", "Orta Doğu"),
        new("EG", "Mısır", "Egypt", "eg", "en", "en", "Afrika"),
        new("MA", "Fas", "Morocco", "ma", "fr", "en", "Afrika"),
        new("DZ", "Cezayir", "Algeria", "dz", "fr", "en", "Afrika"),
        new("TN", "Tunus", "Tunisia", "tn", "fr", "en", "Afrika"),
        new("NG", "Nijerya", "Nigeria", "ng", "en", "en", "Afrika"),
        new("KE", "Kenya", "Kenya", "ke", "en", "en", "Afrika"),
        new("ZA", "Güney Afrika", "South Africa", "za", "en", "en", "Afrika"),
        new("US", "Amerika Birleşik Devletleri", "United States", "us", "en", "en", "Kuzey Amerika"),
        new("CA", "Kanada", "Canada", "ca", "en", "en", "Kuzey Amerika"),
        new("MX", "Meksika", "Mexico", "mx", "es", "en", "Kuzey Amerika"),
        new("BR", "Brezilya", "Brazil", "br", "pt", "en", "Güney Amerika"),
        new("AR", "Arjantin", "Argentina", "ar", "es", "en", "Güney Amerika"),
        new("CL", "Şili", "Chile", "cl", "es", "en", "Güney Amerika"),
        new("CO", "Kolombiya", "Colombia", "co", "es", "en", "Güney Amerika"),
        new("AU", "Avustralya", "Australia", "au", "en", "en", "Okyanusya"),
        new("NZ", "Yeni Zelanda", "New Zealand", "nz", "en", "en", "Okyanusya")
    };

    /// <summary>Kıtalar (ekrandaki sekmeler), gösterim sırasıyla. Türkiye ayrı sekme.</summary>
    public static readonly IReadOnlyList<string> Continents = new[]
    {
        "Türkiye", "Avrupa", "Asya", "Orta Doğu", "Afrika", "Kuzey Amerika", "Güney Amerika", "Okyanusya"
    };

    /// <summary>Tek aramada en fazla bu kadar ülke (her ülke ayrı taranır; kredi ülke sayısıyla artar).</summary>
    public const int MaxCountriesPerSearch = 30;

    /// <summary>Virgüllü ülke anahtarlarını ("DE,FR,IT") bilinen bölgelere çevirir; sıra korunur, tekrarlar ve bilinmeyenler atılır.</summary>
    public static List<SearchRegion> ParseKeys(string? csv) =>
        (csv ?? string.Empty).Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(IsKnown).Select(Get).DistinctBy(r => r.Key).ToList();

    /// <summary>
    /// Seçimin okunur adı: tek ülke → "Almanya"; bir kıtanın tüm ülkeleri → "Avrupa (tüm ülkeler)";
    /// diğerleri → "Almanya, Fransa, İtalya" (3'ten fazlaysa "+2").
    /// </summary>
    public static string Describe(IReadOnlyList<SearchRegion> regions)
    {
        if (regions.Count == 0) return Default.Name;
        if (regions.Count == 1) return regions[0].Name;
        var groups = regions.Select(r => r.Group).Distinct().ToList();
        if (groups.Count == 1 && All.Count(r => r.Group == groups[0]) == regions.Count) return $"{groups[0]} (tüm ülkeler)";
        var names = regions.Select(r => r.Name).ToList();
        return names.Count <= 3 ? string.Join(", ", names) : $"{string.Join(", ", names.Take(3))} +{names.Count - 3}";
    }

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
    public static IEnumerable<IGrouping<string, SearchRegion>> Grouped() =>
        All.GroupBy(r => r.Group).OrderBy(g => Continents.ToList().IndexOf(g.Key));
}
