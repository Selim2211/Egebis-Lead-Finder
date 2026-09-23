using EgebisLeadFinder.Models;
using EgebisLeadFinder.Services;

namespace EgebisLeadFinder.Tests;

/// <summary>Testlerde kullanim sayacini gercekten yazmaz; DbContext'e ihtiyac duymamak icin.</summary>
public class FakeApiUsageTracker : IApiUsageTracker
{
    public Task IncrementAsync(string provider, CancellationToken ct = default) => Task.CompletedTask;

    public Task<ApiUsage> GetAsync(string provider, CancellationToken ct = default) =>
        Task.FromResult(new ApiUsage { Provider = provider });

    public Task ResetAsync(string provider, CancellationToken ct = default) => Task.CompletedTask;

    public Task<int> GetRangeCountAsync(string provider, DateOnly from, DateOnly to, CancellationToken ct = default) =>
        Task.FromResult(0);
}
