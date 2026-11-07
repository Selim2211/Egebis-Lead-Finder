using System.ComponentModel.DataAnnotations;

namespace EgebisLeadFinder.Models;

public enum InboundEmailKind { Reply, AutoReply }

/// <summary>
/// Lead'e gonderdigimiz maile gelen cevap (gelen kutusundan IMAP ile okunur). Mesajlar ekranindaki sohbette
/// "onlarin" balonudur. Govde, alintilanan eski yazisma ("… yazdı:" sonrasi) temizlenmis duz metindir.
/// Adres, ad ve govde sifreli saklanir (kisisel veri).
/// </summary>
public class InboundEmail
{
    public int Id { get; set; }

    public int LeadId { get; set; }
    public Lead? Lead { get; set; }

    /// <summary>Cevaplanan bizim mailimiz (In-Reply-To ile eslesirse).</summary>
    public int? SentEmailId { get; set; }
    public SentEmail? SentEmail { get; set; }

    /// <summary>Gelen mailin Message-ID'si (kucuk harf, &lt;&gt; olmadan); ayni mail iki kez kaydedilmez.</summary>
    [MaxLength(300)]
    public string? MessageId { get; set; }

    public string FromAddress { get; set; } = string.Empty;
    public string? FromName { get; set; }

    [MaxLength(500)]
    public string? Subject { get; set; }

    public string Body { get; set; } = string.Empty;

    public InboundEmailKind Kind { get; set; } = InboundEmailKind.Reply;

    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Sohbet acilinca dolar (okunmamis rozeti icin).</summary>
    public DateTime? ReadAt { get; set; }
}
