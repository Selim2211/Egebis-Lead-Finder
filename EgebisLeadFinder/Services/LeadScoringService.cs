using EgebisLeadFinder.Configuration;
using EgebisLeadFinder.Models;
using Microsoft.Extensions.Options;

namespace EgebisLeadFinder.Services;

/// <summary>
/// Puanlamayi AI'a birakmiyoruz; agirliklar appsettings'ten okunan deterministik
/// bir algoritma calisiyor (dokuman bolum 10). Hedef kitle SAP kullanan ureticiler
/// oldugu icin en yuksek agirlik SAP kaniti.
/// </summary>
public class LeadScoringService
{
    private readonly ScoringOptions _options;

    public LeadScoringService(IOptions<ScoringOptions> options) => _options = options.Value;

    /// <summary>Firma icin 0-100 arasi lead puani hesaplar.</summary>
    public ScoreBreakdown ScoreCompany(CompanyAnalysis? analysis, ScrapedSite? site, IEnumerable<Contact>? contacts)
    {
        var breakdown = new ScoreBreakdown();

        if (analysis is not null)
        {
            // AI firmayi hedef disi bulduysa (bayi, haber sitesi, is ilani platformu,
            // kamu kurumu) puanlamaya hic girmez. "SAP" aramasi bu tur siteleri
            // bolca getirdigi icin bu eleme kritik.
            if (!analysis.Potential)
            {
                breakdown.Disqualify(analysis.Reason ?? "Egebis için hedef müşteri değil");
                return breakdown;
            }

            // SAP hizmeti satan firmalar Egebis'in rakibi, musterisi degil.
            if (analysis.SapVendor)
            {
                breakdown.Disqualify("SAP hizmeti satan firma (rakip)");
                return breakdown;
            }

            // SAP puani yalnizca ureticilere verilir: SAP'tan soz eden bir yazilim
            // veya danismanlik sitesi bu puani almamali.
            if (analysis.Manufacturer)
            {
                if (analysis.UsesSap)
                    breakdown.Add("SAP kullanımı doğrulandı", _options.SapFound);
                else if (analysis.LikelyUsesSap)
                    breakdown.Add("SAP kullanma ihtimali yüksek", _options.SapFound / 2);

                breakdown.Add("Üretici firma", _options.Manufacturer);
            }

            if (IsTargetIndustry(analysis.Industry))
                breakdown.Add("Hedef sektör", _options.TargetIndustry);

            if (IsLargeCompany(analysis.EmployeeSizeHint))
                breakdown.Add("Büyük ölçekli firma", _options.LargeCompany);
        }

        else
        {
            // AI analizi yoksa firmanin hedef olup olmadigi dogrulanamaz.
            // Iletisim puanlari yine verilir ama firma "doğrulanmadı" olarak isaretlenir,
            // boylece analiz edilmis gercek adaylarin onune gecemez.
            breakdown.MarkUnverified();
        }

        var contactList = contacts?.ToList() ?? new List<Contact>();

        // IT/SAP tarafinda muhatap bulmak dogrudan satis avantajidir.
        if (contactList.Any(c => c.TitleScore >= 80))
            breakdown.Add("IT/SAP yöneticisi bulundu", _options.ItManagerFound);

        var hasEmail = contactList.Any(c => !string.IsNullOrWhiteSpace(c.Email))
                       || (site?.Emails.Count > 0);
        if (hasEmail)
            breakdown.Add("E-posta bulundu", _options.EmailFound);

        return breakdown;
    }

    /// <summary>
    /// Unvana gore kisi onceligi. AI #2 yerine kod (dokuman bolum 25).
    /// En uzun eslesen anahtar kazanir: "bilgi işlem müdürü" > "bilgi işlem".
    /// </summary>
    public int ScoreTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return 0;

        return _options.TitleScores
            .Where(kv => TurkishText.ContainsWord(title, kv.Key))
            .OrderByDescending(kv => kv.Key.Length)
            .Select(kv => kv.Value)
            .FirstOrDefault();
    }

    /// <summary>Egebis icin en uygun muhatabi secer.</summary>
    public Contact? PickBestContact(IEnumerable<Contact> contacts) =>
        contacts
            .OrderByDescending(c => c.TitleScore)
            // Esit unvan puaninda e-postasi olan kisi tercih edilir.
            .ThenByDescending(c => string.IsNullOrWhiteSpace(c.Email) ? 0 : 1)
            .FirstOrDefault();

    private bool IsTargetIndustry(string? industry)
    {
        if (string.IsNullOrWhiteSpace(industry)) return false;
        return _options.TargetIndustryKeywords.Any(k => TurkishText.ContainsNormalized(industry, k));
    }

    /// <summary>
    /// AI'dan gelen serbest metinli calisan sayisi ipucunu degerlendirir
    /// ("500+ çalışan", "10.000'in üzerinde çalışan").
    /// </summary>
    private static bool IsLargeCompany(string? employeeSizeHint)
    {
        if (string.IsNullOrWhiteSpace(employeeSizeHint)) return false;

        var digits = new string(employeeSizeHint.Where(ch => char.IsDigit(ch) || ch == '.' || ch == ',').ToArray())
            .Replace(".", string.Empty)
            .Replace(",", string.Empty);

        return int.TryParse(digits, out var count) && count >= 100;
    }
}

/// <summary>Puan ve puanin nasil olustugunu gosteren kalemler.</summary>
public class ScoreBreakdown
{
    public List<(string Reason, int Points)> Items { get; } = new();

    /// <summary>Firma hedef disi bulunduysa nedeni; doluysa puan sifirdir.</summary>
    public string? DisqualifiedReason { get; private set; }

    /// <summary>AI analizi yapilamadi; firma profili dogrulanmis degil.</summary>
    public bool Unverified { get; private set; }

    /// <summary>
    /// Dogrulanmamis firmalarin ust siniri. AI analizi olan gercek adaylarin
    /// altinda kalmalari icin dusuk tutulur.
    /// </summary>
    private const int UnverifiedCap = 15;

    public int Total
    {
        get
        {
            if (DisqualifiedReason is not null) return 0;

            var sum = Math.Min(100, Items.Sum(i => i.Points));
            return Unverified ? Math.Min(UnverifiedCap, sum) : sum;
        }
    }

    public void Add(string reason, int points)
    {
        if (points > 0) Items.Add((reason, points));
    }

    /// <summary>Firmayi listeden dusurur; kayit silinmez, sadece puani sifirlanir.</summary>
    public void Disqualify(string reason) => DisqualifiedReason = reason;

    /// <summary>AI analizi yapilamadigini isaretler ve puani tavanla sinirlar.</summary>
    public void MarkUnverified() => Unverified = true;
}
