using EgebisLeadFinder.Services.Diagnostics;
using Xunit;

namespace EgebisLeadFinder.Tests;

public class DiagnosticsTests
{
    [Theory]
    [InlineData("x-api-key: abcdefghijklmnop123", "abcdefghijklmnop123")]
    [InlineData("Authorization Bearer gsk_ABCDEFGHIJKLMNOP", "gsk_ABCDEFGHIJKLMNOP")]
    [InlineData("Password=SuperSecretValue99;", "SuperSecretValue99")]
    public void Mask_hides_secret_like_values(string line, string secret)
    {
        var masked = DiagnosticsService.Mask(line);
        Assert.DoesNotContain(secret, masked);
        Assert.Contains("***", masked);
    }

    [Fact]
    public void Mask_keeps_normal_log_lines() =>
        Assert.Equal("[INF] HTTP GET /Company responded 200 in 42 ms", DiagnosticsService.Mask("[INF] HTTP GET /Company responded 200 in 42 ms"));

    [Fact]
    public void Timings_ring_buffer_keeps_last_entries_in_order()
    {
        var t = new RequestTimings();
        for (var i = 0; i < 510; i++) t.Add(new RequestTimings.Entry(DateTime.UtcNow, "GET", "/p" + i, 200, i));
        var snap = t.Snapshot();
        Assert.Equal(500, snap.Count);
        Assert.Equal("/p10", snap[0].Path);
        Assert.Equal("/p509", snap[^1].Path);
    }
}
