using SwingAdviser.Domain.MarketData;
using SwingAdviser.Infrastructure.Configuration;
using SwingAdviser.Infrastructure.MarketData;

namespace SwingAdviser.Tests.Infrastructure;

public class LiquidityFilterTests
{
    private static LiquidityFilterOptions Options() => new()
    {
        MinimumAverageTurnoverJpy = 100_000_000m,
        TurnoverAveragePeriodDays = 20,
    };

    private static List<DailyBar> BuildBars(int days, decimal close, long volume, int startDayOffset = 0)
    {
        var bars = new List<DailyBar>();
        var date = new DateOnly(2024, 1, 1).AddDays(startDayOffset);
        for (var i = 0; i < days; i++)
        {
            bars.Add(new DailyBar("7203", date.AddDays(i), close, close + 1m, close - 1m, close, volume));
        }

        return bars;
    }

    [Fact]
    public void IsEligible_AverageTurnoverExactlyAtThreshold_ReturnsTrue()
    {
        // 終値1000円 × 出来高100,000株 = 平均売買代金1億円ちょうど。
        var bars = BuildBars(20, 1000m, 100_000);

        Assert.True(LiquidityFilter.IsEligible(bars, Options()));
    }

    [Fact]
    public void IsEligible_AverageTurnoverJustBelowThreshold_ReturnsFalse()
    {
        var bars = BuildBars(20, 1000m, 99_999);

        Assert.False(LiquidityFilter.IsEligible(bars, Options()));
    }

    [Fact]
    public void IsEligible_FewerBarsThanAveragePeriod_ReturnsFalse()
    {
        var bars = BuildBars(19, 10_000m, 1_000_000);

        Assert.False(LiquidityFilter.IsEligible(bars, Options()));
    }

    [Fact]
    public void IsEligible_UsesOnlyMostRecentWindow()
    {
        // 直近20日だけ薄商いにすると、それより前が厚くても不適格になる。
        var thickBars = BuildBars(10, 10_000m, 1_000_000); // 平均売買代金 100億円
        var thinRecentBars = BuildBars(20, 1000m, 1_000, startDayOffset: 10); // 平均売買代金 100万円
        var combined = thickBars.Concat(thinRecentBars).ToList();

        Assert.False(LiquidityFilter.IsEligible(combined, Options()));
    }
}
