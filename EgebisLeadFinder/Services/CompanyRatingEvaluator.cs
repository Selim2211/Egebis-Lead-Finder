using EgebisLeadFinder.Configuration;
using EgebisLeadFinder.Models;
using EgebisLeadFinder.Services.CompanyIntel;
using Microsoft.Extensions.Options;

namespace EgebisLeadFinder.Services;

/// <summary>
/// On arastirma sinyalini AI'a birakmiyoruz. AI oneri verir; nihai sinyali
/// deterministik kurallar belirler (LeadScoringService felsefesi). Ayni girdi
/// her zaman ayni sinyali uretir ve gerekce kalem kalem gosterilebilir.
/// </summary>
public class CompanyRatingEvaluator
{
    private readonly ResearchOptions _options;

    public CompanyRatingEvaluator(IOptions<ResearchOptions> options) => _options = options.Value;

    /// <summary>
    /// AI ciktisi + toplanan ham parcalardan nihai sinyali ve gerekce dokumunu uretir.
    /// </summary>
    public RatingEvaluation Evaluate(CompanyRating rating, IReadOnlyList<IntelSnippet> snippets)
    {
        var result = new RatingEvaluation();

        // Guven skoru her donus yolunda ayni sekilde hesaplanir. Pozitif kanit
        // sayisini (yan etkisiz okumalar) bastan cikariyoruz; result.Add cagrilari
        // yerlerinde kaliyor ki gerekce sirasi bozulmasin.
        var sourceCount = rating.CheckedSources.Count(c => c.FoundSomething);
        var hasFinancial = rating.HasFinancialData;
        var hasGrowth = rating.GrowthSignals.Count > 0
            || snippets.Any(s => s.Kind == IntelKind.Buyume);
        var hasEstablished = LooksEstablished(rating);
        var hasActivity = rating.Customers.Count > 0 || rating.Projects.Count > 0
            || snippets.Count(s => s.Kind is IntelKind.Haber or IntelKind.Buyume) >= 2;
        var positiveCount = new[] { hasFinancial, hasGrowth, hasEstablished, hasActivity }.Count(x => x);

        RatingEvaluation Done()
        {
            result.SourceCount = sourceCount;
            result.SnippetCount = snippets.Count;
            result.PositiveCount = positiveCount;
            result.Confidence = ComputeConfidence(sourceCount, snippets.Count, positiveCount);
            return result;
        }

        // 1) RISKLI icin AI'in dogruladigi, kaynagi olan yuksek-onem risk sinyali sart.
        //    Ham haber snippet'i tek basina RISKLI yapmaz: risk-kelimesi sorgusu her
        //    firma icin bir seyler getirir ("konkordato haberleri" listesi, adı geçen
        //    baska firma vb.). Bu snippet'ler yalnizca "en fazla İncelenmeli" tavani koyar.
        //    Company-specific yorumu AI yapiyor; guvenilir kaynak orada.
        var confirmedHighRisk = rating.RiskSignals
            .Where(r => IsHighSeverity(r) && !string.IsNullOrWhiteSpace(r.SourceUrl))
            .ToList();

        if (confirmedHighRisk.Count > 0)
        {
            foreach (var r in confirmedHighRisk)
                result.Add($"Doğrulanmış yüksek risk: {r.Text}", RatingSignal.Riskli);
            result.Signal = RatingSignal.Riskli;
            return Done();
        }

        // 2) Risk sinyallerini seviyeye gore ayir.
        //    - "orta": haber/kaynak var ama kritik degil -> en fazla İncelenmeli tavani
        //    - "yuksek" ama kaynaksiz: AI uydurmus olabilir, orta muamelesi
        //    - "dusuk": AI'a gore belirsiz/soylenti -> yalnizca bilgi notu, tavan koymaz
        var mediumRisk = rating.RiskSignals
            .Where(r => IsMediumSeverity(r) || (IsHighSeverity(r) && string.IsNullOrWhiteSpace(r.SourceUrl)))
            .ToList();
        var lowRisk = rating.RiskSignals.Where(IsLowSeverity).ToList();

        // Ham haber snippet'i tavani ancak AI da en az bir "orta" sinyal
        // dogruladiysa koyar. Yoksa risk-kelimesi sorgusunun genel gurultusudur.
        var hasRiskKeywordSnippet = mediumRisk.Count > 0 && snippets.Any(s =>
            s.Kind == IntelKind.Risk && ContainsAny(s.Text, _options.RiskKeywords));

        var hasMediumRisk = mediumRisk.Count > 0 || hasRiskKeywordSnippet;

        foreach (var r in mediumRisk)
            result.Add($"Orta seviye risk işareti: {r.Text}", RatingSignal.Incelenmeli);
        foreach (var r in lowRisk)
            result.Add($"Düşük seviye / doğrulanmamış risk işareti: {r.Text}", RatingSignal.Incelenmeli);

        // 3) Pozitif kanit (booleanlar metodun basinda hesaplandi) gerekce olarak eklenir.
        if (hasFinancial) result.Add($"Finansal veri bulundu ({rating.FinancialSource})", RatingSignal.Guclu);
        if (hasGrowth) result.Add("Büyüme / yatırım işareti", RatingSignal.Guclu);
        if (hasEstablished) result.Add("Köklü / kurumsal firma işareti", RatingSignal.Guclu);
        if (hasActivity) result.Add("Aktif ticari faaliyet işareti", RatingSignal.Guclu);

        // KAP'tan gelen finansal veri denetlenmis ve en guclu sinyaldir. Buyuk
        // firmalarda rutin dava/icra kayitlari her aramada cikar; KAP finansali +
        // birden fazla pozitif varken bunlar tek basina firmayi "İncelenmeli"ye
        // cekmemeli. Yuksek-onem risk zaten yukarida RISKLI ile ele alindi.
        var kapBackedStrong = rating.HasFinancialData
            && rating.FinancialSource.Equals("KAP", StringComparison.OrdinalIgnoreCase)
            && positiveCount >= 3;

        // 4) Karar.
        if (hasMediumRisk && !kapBackedStrong)
        {
            result.Signal = RatingSignal.Incelenmeli;
            result.Add("Risk işareti nedeniyle en fazla 'İncelenmeli'", RatingSignal.Incelenmeli);
            return Done();
        }

        if (hasMediumRisk)
            result.Add("Orta seviye risk var ama KAP finansalı + güçlü pozitifler baskın", RatingSignal.Guclu);

        // Kanitsiz "Guclu" olamaz (LeadScoringService.UnverifiedCap deseni).
        if (positiveCount == 0)
        {
            result.Signal = snippets.Count == 0 ? RatingSignal.Bilinmiyor : RatingSignal.Incelenmeli;
            result.Add(
                snippets.Count == 0
                    ? "Yeterli veri bulunamadı; sinyal üretilemedi"
                    : "Doğrulanmış olumlu kanıt yok; elle incelenmeli",
                result.Signal);
            return Done();
        }

        // Guclu icin en az iki bagimsiz pozitif kanit (finansal + baska bir eksen).
        if (hasFinancial && positiveCount >= 2)
        {
            result.Signal = RatingSignal.Guclu;
            return Done();
        }

        if (positiveCount >= 3)
        {
            result.Signal = RatingSignal.Guclu;
            return Done();
        }

        result.Signal = RatingSignal.Incelenmeli;
        result.Add("Olumlu işaretler var ama 'Güçlü' için yeterli kanıt yok", RatingSignal.Incelenmeli);
        return Done();
    }

