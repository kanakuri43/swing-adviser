using Microsoft.EntityFrameworkCore;
using SwingAdviser.Domain.MarketData;
using SwingAdviser.Infrastructure.Persistence;

namespace SwingAdviser.Application.MarketData;

/// <summary>
/// 日足の読み込みはこの1か所だけを通す。asOfDate より後のバーを絶対に含めないことで、
/// 過去日のシグナル計算に未来データを混入させない（CLAUDE.md「Non-negotiable rules」）。
/// </summary>
public static class BarRepository
{
    public static async Task<IReadOnlyList<DailyBar>> LoadBarsAsOfAsync(
        SwingAdviserDbContext context, string stockCode, DateOnly asOfDate, int count, CancellationToken cancellationToken = default)
    {
        var bars = await context.DailyBars.AsNoTracking()
            .Where(b => b.StockCode == stockCode && b.TradeDate <= asOfDate)
            .OrderByDescending(b => b.TradeDate)
            .Take(count)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        bars.Reverse();
        return bars;
    }
}
