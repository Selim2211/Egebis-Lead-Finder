using System.ComponentModel.DataAnnotations;

namespace EgebisLeadFinder.Models;

public enum LeadStatus
{
    Yeni = 0,
    Incelendi = 1,
    EmailHazir = 2,
    Gonderildi = 3,
    Ilgilendi = 4,
    Ilgilenmedi = 5
}

/// <summary>Firma + kisi eslesmesinden dogan satis firsati.</summary>
public class Lead
{
    public int Id { get; set; }

    public int CompanyId { get; set; }
    public Company? Company { get; set; }

    public int? ContactId { get; set; }
    public Contact? Contact { get; set; }

    public int Score { get; set; }

    public LeadStatus Status { get; set; } = LeadStatus.Yeni;

    public int? SelectedTemplateId { get; set; }
    public EmailTemplate? SelectedTemplate { get; set; }

    [MaxLength(2000)]
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SentAt { get; set; }

    /// <summary>Kisiyle (telefon, LinkedIn, yuz yuze vb.) iletisim kuruldugu an.</summary>
    public DateTime? ContactedAt { get; set; }

    /// <summary>Kisiden cevap alindigi an; dolu ise lead takip listesinden cikar.</summary>
    public DateTime? RepliedAt { get; set; }

    /// <summary>Bu tarihe kadar takip listesinde gosterilmez ("ertele").</summary>
    public DateTime? SnoozedUntil { get; set; }

    // --- CRM (Salesforce) senkronu. Null = hic gonderilmedi. ---

    /// <summary>Salesforce'taki karsilik gelen Lead kaydinin Id'si.</summary>
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

    /// <summary>Bu lead'e atilan e-postalar (en yenisi SentAt'e gore).</summary>
    public List<SentEmail> SentEmails { get; set; } = new();

    /// <summary>Son temas ani: en son gonderilen e-posta, yoksa SentAt / ContactedAt.</summary>
    public DateTime? LastTouchAt =>
        new[] { SentEmails.Count > 0 ? SentEmails.Max(e => e.SentAt) : (DateTime?)null, SentAt, ContactedAt }
            .Where(d => d is not null)
            .DefaultIfEmpty(null)
            .Max();

    /// <summary>Son temastan bu yana gecen tam gun; hic temas yoksa null.</summary>
    public int? WaitingDays(DateTime? now = null) => LastTouchAt is null
        ? null
        : Math.Max(0, (int)((now ?? DateTime.UtcNow) - LastTouchAt.Value).TotalDays);

    /// <summary>
    /// Takip edilmeli mi: mail/temas var, cevap gelmemis, erteleme suresi dolmus,
    /// lead kapanmamis ve bekleme suresi esigi asmis.
    /// </summary>
    public bool NeedsFollowUp(int afterDays, DateTime? now = null)
    {
        var moment = now ?? DateTime.UtcNow;

        if (RepliedAt is not null) return false;
        if (Status is LeadStatus.Ilgilendi or LeadStatus.Ilgilenmedi) return false;
        if (SnoozedUntil is not null && SnoozedUntil > moment) return false;

        return WaitingDays(moment) >= afterDays;
    }
}

public static class LeadStatusDisplay
{
    public static string Label(LeadStatus status) => status switch
    {
        LeadStatus.Yeni => "Yeni",
        LeadStatus.Incelendi => "İncelendi",
        LeadStatus.EmailHazir => "E-posta hazır",
        LeadStatus.Gonderildi => "Mail atıldı",
        LeadStatus.Ilgilendi => "İlgilendi",
        LeadStatus.Ilgilenmedi => "İlgilenmedi",
        _ => status.ToString()
    };

    public static string Css(LeadStatus status) => status switch
    {
        LeadStatus.Gonderildi => "lead-status-sent",
        LeadStatus.Ilgilendi => "lead-status-won",
        LeadStatus.Ilgilenmedi => "lead-status-lost",
        LeadStatus.EmailHazir => "lead-status-ready",
        _ => "lead-status-new"
    };
}
