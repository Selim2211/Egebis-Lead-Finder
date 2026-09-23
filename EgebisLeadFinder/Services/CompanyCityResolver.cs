using EgebisLeadFinder.Data;
using EgebisLeadFinder.Models;

namespace EgebisLeadFinder.Services;

/// <summary>
/// Bulunan firmanin gercek ilini tahmin eder. Aramada birden fazla il secildiginde
/// "hepsini" firmaya yazmak hem yanlis veri uretiyordu hem de Company.City (100 karakter)
/// sinirini asip tum kaydi dusuruyordu.
/// </summary>
public static class CompanyCityResolver
{
    public static string? Resolve(SearchResult result, string? criteriaCity)
    {
        var selected = (criteriaCity ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        // Haritalar adresi en guvenilir kaynak ("... Nilüfer/Bursa, Türkiye"): firma baska
        // bir ilde olsa bile gercek ili yazilir.
        var fromAddress = FindLast(result.Address, TurkishProvinces.All);
        if (fromAddress is not null) return fromAddress;

        // Arama ozeti/baslik daha gurultulu; secili iller varsa sadece onlar icinde aranir.
        var candidates = selected.Count > 0 ? selected : (IEnumerable<string>)TurkishProvinces.All;
        var fromText = FindLast(result.Snippet, candidates) ?? FindLast(result.Title, candidates);
        if (fromText is not null) return fromText;

        return selected.Count == 1 ? selected[0] : null;
    }

    /// <summary>Metinde gecen illerden en sonda geceni doner (Turkce adreslerde il sondadir).</summary>
    private static string? FindLast(string? text, IEnumerable<string> provinces)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var normalized = TurkishText.Normalize(text);
        string? best = null;
        var bestIndex = -1;

        foreach (var province in provinces)
        {
            if (!TurkishText.ContainsWord(text, province)) continue;

            var index = normalized.LastIndexOf(TurkishText.Normalize(province), StringComparison.Ordinal);
            if (index > bestIndex)
            {
                bestIndex = index;
                best = province;
            }
        }

        return best;
    }
}
