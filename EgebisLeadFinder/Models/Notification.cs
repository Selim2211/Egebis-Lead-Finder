using System.ComponentModel.DataAnnotations;

namespace EgebisLeadFinder.Models;

/// <summary>
/// Kullaniciya bildirim (sag ustteki zil). Simdilik tek tur: lead mailimize cevap verdi. Her alici kullanici icin ayri
/// satir tutulur (okundu bilgisi kisiye ozel). Kisi adi sifreli saklanir.
/// </summary>
public class Notification
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public AppUser? User { get; set; }

    [MaxLength(30)]
    public string Kind { get; set; } = NotificationKinds.Reply;

    public int? LeadId { get; set; }

    [MaxLength(300)]
    public string? CompanyName { get; set; }

    public string? PersonName { get; set; }

    /// <summary>Cevabin ilk satirlari (bildirim listesinde kisa onizleme). Sifreli.</summary>
    public string? Preview { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReadAt { get; set; }
}

public static class NotificationKinds
{
    public const string Reply = "reply";
}
