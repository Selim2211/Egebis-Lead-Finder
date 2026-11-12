namespace EgebisLeadFinder.Services.News;

/// <summary>
/// Ana kaynağın (RSS) sağlığı. Üst üste 3 hata → 60 dakika dinlenir; dinlenirken yedek kaynağa (Serper) DÜŞÜLMEZ, böylece bir engel
/// kredi yakmaz. Yedek, dinlenme bittikten sonra RSS yine hata verirse ve günlük tavan dolmadıysa kullanılır.
/// Singleton: tur sonrası da durumu hatırlar.
/// </summary>
public class NewsSourceHealth
{
    public const int FailuresBeforeRest = 3;
    public static readonly TimeSpan RestDuration = TimeSpan.FromMinutes(60);

    private readonly object _gate = new();
    private int _failures;
    private bool _hasRested;
    private DateTime? _restUntil;
    private DateOnly _serperDay;
    private int _serperToday;

    public bool IsResting(DateTime utcNow)
    {
        lock (_gate) return _restUntil is { } until && utcNow < until;
    }

    public DateTime? RestUntil
    {
        get { lock (_gate) return _restUntil; }
    }

    public void RecordSuccess()
    {
        lock (_gate)
        {
            _failures = 0;
            _hasRested = false;
            _restUntil = null;
        }
    }

    /// <summary>Yedek kaynağa düşülebilir mi? Yalnızca bir dinlenme yaşandıysa ve şu an dinlenmiyorsak (kaynak hâlâ bozuk).</summary>
    public bool FallbackAllowed(DateTime utcNow)
    {
        lock (_gate) return _hasRested && !(_restUntil is { } until && utcNow < until);
    }

    /// <summary>Hatayı kaydeder; eşik aşıldıysa dinlenmeye alır ve true döner (tur kesilmeli).</summary>
    public bool RecordFailure(DateTime utcNow)
    {
        lock (_gate)
        {
            _failures++;
            if (_failures < FailuresBeforeRest) return false;
            _failures = 0;
            _hasRested = true;
            _restUntil = utcNow + RestDuration;
            return true;
        }
    }

    /// <summary>Yedek kaynak için günlük (Türkiye günü) hak var mı? Varsa bir hak harcar.</summary>
    public bool TryUseSerper(DateTime utcNow, int dailyCap)
    {
        var day = DateOnly.FromDateTime(utcNow.AddHours(NewsSlots.TurkeyOffsetHours));
        lock (_gate)
        {
            if (_serperDay != day) { _serperDay = day; _serperToday = 0; }
            if (_serperToday >= dailyCap) return false;
            _serperToday++;
            return true;
        }
    }
}
