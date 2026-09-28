using System.ComponentModel.DataAnnotations;

namespace EgebisLeadFinder.Models;

/// <summary>
/// Aramalar sonucu bulunan firma. AI analizi ham JSON olarak AiAnalysis kolonunda tutulur.
/// </summary>
public class Company
{
    public int Id { get; set; }

    [Required, MaxLength(300)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Website { get; set; }

    /// <summary>Website'in normalize edilmis domaini. Tekillestirme icin kullanilir.</summary>
    [MaxLength(255)]
    public string? Domain { get; set; }

    [MaxLength(150)]
    public string? Industry { get; set; }

    /// <summary>NACE Rev.2 faaliyet kodu ("22.19"); AI analizinden gelir.</summary>
    [MaxLength(10)]
    public string? NaceCode { get; set; }

    /// <summary>Son puanlamada Ayarlar'daki ideal musteri profiline (ICP) uydu mu?</summary>
    public bool IcpMatch { get; set; }

    [MaxLength(100)]
    public string? City { get; set; }

    [MaxLength(100)]
    public string? Country { get; set; }

    public string? Description { get; set; }

    /// <summary>AI analizinin ham JSON ciktisi (jsonb).</summary>
    public string? AiAnalysis { get; set; }

    public int Score { get; set; }

    /// <summary>Scrape veya AI adiminda olusan hata. Null ise sorun yok.</summary>
    public string? ProcessingError { get; set; }

    /// <summary>
    /// Firma gercekten degerlendirildi mi? Puan 0 iki farkli anlama gelebilir: hedef disi
    /// oldugu icin elendi ya da site okunamadigi/servis hata verdigi icin hic incelenemedi.
    /// </summary>
    public EvaluationStatus EvaluationStatus { get; set; } = EvaluationStatus.Evaluated;

    /// <summary>Eleme nedeni veya neden incelenemedigi (kullaniciya gosterilir).</summary>
    [MaxLength(500)]
    public string? EvaluationNote { get; set; }

    // --- Faz-II: on arastirma / rating. Lead puanindan bagimsiz bir eksen. ---

    /// <summary>On arastirma AI ciktisinin ham JSON'i (jsonb). Null ise henuz arastirilmadi.</summary>
    public string? RatingJson { get; set; }

    /// <summary>
    /// Deterministik degerlendirme sonucu: "Guclu" | "Incelenmeli" | "Riskli".
    /// Liste ve panelde jsonb ayristirmadan gostermek icin ayri kolonda tutulur.
    /// </summary>
    [MaxLength(20)]
    public string? RatingSignal { get; set; }

    /// <summary>Son on arastirma tarihi. Null ise hic arastirilmadi.</summary>
    public DateTime? RatedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // --- Satis takibi (Firmalar kartlarindaki butonlar). Null = henuz olmadi. ---

    public DateTime? ContactedAt { get; set; }
    public DateTime? EmailSentAt { get; set; }
    public DateTime? ProjectStartedAt { get; set; }

    // --- CRM (Salesforce) senkronu. Null = hic gonderilmedi. ---

    /// <summary>Salesforce'taki karsilik gelen Account kaydinin Id'si.</summary>
    [MaxLength(30)]
    public string? SalesforceId { get; set; }

    public DateTime? SalesforceSyncedAt { get; set; }

    /// <summary>Son senkron denemesinin sonucu (basarili ise null; hata varsa mesaj).</summary>
    [MaxLength(500)]
    public string? SalesforceSyncError { get; set; }

    /// <summary>Son gonderimden sonra degisti mi? Arka plan senkronu bunlari gonderir.</summary>
    public bool SalesforceDirty { get; set; }

    /// <summary>Son senkron denemesi (basarili/basarisiz); hatali kayit hemen tekrar denenmesin diye.</summary>
    public DateTime? SalesforceAttemptAt { get; set; }

    public List<Contact> Contacts { get; set; } = new();
    public List<Lead> Leads { get; set; } = new();

    public void MarkNotEvaluated(string note)
    {
        EvaluationStatus = EvaluationStatus.NotEvaluated;
        EvaluationNote = note.Length > 500 ? note[..500] : note;
    }
}

public enum EvaluationStatus
{
    /// <summary>Site okundu, yapay zeka analiz etti, puanlandi.</summary>
    Evaluated = 0,

    /// <summary>Degerlendirildi ve hedef disi bulundu (bayi, rakip, ICP disi...).</summary>
    Disqualified = 1,

    /// <summary>Site okunamadi, servis hata verdi veya kota doldu: firma hakkinda karar verilmedi.</summary>
    NotEvaluated = 2
}

public static class EvaluationDisplay
{
    public static bool IsNotEvaluated(Company c) => c.EvaluationStatus == EvaluationStatus.NotEvaluated;
}
