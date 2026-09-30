namespace EgebisLeadFinder.Models;

/// <summary>
/// Firma detayindaki harita karti icin Google Haritalar adresleri. Anahtar gerektirmeyen
/// herkese acik URL bicimleri kullanilir (gomulu harita "output=embed", tiklama "maps/search").
/// Acik adres yoksa firma adi + sehir + ulke ile aranir (yaklasik konum).
/// </summary>
public sealed class CompanyLocation
{
    public string Query { get; private init; } = string.Empty;

    /// <summary>Kartta gosterilecek adres satiri.</summary>
    public string Label { get; private init; } = string.Empty;

    /// <summary>Acik adres var mi? (yoksa ad + sehirle yaklasik arama)</summary>
    public bool IsExact { get; private init; }

    public string EmbedUrl => $"https://maps.google.com/maps?q={Uri.EscapeDataString(Query)}&hl=tr&z={(IsExact ? 16 : 13)}&output=embed";

    public string OpenUrl => $"https://www.google.com/maps/search/?api=1&query={Uri.EscapeDataString(Query)}";

    public string DirectionsUrl => $"https://www.google.com/maps/dir/?api=1&destination={Uri.EscapeDataString(Query)}";

    /// <summary>Firmanin konumu; ne adres ne sehir/ulke varsa null (harita gosterilmez).</summary>
    public static CompanyLocation? For(Company company)
    {
        var address = company.Address?.Trim();
        if (!string.IsNullOrEmpty(address))
        {
            // Haritalar adresi genelde ulkeyi icerir; icermiyorsa ulke eklenir ki baska ulkedeki ayni sokak cikmasin.
            var withCountry = string.IsNullOrWhiteSpace(company.Country) || address.Contains(company.Country.Trim(), StringComparison.OrdinalIgnoreCase)
                ? address
                : $"{address}, {company.Country!.Trim()}";
            return new CompanyLocation
            {
                Query = $"{company.Name.Trim()}, {withCountry}".Trim(' ', ','),
                Label = address,
                IsExact = true
            };
        }

        var place = new[] { company.City, company.Country }
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (place.Count == 0) return null;

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(company.Name)) parts.Add(company.Name.Trim());
        parts.AddRange(place);

        return new CompanyLocation
        {
            Query = string.Join(", ", parts),
            Label = string.Join(", ", place),
            IsExact = false
        };
    }
}
