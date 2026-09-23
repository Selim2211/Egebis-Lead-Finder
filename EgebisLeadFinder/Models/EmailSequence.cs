using System.ComponentModel.DataAnnotations;

namespace EgebisLeadFinder.Models;

/// <summary>Otomatik mail dizisi: adimlar sirayla, aralarindaki gun farkiyla gonderilir.</summary>
public class EmailSequence
{
    public int Id { get; set; }

    [MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    public bool Active { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<SequenceStep> Steps { get; set; } = new();
}

public class SequenceStep
{
    public int Id { get; set; }

    public int SequenceId { get; set; }
    public EmailSequence? Sequence { get; set; }

    /// <summary>0'dan baslayan sira.</summary>
    public int Order { get; set; }

    /// <summary>Bir onceki adimdan (ilk adimda diziye eklenmeden) kac gun sonra.</summary>
    public int DelayDays { get; set; }

    /// <summary>Kullanilacak sablon; UseAi ise ton/imza ornegi olarak kullanilir.</summary>
    public int? TemplateId { get; set; }
    public EmailTemplate? Template { get; set; }

    /// <summary>Mail gonderim aninda AI ile kisiye ozel yazilir.</summary>
    public bool UseAi { get; set; }
}

public enum LeadSequenceStatus
{
    Active = 0,
    Completed = 1,
    Stopped = 2
}

/// <summary>Bir lead'in bir dizideki ilerleyisi.</summary>
public class LeadSequence
{
    public int Id { get; set; }

    public int LeadId { get; set; }
    public Lead? Lead { get; set; }

    public int SequenceId { get; set; }
    public EmailSequence? Sequence { get; set; }

    /// <summary>Siradaki gonderilecek adimin sirasi (Order).</summary>
    public int CurrentStep { get; set; }

    public DateTime? NextSendAt { get; set; }

    public LeadSequenceStatus Status { get; set; } = LeadSequenceStatus.Active;

    [MaxLength(200)]
    public string? StopReason { get; set; }

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    public DateTime? FinishedAt { get; set; }
}
