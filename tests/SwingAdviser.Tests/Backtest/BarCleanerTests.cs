using SwingAdviser.Backtest;
using SwingAdviser.Infrastructure.MarketData;

namespace SwingAdviser.Tests.Backtest;

public class BarCleanerTests
{
    private static readonly DateOnly Start = new(2025, 1, 1);

    [Fact]
    public void Clean_SplitEventWithUnadjustedHistory_AdjustsEarlierBars()
    {
        // 5:1 分割の効力発生日（index 3）をまたいで 1000 → 200 と段差がある＝過去側が未調整
        var bars = Series(1000, 1000, 1000, 200, 200);
        var splits = new[] { new YahooSplitEvent(Start.AddDays(3), 5m) };

        var cleaned = BarCleaner.Clean(bars, splits);

        Assert.All(cleaned.Take(3), b => Assert.Equal(200m, b.Close));
        Assert.All(cleaned.Take(3), b => Assert.Equal(500_000L, b.Volume));
        Assert.Equal(100_000L, cleaned[3].Volume);
    }

    [Fact]
    public void Clean_SplitEventWithAlreadyAdjustedHistory_LeavesBarsUntouched()
    {
        var bars = Series(200, 202, 198, 200, 201);
        var splits = new[] { new YahooSplitEvent(Start.AddDays(3), 5m) };

        var cleaned = BarCleaner.Clean(bars, splits);

        Assert.Equal(bars.Select(b => b.Close), cleaned.Select(b => b.Close));
        Assert.Equal(bars.Select(b => b.Volume), cleaned.Select(b => b.Volume));
    }

    [Fact]
    public void Clean_LargeDropWithoutEvent_IsTreatedAsSplitWithEstimatedRatio()
    {
        // 382.7 → 37.64 は 1:10 分割（当日の値動き+1.7%を含むので、比率は10に丸める）
        var bars = Series(382.7m, 380m, 37.64m, 37.8m);

        var cleaned = BarCleaner.Clean(bars, []);

        Assert.Equal(38.27m, cleaned[0].Close);
        Assert.Equal(38m, cleaned[1].Close);
        Assert.Equal(37.64m, cleaned[2].Close);
    }

    [Fact]
    public void Clean_LargeJumpWithoutEvent_IsTreatedAsConsolidation()
    {
        // 26 → 1277 は約49:1 の併合（比率は整数に丸める）。過去側を49倍する
        var bars = Series(26, 26, 1277, 1280);

        var cleaned = BarCleaner.Clean(bars, []);

        Assert.Equal(1274m, cleaned[0].Close, 4);
        Assert.Equal(1277m, cleaned[2].Close);
    }

    [Fact]
    public void Clean_ZeroVolumeBarWithAbsurdPrice_IsDropped()
    {
        var bars = Series(2800, 2810, 2790).ToList();
        bars.Insert(2, new FetchedDailyBar(Start.AddDays(10), 55_000_000_000m, 55_000_000_000m, 55_000_000_000m, 55_000_000_000m, 0));

        var cleaned = BarCleaner.Clean(bars, []);

        Assert.Equal(3, cleaned.Count);
        Assert.Equal(2790m, cleaned[^1].Close);
        Assert.Equal(2800m, cleaned[0].Close);
    }

    [Fact]
    public void Clean_OrdinaryMoves_AreUnchanged()
    {
        var bars = Series(1000, 1100, 770, 800); // +10%, -30%（値幅制限内の暴落は分割扱いしない）

        var cleaned = BarCleaner.Clean(bars, []);

        Assert.Equal(bars.Select(b => b.Close), cleaned.Select(b => b.Close));
    }

    private static List<FetchedDailyBar> Series(params decimal[] closes) =>
        closes.Select((c, i) => new FetchedDailyBar(Start.AddDays(i), c, c + 1, c - 1, c, 100_000)).ToList();
}
