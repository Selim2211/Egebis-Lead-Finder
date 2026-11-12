namespace EgebisLeadFinder.Services.News;

/// <summary>Mini grafik verisi: son N Türkiye gününün haber sayıları (en eski gün başta, bugün sonda).</summary>
public static class NewsSpark
{
    public static int[] Build(IEnumerable<DateTime> publishedUtc, DateTime nowUtc, int days = 7)
    {
        var result = new int[days];
        var todayStart = NewsSlots.TodayStartUtc(nowUtc);
        foreach (var d in publishedUtc)
        {
            var back = d >= todayStart ? 0 : (int)Math.Ceiling((todayStart - d).TotalDays);
            var idx = days - 1 - back;
            if (idx >= 0 && idx < days) result[idx]++;
        }
        return result;
    }
}
