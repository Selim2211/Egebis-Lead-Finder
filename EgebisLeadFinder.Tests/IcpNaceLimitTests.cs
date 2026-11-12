using EgebisLeadFinder.Controllers;
using Xunit;

namespace EgebisLeadFinder.Tests;

public class IcpNaceLimitTests
{
    [Fact]
    public void Alti_haneli_kodlar_16da_kesilir_bolumler_sinira_girmez()
    {
        var codes = new List<string> { "27", "28" };
        codes.AddRange(Enumerable.Range(1, 20).Select(i => $"27.51.{i:00}"));

        var limited = IcpController.LimitSixDigit(codes);

        Assert.Equal(2 + 16, limited.Count);
        Assert.Equal(16, limited.Count(c => c.Length > 2));
        Assert.Contains("27", limited);
        Assert.DoesNotContain("27.51.17", limited);
    }
}
