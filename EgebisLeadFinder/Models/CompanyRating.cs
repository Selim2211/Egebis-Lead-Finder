using System.Text.Json.Serialization;

namespace EgebisLeadFinder.Models;

/// <summary>
/// AI #3 ciktisi: firma on arastirmasinin yapisal sonucu. Toplanan kaynak
/// snippet'lerinden (haber, KAP, resmi kayit) uretilir. Gemini'ye responseSchema
/// olarak bu sekil verilir.
///
/// Nihai <see cref="Signal"/> degeri AI'in onerisi degildir: CompanyRatingEvaluator
/// deterministik kurallardan gecirip belirler (LeadScoringService felsefesi).
/// </summary>
public class CompanyRating
{
    /// <summary>AI onerisi: "guclu" | "incelenmeli" | "riskli". Nihai deger evaluator'dan gelir.</summary>
    [JsonPropertyName("signal")]
    public string Signal { get; set; } = "incelenmeli";

    /// <summary>1-2 cumlelik Turkce ozet degerlendirme.</summary>
    [JsonPropertyName("summary")]
    public string? Summary { get; set; }

    /// <summary>Kurulus yili, merkez, faaliyet alani.</summary>
    [JsonPropertyName("foundingInfo")]
    public string? FoundingInfo { get; set; }

    /// <summary>Tahmini calisan sayisi, sube/fabrika bilgisi.</summary>
    [JsonPropertyName("scaleInfo")]
    public string? ScaleInfo { get; set; }

    /// <summary>Ciro/kar/sermaye bilgisi varsa. Yoksa bos.</summary>
    [JsonPropertyName("financialInfo")]
    public string? FinancialInfo { get; set; }

    /// <summary>Finansal bilginin kaynagi: "KAP" | "haber" | "yok".</summary>
    [JsonPropertyName("financialSource")]
    public string FinancialSource { get; set; } = "yok";

    /// <summary>Ortaklik yapisi ve onemli yoneticiler.</summary>
    [JsonPropertyName("owners")]
    public string? Owners { get; set; }

    /// <summary>Bilinen musteriler / referanslar.</summary>
    [JsonPropertyName("customers")]
    public List<string> Customers { get; set; } = new();

    /// <summary>Is yaptigi veya birlikte proje yuruttugu firmalar.</summary>
    [JsonPropertyName("suppliers")]
    public List<string> Suppliers { get; set; } = new();

    /// <summary>Son donem buyuk projeler / yatirimlar.</summary>
    [JsonPropertyName("projects")]
    public List<string> Projects { get; set; } = new();

    /// <summary>Yeni yatirim, ise alim, kapasite artisi gibi buyume isaretleri.</summary>
    [JsonPropertyName("growthSignals")]
    public List<string> GrowthSignals { get; set; } = new();

    /// <summary>Konkordato, icra, dava, odeme problemi, kapanma gibi risk isaretleri.</summary>
    [JsonPropertyName("riskSignals")]
    public List<RiskSignal> RiskSignals { get; set; } = new();

    /// <summary>Hangi kaynaklar kontrol edildi ve veri dondu mu? Seffaflik icin.</summary>
    [JsonPropertyName("checkedSources")]
    public List<CheckedSource> CheckedSources { get; set; } = new();

    // --- Asagidakiler AI semasinda YOK; CompanyResearchService degerlendirmeden
    //     sonra doldurur ve RatingJson icine yazar. ---

    /// <summary>Deterministik degerlendiricinin gerekce satirlari.</summary>
    [JsonPropertyName("evaluatorNotes")]
    public List<string> EvaluatorNotes { get; set; } = new();

    /// <summary>Kullanicinin elle acabilecegi resmi kaynak linkleri.</summary>
    [JsonPropertyName("registryLinks")]
    public List<RatingLink> RegistryLinks { get; set; } = new();

    /// <summary>
    /// Gerekce satirlari + isaret ettikleri yon. Eski RatingJson satirlarinda bos
    /// kalir; view o zaman yonsuz <see cref="EvaluatorNotes"/> listesine duser.
    /// </summary>
    [JsonPropertyName("evaluatorNotesV2")]
    public List<EvaluatorNote> EvaluatorNotesV2 { get; set; } = new();

    /// <summary>0-100 arasi guven skoru: kaynak + bilgi parcasi + pozitif kanit sayisindan.</summary>
    [JsonPropertyName("confidence")]
    public int? Confidence { get; set; }

    /// <summary>Guven skorunun turedigi ham sayilar (seffaflik icin).</summary>
    [JsonPropertyName("confidenceInputs")]
    public ConfidenceInputs? ConfidenceInputs { get; set; }

    /// <summary>Her kaynagin topladigi ham parcalar (drill-down icin). Eski satirlarda bos.</summary>
    [JsonPropertyName("sourceSnippets")]
    public List<SourceSnippetGroup> SourceSnippets { get; set; } = new();

    /// <summary>KAP / haber finansal tablolari ve turetilen oranlar. Yoksa null.</summary>
    [JsonPropertyName("financials")]
    public FinancialsBlock? Financials { get; set; }

    /// <summary>En az bir yuksek onemli risk sinyali var mi?</summary>
    [JsonIgnore]
    public bool HasHighRisk =>
        RiskSignals.Any(r => string.Equals(r.Severity, "yuksek", StringComparison.OrdinalIgnoreCase));

