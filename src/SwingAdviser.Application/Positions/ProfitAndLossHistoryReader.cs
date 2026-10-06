using Microsoft.EntityFrameworkCore;
using SwingAdviser.Application.Common;
using SwingAdviser.Domain.Common;
using SwingAdviser.Domain.Positions;
using SwingAdviser.Infrastructure.Persistence;

namespace SwingAdviser.Application.Positions;

/// <summary>日次の損益。Total = Realized（実現累計）+ Unrealized（その日の終値での含み損益）。</summary>
public sealed record ProfitAndLossPoint(DateOnly Date, decimal Realized, decimal Unrealized)
{
    public decimal Total => Realized + Unrealized;
}

/// <summary>
/// 損益タブ用の日次推移。実現損益は <see cref="Position.RealizedProfitAndLossOf"/>（ポジション全体の平均取得単価基準）に従い、
/// 保有タブ・履歴タブの数値と最終日の値が一致する。含み損益は各日の時点までの約定と終値だけで計算する
/// （終値が無い日はその銘柄の直近の既知終値を引き継ぎ、一度も無ければ0）。手数料・信用コスト・配当は含まない参考値。
/// </summary>
public sealed class ProfitAndLossHistoryReader(IDbContextFactory<SwingAdviserDbContext> contextFactory)
{
    public async Task<IReadOnlyList<ProfitAndLossPoint>> GetDailyAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var positions = await context.Positions
            .Include(p => p.Executions)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        if (positions.Count == 0)
        {
            return [];
        }

        var firstDate = positions.Min(p => p.OpenedDate);
        var stockCodes = positions.Select(p => p.StockCode).Distinct().ToArray();
        var bars = await context.DailyBars
            .Where(b => stockCodes.Contains(b.StockCode) && b.TradeDate >= firstDate)
            .Select(b => new { b.StockCode, b.TradeDate, b.Close })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var closeByCode = bars
            .GroupBy(b => b.StockCode)
            .ToDictionary(g => g.Key, g => g.OrderBy(b => b.TradeDate).Select(b => (b.TradeDate, b.Close)).ToArray());

        var dates = bars.Select(b => b.TradeDate)
            .Concat(positions.SelectMany(p => p.Executions).Select(e => ToJstDate(e.ExecutedAtUtc)))
            .Distinct()
            .OrderBy(d => d)
            .ToArray();

        var points = new List<ProfitAndLossPoint>(dates.Length);
        foreach (var date in dates)
        {
            decimal realized = 0m;
            decimal unrealized = 0m;
            foreach (var position in positions)
            {
                var executedByDate = position.Executions.Where(e => ToJstDate(e.ExecutedAtUtc) <= date).ToArray();
                realized += executedByDate
                    .Where(e => e.Side == ExecutionSide.Close)
                    .Sum(position.RealizedProfitAndLossOf);
                unrealized += UnrealizedAsOf(position, executedByDate, date, closeByCode);
            }

            points.Add(new ProfitAndLossPoint(date, realized, unrealized));
        }

        return points;
    }

    private static decimal UnrealizedAsOf(
        Position position, Execution[] executedByDate, DateOnly date,
        Dictionary<string, (DateOnly TradeDate, decimal Close)[]> closeByCode)
    {
        var opens = executedByDate.Where(e => e.Side == ExecutionSide.Open).ToArray();
        var openQuantity = opens.Sum(e => e.AdjustedQuantity);
        var remaining = openQuantity - executedByDate.Where(e => e.Side == ExecutionSide.Close).Sum(e => e.AdjustedQuantity);
        if (remaining <= 0 || !closeByCode.TryGetValue(position.StockCode, out var closes))
        {
            return 0m;
        }

        decimal? close = null;
        foreach (var bar in closes)
        {
            if (bar.TradeDate > date)
            {
                break;
            }

            close = bar.Close;
        }

        if (close is null)
        {
            return 0m;
        }

        var averageEntry = opens.Sum(e => e.AdjustedPrice * e.AdjustedQuantity) / openQuantity;
        var sign = position.Direction == TradeDirection.Long ? 1m : -1m;
        return sign * (close.Value - averageEntry) * remaining;
    }

    private static DateOnly ToJstDate(DateTime utc) => DateOnly.FromDateTime(Jst.ToJst(utc));
}
