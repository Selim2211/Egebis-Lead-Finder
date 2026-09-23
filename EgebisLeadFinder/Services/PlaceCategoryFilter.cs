namespace EgebisLeadFinder.Services;

/// <summary>
/// Google Haritalar sonuclarini isletme kategorisine gore eler.
/// Haritalar aramasi fabrikalarin yani sira ayni bolgedeki tamirci, galeri,
/// lastikci gibi kucuk isletmeleri de dondurur; bunlar Egebis icin hedef degil
/// ve her biri bosuna site okuma + AI analizi maliyeti demek.
/// </summary>
public static class PlaceCategoryFilter
{
    /// <summary>
    /// Kesin hedef disi kategoriler. Kategori bunlardan birini iceriyorsa
    /// firma daha site okunmadan elenir.
    /// </summary>
    private static readonly string[] ExcludedCategories =
    {
        // Servis / bakim
        "tamirci", "tamir", "servis", "oto yikama", "yikama", "lastikci", "lastik",
        "egzoz", "rot balans", "kaporta", "boyaci", "akü", "oto elektrik",
        "repair", "car wash", "mechanic",

        // Satis / perakende
        "galeri", "bayi", "bayii", "satici", "satis", "showroom", "magaza", "market",
        "yedek parca satis", "dealer", "store", "shop", "rental", "kiralama",
        "arac kiralama", "rent a car", "otopark", "parking", "benzin", "akaryakit",
        "petrol", "gas station",

        // Hizmet / diger
        "restoran", "cafe", "otel", "hastane", "eczane", "okul", "universite",
        "dernek", "vakif", "belediye", "muhasebe", "avukat", "emlak",
        "surucu kursu", "ekspertiz", "sigorta"
    };

    /// <summary>
    /// Uretim/sanayi belirten kategoriler. Bunlardan biri varsa firma
    /// kesinlikle gecer (eleme listesi kontrol edilmez).
    /// </summary>
    private static readonly string[] ManufacturerCategories =
    {
        "fabrika", "uretici", "imalat", "sanayi", "factory", "manufacturer",
        "dokum", "pres", "kalip", "makine uretici", "plastik uretici",
        "metal isleme", "tekstil uretici", "kimya", "muhendislik",
        "industrial", "production"
    };

    /// <summary>Kategori uretim/sanayi belirtiyor mu?</summary>
    public static bool IsManufacturerCategory(string? category) =>
        !string.IsNullOrWhiteSpace(category)
        && ManufacturerCategories.Any(c => TurkishText.ContainsNormalized(category, c));

    /// <summary>
    /// Firma kategorisine gore islenmeye deger mi?
    /// Kategori bos ise elenmez: Haritalar her kayda kategori yazmiyor ve
    /// gercek bir fabrikayi kategori eksikligi yuzunden kaybetmek istemiyoruz.
    /// </summary>
    public static bool IsRelevant(string? category)
    {
        if (string.IsNullOrWhiteSpace(category)) return true;

        // Uretici isareti eleme listesini gecersiz kilar: "Otomobil Parcasi
        // Ureticisi" kategorisi "parca satis" ifadesini de icerebilir ama
        // firma yine de ureticidir.
        if (IsManufacturerCategory(category)) return true;

        return !ExcludedCategories.Any(c => TurkishText.ContainsNormalized(category, c));
    }
}
