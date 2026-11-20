using EgebisLeadFinder.Configuration;
using EgebisLeadFinder.Models;

namespace EgebisLeadFinder.Services.News;

/// <summary>
/// Ücretsiz, kural tabanlı ön sınıflama: kategori ve önem (1-5). Mevcut araştırma kelime listeleri
/// (<see cref="ResearchOptions"/>) kullanılır. Yapay zekâ zenginleştirmesi bunun üstüne eklenir; çalışmasa da haber görünür.
/// </summary>
public static class NewsRuleClassifier
{
    /// <summary>Fabrika/tesis açılışı gibi "hemen görüş" fırsatı sayılan ifadeler.</summary>
    private static readonly string[] HotInvestment =
    {
        "yeni fabrika", "fabrika aç", "fabrikasını aç", "yeni tesis", "tesis aç", "üretim tesisi", "temel atma",
        "kapasite artır", "kapasite artış", "yatırım yap", "yatırıma başl", "yatırım teşvik", "organize sanayi"
    };

    /// <summary>Yatırım durdurma/fabrika kapatma gibi kötü haberler: "yatırım" kelimesi geçse de olumlu sayılmaz.</summary>
    private static readonly string[] HaltRisk =
    {
        "yatırımları durdur", "yatırımlar durdur", "yatırımı durdur", "yatırım durdur", "askıya al", "askıya alın",
        "fabrikasını kapat", "fabrikayı kapat", "fabrikaları kapat", "fabrika kapat", "kapanabilir", "kapatmayı değerlendir",
        "üretime ara ver", "üretimi durdur", "fabrikalar tehlikede", "fabrika riskte", "fabrikalar riskte"
    };

    public static (string Category, int Importance) Classify(string text, ResearchOptions options)
    {
        if (ContainsAny(text, HaltRisk)) return (NewsCategories.Risk, 4);
        if (ContainsAny(text, options.HighRiskKeywords)) return (NewsCategories.Risk, 4);
        if (ContainsAny(text, options.RiskKeywords)) return (NewsCategories.Risk, 3);

        if (ContainsAny(text, HotInvestment)) return (NewsCategories.Investment, 5);
        if (ContainsAny(text, options.GrowthKeywords)) return (NewsCategories.Investment, 3);

        if (ContainsAny(text, options.FinanceKeywords)) return (NewsCategories.Finance, 2);
        if (ContainsAny(text, options.ManagementKeywords)) return (NewsCategories.Management, 2);
        if (ContainsAny(text, options.TechnologyKeywords, wholeWordShort: true)) return (NewsCategories.Technology, 2);
        return (NewsCategories.General, 1);
    }

    private static bool ContainsAny(string text, IEnumerable<string> keywords, bool wholeWordShort = false) =>
        keywords.Any(k => wholeWordShort && k.Length <= 4
            ? System.Text.RegularExpressions.Regex.IsMatch(text, $@"(?<!\p{{L}}){System.Text.RegularExpressions.Regex.Escape(k)}(?!\p{{L}})",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant)
            : text.Contains(k, StringComparison.OrdinalIgnoreCase));
}
