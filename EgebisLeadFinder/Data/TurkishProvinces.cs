using System.Globalization;

namespace EgebisLeadFinder.Data;

/// <summary>Turkiye'nin 81 ili ve 7 cografi bolgesi. Gelismis aramadaki il/bolge secimi icin.</summary>
public static class TurkishProvinces
{
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    public static readonly IReadOnlyDictionary<string, string[]> Regions = new Dictionary<string, string[]>
    {
        ["Marmara"] = new[]
        {
            "Balıkesir", "Bilecik", "Bursa", "Çanakkale", "Edirne", "İstanbul",
            "Kırklareli", "Kocaeli", "Sakarya", "Tekirdağ", "Yalova"
        },
        ["Ege"] = new[]
        {
            "Afyonkarahisar", "Aydın", "Denizli", "İzmir", "Kütahya", "Manisa", "Muğla", "Uşak"
        },
        ["Akdeniz"] = new[]
        {
            "Adana", "Antalya", "Burdur", "Hatay", "Isparta", "Kahramanmaraş", "Mersin", "Osmaniye"
        },
        ["İç Anadolu"] = new[]
        {
            "Aksaray", "Ankara", "Çankırı", "Eskişehir", "Karaman", "Kayseri", "Kırıkkale",
            "Kırşehir", "Konya", "Nevşehir", "Niğde", "Sivas", "Yozgat"
        },
        ["Karadeniz"] = new[]
        {
            "Amasya", "Artvin", "Bartın", "Bayburt", "Bolu", "Çorum", "Düzce", "Giresun", "Gümüşhane",
            "Karabük", "Kastamonu", "Ordu", "Rize", "Samsun", "Sinop", "Tokat", "Trabzon", "Zonguldak"
        },
        ["Doğu Anadolu"] = new[]
        {
            "Ağrı", "Ardahan", "Bingöl", "Bitlis", "Elazığ", "Erzincan", "Erzurum", "Hakkari",
            "Iğdır", "Kars", "Malatya", "Muş", "Tunceli", "Van"
        },
        ["Güneydoğu Anadolu"] = new[]
        {
            "Adıyaman", "Batman", "Diyarbakır", "Gaziantep", "Kilis", "Mardin", "Siirt", "Şanlıurfa", "Şırnak"
        }
    };

    /// <summary>81 il, Turkce alfabetik sirada.</summary>
    public static readonly IReadOnlyList<string> All = Regions.Values
        .SelectMany(v => v)
        .OrderBy(p => p, StringComparer.Create(Tr, ignoreCase: false))
        .ToList();

    /// <summary>Il adindan bolge adina.</summary>
    public static readonly IReadOnlyDictionary<string, string> RegionOf = Regions
        .SelectMany(r => r.Value.Select(p => (Province: p, Region: r.Key)))
        .ToDictionary(x => x.Province, x => x.Region);
}
