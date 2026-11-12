namespace EgebisLeadFinder.Services.News;

/// <summary>Ajan panelinin gördüğü anlık durum.</summary>
public record AgentSnapshot(string State, string? CurrentQuery, int Done, int Total, DateTime? LastTickAt, DateTime? NextTickAt, DateTime? RestUntil);

/// <summary>
/// Arka plan ajanının canlı durumu (singleton): şu an ne taranıyor, tur ilerlemesi, sıradaki tur ne zaman.
/// Ekranda "canlı ajan" hissini veren veri buradan gelir; kalıcı değildir.
/// </summary>
public class NewsAgentStatus
{
    public const string Idle = "idle", Scanning = "scanning", Resting = "resting", Off = "off";

    private readonly object _gate = new();
    private string _state = Idle;
    private string? _query;
    private int _done, _total;
    private DateTime? _lastTick, _nextTick, _restUntil;

    public void Disabled()
    {
        lock (_gate) _state = Off;
    }

    public void BeginTick(int total)
    {
        lock (_gate)
        {
            _state = Scanning;
            _done = 0;
            _total = total;
            _query = null;
        }
    }

    public void Progress(string query, int done)
    {
        lock (_gate)
        {
            _query = query;
            _done = done;
        }
    }

    public void EndTick(DateTime utcNow, DateTime nextTick, DateTime? restUntil)
    {
        lock (_gate)
        {
            _lastTick = utcNow;
            _nextTick = nextTick;
            _restUntil = restUntil;
            _state = restUntil is { } r && r > utcNow ? Resting : Idle;
            _query = null;
        }
    }

    public void Schedule(DateTime nextTick)
    {
        lock (_gate) _nextTick = nextTick;
    }

    public AgentSnapshot Snapshot()
    {
        lock (_gate) return new AgentSnapshot(_state, _query, _done, _total, _lastTick, _nextTick, _restUntil);
    }
}
