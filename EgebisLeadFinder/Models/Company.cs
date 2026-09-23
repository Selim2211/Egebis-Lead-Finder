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

    public List<Contact> Contacts { get; set; } = new();
    public List<Lead> Leads { get; set; } = new();
}