    private int ComputeConfidence(int sourceCount, int snippetCount, int positiveCount) =>
        Math.Clamp(
            _options.ConfidenceSourceWeight * Math.Min(sourceCount, 3)
            + _options.ConfidenceSnippetWeight * Math.Min(snippetCount, 10)
            + _options.ConfidencePositiveWeight * Math.Min(positiveCount, 4),
            0, 100);

    private static bool IsHighSeverity(RiskSignal r) =>
        string.Equals(r.Severity, "yuksek", StringComparison.OrdinalIgnoreCase);

    private static bool IsMediumSeverity(RiskSignal r) =>
        string.Equals(r.Severity, "orta", StringComparison.OrdinalIgnoreCase);

    private static bool IsLowSeverity(RiskSignal r) =>
        string.Equals(r.Severity, "dusuk", StringComparison.OrdinalIgnoreCase)
        || string.Equals(r.Severity, "düşük", StringComparison.OrdinalIgnoreCase);

    private bool LooksEstablished(CompanyRating rating)
    {
        var text = $"{rating.FoundingInfo} {rating.ScaleInfo} {rating.Summary}";
        if (string.IsNullOrWhiteSpace(text)) return false;

        // "1985 yılında kuruldu", "40 yıllık", "1000 çalışan" gibi izler.
        var years = System.Text.RegularExpressions.Regex.Matches(text, @"\b(19\d{2}|20[0-2]\d)\b");
        foreach (System.Text.RegularExpressions.Match m in years)
            if (int.TryParse(m.Value, out var y) && DateTime.UtcNow.Year - y >= 10)
                return true;

        return text.Contains("köklü", StringComparison.OrdinalIgnoreCase)
            || System.Text.RegularExpressions.Regex.IsMatch(text, @"\b\d{2,}\s*yıl");
    }

    private static bool ContainsAny(string text, IEnumerable<string> keywords) =>
        keywords.Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Nihai sinyal + nasil olustugunu gosteren gerekce kalemleri.</summary>
public class RatingEvaluation
{
    public RatingSignal Signal { get; set; } = RatingSignal.Bilinmiyor;

    public List<(string Reason, RatingSignal Direction)> Items { get; } = new();

    public void Add(string reason, RatingSignal direction) => Items.Add((reason, direction));

    /// <summary>DB'ye yazilacak kisa metin.</summary>
    public string SignalText => Signal.ToString();

    /// <summary>0-100 guven skoru (kaynak + bilgi parcasi + pozitif kanit sayisindan).</summary>
    public int Confidence { get; set; }

    /// <summary>Veri donduren kaynak sayisi.</summary>
    public int SourceCount { get; set; }

    /// <summary>Toplanan bilgi parcasi sayisi.</summary>
    public int SnippetCount { get; set; }

    /// <summary>Pozitif kanit ekseni sayisi (0-4).</summary>
    public int PositiveCount { get; set; }
}
