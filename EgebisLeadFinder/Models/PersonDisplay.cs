namespace EgebisLeadFinder.Models;

/// <summary>Kisi kartlarindaki bas harf avatari icin kucuk yardimcilar.</summary>
public static class PersonDisplay
{
    public static string Initials(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.StartsWith('(')) return "?";

        // E-posta adresi (ör. info@firma.com): Turkce buyuk harf kurali uygulanmaz ("i" → "I").
        if (name.Contains('@'))
            return char.ToUpperInvariant(name.TrimStart()[0]).ToString();

        var parts = name
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => char.IsLetter(p[0]))
            .ToArray();

        return parts.Length switch
        {
            0 => "?",
            1 => char.ToUpper(parts[0][0], new System.Globalization.CultureInfo("tr-TR")).ToString(),
            _ => string.Concat(
                char.ToUpper(parts[0][0], new System.Globalization.CultureInfo("tr-TR")),
                char.ToUpper(parts[^1][0], new System.Globalization.CultureInfo("tr-TR")))
        };
    }

    /// <summary>Isimden sabit bir avatar rengi (0-5) — ayni kisi her yerde ayni renkte.</summary>
    public static int Hue(string? name)
    {
        var sum = 0;
        foreach (var ch in name ?? string.Empty) sum = (sum * 31 + ch) & 0x7fffffff;
        return sum % 6;
    }

    /// <summary>
    /// E-posta hitabi icin ad soyad ("Sayın Ahmet Yılmaz"). Apollo'nun kismen gizli
    /// soyadi ("At***n") e-postaya yazilmaz, sadece ad kullanilir. Isim yoksa
    /// (info@ gibi genel adres) null doner; cagiran "Yetkili"ye duser.
    /// </summary>
    public static string? SalutationName(Contact? contact)
    {
        var name = contact?.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.StartsWith('(') || name.Contains('@'))
            return null;

        var tr = System.Globalization.CultureInfo.GetCultureInfo("tr-TR");
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();

        if (parts.Any(p => p.Contains('*')))
            parts = parts.TakeWhile(p => !p.Contains('*')).ToList();

        if (parts.Count == 0) return null;

        // "AHMET YILMAZ" / "ahmet yılmaz" → "Ahmet Yılmaz"; karisik yazim oldugu gibi kalir ("McKinsey").
        var letters = string.Concat(parts).Where(char.IsLetter).ToArray();
        if (letters.All(char.IsUpper) || letters.All(char.IsLower))
            parts = parts.Select(p => p.Length == 0 ? p
                : char.ToUpper(p[0], tr) + p[1..].ToLower(tr)).ToList();

        return string.Join(' ', parts);
    }

    public static string DisplayName(Contact? contact) =>
        contact is null ? "(kişi seçilmedi)"
        : !string.IsNullOrWhiteSpace(contact.Name) ? contact.Name!
        : !string.IsNullOrWhiteSpace(contact.Email) ? contact.Email!
        : "(isim yok)";
}
