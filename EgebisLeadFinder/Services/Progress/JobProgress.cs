using System.Collections.Concurrent;

namespace EgebisLeadFinder.Services.Progress;

/// <summary>Uzun suren bir islemin (firma aramasi, on arastirma) anlik durumu.</summary>
public enum JobState { Running, Done, Failed }

/// <summary>Servislerin ilerleme bildirmek icin kullandigi tek adim.</summary>
public readonly record struct JobStep(int Percent, string Stage, string? Detail = null);

public class JobProgress
{
    public string Id { get; init; } = Guid.NewGuid().ToString("n");
    public int Percent { get; set; }
    public string Stage { get; set; } = "Başlatılıyor…";
    public string? Detail { get; set; }
    public JobState State { get; set; } = JobState.Running;
    public string? Error { get; set; }
    public string? ResultUrl { get; set; }

    /// <summary>Islem bitince tasinan sonuc (orn. DiscoveryResult). UI yeniden cizerken kullanir.</summary>
    public object? Payload { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Arka planda calisan islerin ilerlemesini tutan basit bellek-ici depo.
/// Kuyruk/Redis yok: tek sunucu MVP. Eski kayitlar 30 dakikada bir temizlenir.
/// </summary>
public class JobProgressStore
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(30);
    private readonly ConcurrentDictionary<string, JobProgress> _jobs = new();

    public JobProgress Create()
    {
        Prune();
        var job = new JobProgress();
        _jobs[job.Id] = job;
        return job;
    }

    public JobProgress? Get(string id) =>
        _jobs.TryGetValue(id, out var job) ? job : null;

    public void Report(string id, int percent, string stage, string? detail = null)
    {
        if (!_jobs.TryGetValue(id, out var job) || job.State != JobState.Running) return;
        job.Percent = Math.Clamp(percent, 0, 100);
        job.Stage = stage;
        if (detail is not null) job.Detail = detail;
        job.UpdatedAt = DateTime.UtcNow;
    }

    public void Complete(string id, string resultUrl, object? payload = null)
    {
        if (!_jobs.TryGetValue(id, out var job)) return;
        job.Percent = 100;
        job.State = JobState.Done;
        job.Stage = "Tamamlandı";
        job.ResultUrl = resultUrl;
        job.Payload = payload;
        job.UpdatedAt = DateTime.UtcNow;
    }

    public void Fail(string id, string error)
    {
        if (!_jobs.TryGetValue(id, out var job)) return;
        job.State = JobState.Failed;
        job.Stage = "Hata";
        job.Error = error;
        job.UpdatedAt = DateTime.UtcNow;
    }

    private void Prune()
    {
        var cutoff = DateTime.UtcNow - MaxAge;
        foreach (var pair in _jobs)
            if (pair.Value.UpdatedAt < cutoff)
                _jobs.TryRemove(pair.Key, out _);
    }
}
