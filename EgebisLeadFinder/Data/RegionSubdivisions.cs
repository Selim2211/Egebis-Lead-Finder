namespace EgebisLeadFinder.Data;

/// <summary>
/// Türkiye dışındaki Avrupa ülkeleri için bölge / eyalet / kanton listeleri (arama "Gelişmiş Arama" ekranında çoklu seçilir).
/// Adlar ülkenin kendi dilindedir: sorgular o ülkenin dilinde kurulduğu için ("Bayern", "Île-de-France") Google en iyi sonucu verir.
/// Gruplar yalnızca seçimi kolaylaştıran hızlı düğmelerdir.
/// </summary>
public static class RegionSubdivisions
{
    public record Group(string Name, string[] Items);

    private static readonly Dictionary<string, Group[]> Data = new(StringComparer.OrdinalIgnoreCase)
    {
        ["DE"] = new[]
        {
            new Group("Güney", new[] { "Baden-Württemberg", "Bayern" }),
            new Group("Batı", new[] { "Nordrhein-Westfalen", "Rheinland-Pfalz", "Saarland", "Hessen" }),
            new Group("Kuzey", new[] { "Niedersachsen", "Bremen", "Hamburg", "Schleswig-Holstein", "Mecklenburg-Vorpommern" }),
            new Group("Doğu", new[] { "Berlin", "Brandenburg", "Sachsen", "Sachsen-Anhalt", "Thüringen" })
        },
        ["AT"] = new[]
        {
            new Group("Doğu", new[] { "Wien", "Niederösterreich", "Burgenland" }),
            new Group("Güney", new[] { "Steiermark", "Kärnten" }),
            new Group("Batı", new[] { "Tirol", "Vorarlberg", "Salzburg" }),
            new Group("Kuzey", new[] { "Oberösterreich" })
        },
        ["CH"] = new[]
        {
            new Group("Almanca bölgesi", new[] { "Zürich", "Bern", "Aargau", "Basel-Stadt", "Basel-Landschaft", "Luzern", "St. Gallen", "Thurgau", "Solothurn",
                "Schaffhausen", "Zug", "Schwyz", "Graubünden", "Appenzell", "Glarus", "Nidwalden", "Obwalden", "Uri" }),
            new Group("Fransızca bölgesi", new[] { "Genève", "Vaud", "Valais", "Fribourg", "Neuchâtel", "Jura" }),
            new Group("İtalyanca bölgesi", new[] { "Ticino" })
        },
        ["NL"] = new[]
        {
            new Group("Randstad", new[] { "Noord-Holland", "Zuid-Holland", "Utrecht", "Flevoland" }),
            new Group("Güney", new[] { "Noord-Brabant", "Limburg", "Zeeland" }),
            new Group("Doğu", new[] { "Gelderland", "Overijssel" }),
            new Group("Kuzey", new[] { "Groningen", "Friesland", "Drenthe" })
        },
        ["BE"] = new[]
        {
            new Group("Flaman bölgesi", new[] { "Antwerpen", "Oost-Vlaanderen", "West-Vlaanderen", "Vlaams-Brabant", "Limburg" }),
            new Group("Valon bölgesi", new[] { "Hainaut", "Liège", "Namur", "Luxembourg", "Brabant wallon" }),
            new Group("Brüksel", new[] { "Brussel" })
        },
        ["FR"] = new[]
        {
            new Group("Kuzey", new[] { "Hauts-de-France", "Normandie", "Île-de-France", "Grand Est" }),
            new Group("Batı", new[] { "Bretagne", "Pays de la Loire", "Nouvelle-Aquitaine", "Centre-Val de Loire" }),
            new Group("Doğu / Orta", new[] { "Bourgogne-Franche-Comté", "Auvergne-Rhône-Alpes" }),
            new Group("Güney", new[] { "Occitanie", "Provence-Alpes-Côte d'Azur", "Corse" })
        },
        ["IT"] = new[]
        {
            new Group("Kuzey", new[] { "Piemonte", "Valle d'Aosta", "Lombardia", "Liguria", "Veneto", "Trentino-Alto Adige", "Friuli-Venezia Giulia", "Emilia-Romagna" }),
            new Group("Orta", new[] { "Toscana", "Umbria", "Marche", "Lazio", "Abruzzo" }),
            new Group("Güney ve adalar", new[] { "Molise", "Campania", "Puglia", "Basilicata", "Calabria", "Sicilia", "Sardegna" })
        },
        ["ES"] = new[]
        {
            new Group("Kuzey", new[] { "Galicia", "Asturias", "Cantabria", "País Vasco", "Navarra", "La Rioja", "Aragón" }),
            new Group("Doğu", new[] { "Cataluña", "Comunidad Valenciana", "Illes Balears", "Región de Murcia" }),
            new Group("Orta", new[] { "Comunidad de Madrid", "Castilla y León", "Castilla-La Mancha", "Extremadura" }),
            new Group("Güney ve adalar", new[] { "Andalucía", "Canarias" })
        },
        ["GB"] = new[]
        {
            new Group("İngiltere", new[] { "London", "South East", "South West", "East of England", "East Midlands", "West Midlands",
                "North West", "North East", "Yorkshire and the Humber" }),
            new Group("Diğer ülkeler", new[] { "Scotland", "Wales", "Northern Ireland" })
        },
        ["PL"] = new[]
        {
            new Group("Güney", new[] { "Dolnośląskie", "Opolskie", "Śląskie", "Małopolskie" }),
            new Group("Doğu", new[] { "Podkarpackie", "Lubelskie", "Podlaskie", "Świętokrzyskie" }),
            new Group("Orta", new[] { "Mazowieckie", "Łódzkie", "Wielkopolskie", "Kujawsko-Pomorskie", "Lubuskie" }),
            new Group("Kuzey", new[] { "Pomorskie", "Zachodniopomorskie", "Warmińsko-Mazurskie" })
        },
        ["CZ"] = new[]
        {
            new Group("Bohemya", new[] { "Praha", "Středočeský kraj", "Jihočeský kraj", "Plzeňský kraj", "Karlovarský kraj", "Ústecký kraj",
                "Liberecký kraj", "Královéhradecký kraj", "Pardubický kraj" }),
            new Group("Moravya", new[] { "Vysočina", "Jihomoravský kraj", "Olomoucký kraj", "Zlínský kraj", "Moravskoslezský kraj" })
        },
        ["RO"] = new[]
        {
            new Group("Büyük sanayi şehirleri / illeri", new[] { "București", "Cluj", "Timiș", "Brașov", "Iași", "Constanța", "Sibiu", "Argeș", "Prahova", "Dolj", "Bihor", "Mureș" })
        },
        ["BG"] = new[]
        {
            new Group("Büyük sanayi şehirleri", new[] { "Sofia", "Plovdiv", "Varna", "Burgas", "Ruse", "Stara Zagora", "Pleven", "Veliko Tarnovo", "Gabrovo" })
        },
        ["GR"] = new[]
        {
            new Group("Anakara", new[] { "Attica", "Central Macedonia", "Thessaly", "Western Greece", "Central Greece", "Peloponnese", "Epirus",
                "Eastern Macedonia and Thrace", "Western Macedonia" }),
            new Group("Adalar", new[] { "Crete", "Ionian Islands", "North Aegean", "South Aegean" })
        },
        ["SE"] = new[]
        {
            new Group("Güney", new[] { "Skåne", "Blekinge", "Halland", "Kronoberg", "Kalmar", "Jönköping", "Gotland" }),
            new Group("Orta", new[] { "Stockholm", "Uppsala", "Södermanland", "Västmanland", "Örebro", "Östergötland", "Västra Götaland", "Värmland" }),
            new Group("Kuzey", new[] { "Dalarna", "Gävleborg", "Jämtland", "Västernorrland", "Västerbotten", "Norrbotten" })
        }
    };

    public static bool Has(string? regionKey) => !string.IsNullOrEmpty(regionKey) && Data.ContainsKey(regionKey);

    public static IReadOnlyList<Group> For(string? regionKey) =>
        regionKey is not null && Data.TryGetValue(regionKey, out var groups) ? groups : Array.Empty<Group>();

    /// <summary>Tüm ülkelerin listesi (sayfaya JSON olarak gömülür; bölge değişince panel buradan kurulur).</summary>
    public static IReadOnlyDictionary<string, Group[]> All => Data;
}