    /// <summary>Finansal veri KAP veya haberden dogrulandi mi?</summary>
    [JsonIgnore]
    public bool HasFinancialData =>
        !string.IsNullOrWhiteSpace(FinancialInfo)
        && !FinancialSource.Equals("yok", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Bir gerekce satiri ve isaret ettigi yon ("guclu" | "riskli" | "incelenmeli" | "bilinmiyor").</summary>
public class EvaluatorNote
{
    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty;

    [JsonPropertyName("direction")]
    public string Direction { get; set; } = "incelenmeli";
}

/// <summary>Guven skorunun turedigi ham sayilar.</summary>
public class ConfidenceInputs
{
    [JsonPropertyName("sourceCount")]
    public int SourceCount { get; set; }

    [JsonPropertyName("snippetCount")]
    public int SnippetCount { get; set; }

    [JsonPropertyName("positiveCount")]
    public int PositiveCount { get; set; }
}

/// <summary>Tek bir kaynagin topladigi ham parcalar (drill-down icin).</summary>
public class SourceSnippetGroup
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("foundSomething")]
    public bool FoundSomething { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("snippets")]
    public List<SourceSnippetItem> Snippets { get; set; } = new();
}

public class SourceSnippetItem
{
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("kind")]
    public string? Kind { get; set; }

    [JsonPropertyName("date")]
    public string? Date { get; set; }
}

/// <summary>KAP / haber finansal tablolari ve turetilen oranlar (Faz 5).</summary>
public class FinancialsBlock
{
    /// <summary>"KAP" | "haber".</summary>
    [JsonPropertyName("source")]
    public string Source { get; set; } = "yok";

    [JsonPropertyName("currency")]
    public string Currency { get; set; } = "TRY";

    /// <summary>Eskiden yeniye siralı 2-3 donem.</summary>
    [JsonPropertyName("periods")]
    public List<FinancialPeriod> Periods { get; set; } = new();

    [JsonPropertyName("ratios")]
    public FinancialRatios? Ratios { get; set; }

    /// <summary>KAP bildirim sayfasi linki (varsa).</summary>
    [JsonPropertyName("disclosureUrl")]
    public string? DisclosureUrl { get; set; }

    [JsonIgnore]
    public bool HasPeriods => Periods.Count > 0;
}

public class FinancialPeriod
{
    /// <summary>"2024/12" gibi.</summary>
    [JsonPropertyName("period")]
    public string? Period { get; set; }

    [JsonPropertyName("revenue")]
    public decimal? Revenue { get; set; }

    [JsonPropertyName("netProfit")]
    public decimal? NetProfit { get; set; }

    [JsonPropertyName("equity")]
    public decimal? Equity { get; set; }

    [JsonPropertyName("totalAssets")]
    public decimal? TotalAssets { get; set; }
}

public class FinancialRatios
{
    /// <summary>Net kar / hasilat.</summary>
    [JsonPropertyName("netMargin")]
    public decimal? NetMargin { get; set; }

    /// <summary>Net kar / ozkaynak.</summary>
    [JsonPropertyName("roe")]
    public decimal? Roe { get; set; }

    /// <summary>1 - ozkaynak / toplam varlik.</summary>
    [JsonPropertyName("leverage")]
    public decimal? Leverage { get; set; }
}

/// <summary>Firma hakkinda bulunan tek bir risk isareti, kaynagiyla birlikte.</summary>
public class RiskSignal
{
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    /// <summary>"yuksek" | "orta" | "dusuk".</summary>
    [JsonPropertyName("severity")]
    public string Severity { get; set; } = "orta";

    [JsonPropertyName("sourceUrl")]
    public string? SourceUrl { get; set; }
}

/// <summary>"Kendin ac" turu resmi kaynak linki.</summary>
public class RatingLink
{
    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;
}

/// <summary>On arastirmada kontrol edilen bir kaynak ve sonucu.</summary>
public class CheckedSource
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("foundSomething")]
    public bool FoundSomething { get; set; }
}

/// <summary>
/// On arastirma sinyali: firmanin ticari/finansal saglik durumu.
/// Lead puanindan (Egebis'e uygunluk) bagimsizdir.
/// </summary>
public enum RatingSignal
{
    /// <summary>Yeterli veri yok; sinyal uretilemedi.</summary>
    Bilinmiyor = 0,

    /// <summary>Guclu: koklu, finansal olarak saglikli, risk isareti yok.</summary>
    Guclu = 1,

    /// <summary>Incelenmeli: veri sinirli veya karisik; elle bakilmali.</summary>
    Incelenmeli = 2,

    /// <summary>Riskli: dogrulanmis risk isareti var (konkordato, icra, iflas vb.).</summary>
    Riskli = 3
}

/// <summary>
/// Liste / panel gibi yerlerde Company.RatingSignal string kolonundan (jsonb
/// ayristirmadan) kucuk renkli nokta ve etiket uretmek icin yardimci.
/// </summary>
public static class RatingSignalDisplay
{
    public static RatingSignal Parse(string? value) =>
        Enum.TryParse<RatingSignal>(value, ignoreCase: true, out var s) ? s : RatingSignal.Bilinmiyor;

    public static string PipCss(string? value) => Parse(value) switch
    {
        RatingSignal.Guclu => "signal-strong",
        RatingSignal.Riskli => "signal-risk",
        RatingSignal.Incelenmeli => "signal-review",
        _ => "signal-unknown"
    };

    public static string Label(string? value) => Parse(value) switch
    {
        RatingSignal.Guclu => "Güçlü",
        RatingSignal.Riskli => "Riskli",
        RatingSignal.Incelenmeli => "İncelenmeli",
        _ => "Bilinmiyor"
    };

    public static bool HasSignal(string? value) => Parse(value) != RatingSignal.Bilinmiyor;

    /// <summary>Son arastirma tarihi verilen gun esiginden eskiyse true.</summary>
    public static bool IsStale(DateTime? ratedAt, int stalenessDays) =>
        ratedAt is { } dt && dt < DateTime.UtcNow.AddDays(-Math.Max(1, stalenessDays));
}
