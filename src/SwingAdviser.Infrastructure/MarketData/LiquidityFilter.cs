using SwingAdviser.Domain.MarketData;
using SwingAdviser.Infrastructure.Configuration;

namespace SwingAdviser.Infrastructure.MarketData;

public static class LiquidityFilter
{
    /// <summary>直近TurnoverAveragePeriodDays営業日の平均売買代金（終値×出来高）が閾値以上かを判定する。</summary>
    public static bool IsEligible(IReadOnlyList<DailyBar> recentBars, LiquidityFilterOptions options)
    {
        if (recentBars.Count < options.TurnoverAveragePeriodDays)
        {
            return false;
        }

        var averageTurnover = recentBars.TakeLast(options.TurnoverAveragePeriodDays).Average(b => b.Close * b.Volume);
        return averageTurnover >= options.MinimumAverageTurnoverJpy;
    }
}
