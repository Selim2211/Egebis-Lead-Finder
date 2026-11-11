using System.Collections.Concurrent;

namespace EgebisLeadFinder.Services;

/// <summary>
/// Bir aramanin (SearchRun) harcadigi dis API cagrilarini sayar. Arama baslarken <see cref="Begin"/> ile acilir;
/// o aramanin async akisi icinde (paralel firma islemleri dahil) yapilan her sayac artisi (ApiUsageTracker) buraya da yazilir.
/// AsyncLocal sayesinde es zamanli iki arama birbirinin sayacini karistirmaz.
/// </summary>
public sealed class UsageScope : IDisposable
{
    private static readonly AsyncLocal<UsageScope?> CurrentScope = new();
    private readonly ConcurrentDictionary<string, int> _counts = new(StringComparer.OrdinalIgnoreCase);
    private readonly UsageScope? _previous;

    private UsageScope(UsageScope? previous) => _previous = previous;

    public static UsageScope? Current => CurrentScope.Value;

    public static UsageScope Begin()
    {
        var scope = new UsageScope(CurrentScope.Value);
        CurrentScope.Value = scope;
        return scope;
    }

    public void Dispose() => CurrentScope.Value = _previous;

    /// <summary>Kullanim kaydi sirasinda cagrilir (ApiUsageTracker).</summary>
    public static void Record(string provider)
    {
        var scope = CurrentScope.Value;
        while (scope is not null)
        {
            scope._counts.AddOrUpdate(provider, 1, (_, n) => n + 1);
            scope = scope._previous;
        }
    }

    public int Count(string provider) => _counts.GetValueOrDefault(provider);

    /// <summary>Serper kredisi (onbellekten donen aramalar sayilmaz).</summary>
    public int SerperCredits => Count(SerperSearchService.UsageProvider);

    /// <summary>Yapay zeka cagrilari: Gemini, Groq, NVIDIA... (Serper ve Apollo disindakiler).</summary>
    public int AiCalls => _counts
        .Where(kv => !IsNonAi(kv.Key))
        .Sum(kv => kv.Value);

    public static bool IsNonAi(string provider) =>
        string.Equals(provider, SerperSearchService.UsageProvider, StringComparison.OrdinalIgnoreCase)
        || string.Equals(provider, ApolloPersonEmailFinder.UsageProvider, StringComparison.OrdinalIgnoreCase)
        || string.Equals(provider, ApolloPersonEmailFinder.CreditUsageProvider, StringComparison.OrdinalIgnoreCase);
}
