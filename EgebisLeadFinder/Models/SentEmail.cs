using System.ComponentModel.DataAnnotations;

namespace EgebisLeadFinder.Models;

public enum SentEmailMethod
{
    /// <summary>Uygulama uzerinden SMTP ile gonderildi.</summary>
    Smtp = 0,

    /// <summary>Kullanici disarida (Outlook vb.) gonderip elle isaretledi.</summary>
    Manual = 1
}

/// <summary>Bir lead'e atilan e-postanin kalici kopyasi (lead profilindeki e-posta gecmisi).</summary>
public class SentEmail
{
    public int Id { get; set; }

    public int LeadId { get; set; }
    public Lead? Lead { get; set; }

    [MaxLength(255)]
    public string? FromAddress { get; set; }

    [MaxLength(255)]
    public string ToAddress { get; set; } = string.Empty;

    [MaxLength(500)]
    public string Subject { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    /// <summary>Gorselli/bicimli govde (temizlenmis HTML); duz metin gonderimde null.</summary>
    public string? BodyHtml { get; set; }

    public int? TemplateId { get; set; }

    [MaxLength(150)]
    public string? TemplateName { get; set; }

    /// <summary>E-postaya eklenen gorsellerin adlari (virgulle ayrilmis).</summary>
    [MaxLength(500)]
    public string? ImageNames { get; set; }

    public SentEmailMethod Method { get; set; } = SentEmailMethod.Smtp;

    public DateTime SentAt { get; set; } = DateTime.UtcNow;
}
