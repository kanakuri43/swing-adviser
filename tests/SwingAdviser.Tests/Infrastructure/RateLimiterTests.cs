using System.Diagnostics;
using SwingAdviser.Infrastructure.MarketData;

namespace SwingAdviser.Tests.Infrastructure;

public class RateLimiterTests
{
    [Fact]
    public async Task WaitForTurnAsync_FirstCall_DoesNotWait()
    {
        var limiter = new RateLimiter(maxRequestsPerSecond: 5);
        var stopwatch = Stopwatch.StartNew();

        await limiter.WaitForTurnAsync();

        Assert.True(stopwatch.ElapsedMilliseconds < 50, $"初回は待たないはずが{stopwatch.ElapsedMilliseconds}ms待った。");
    }

    [Fact]
    public async Task WaitForTurnAsync_EnforcesMinimumIntervalBetweenCalls()
    {
        // 5req/s -> 間隔200ms。10回呼んで概ね(10-1)*200ms以上かかることを確認する（実時間なので厳密な精度は求めない）。
        var limiter = new RateLimiter(maxRequestsPerSecond: 5);
        var stopwatch = Stopwatch.StartNew();

        for (var i = 0; i < 10; i++)
        {
            await limiter.WaitForTurnAsync();
        }

        Assert.True(stopwatch.ElapsedMilliseconds >= 1700, $"9間隔(1800ms想定)より大幅に短い{stopwatch.ElapsedMilliseconds}msだった。");
    }

    [Fact]
    public void Constructor_NonPositiveRate_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RateLimiter(0));
    }
}
