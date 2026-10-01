using Microsoft.EntityFrameworkCore;
using SwingAdviser.Application.Common;
using SwingAdviser.Domain.Common;
using SwingAdviser.Domain.Positions;
using SwingAdviser.Infrastructure.Persistence;

namespace SwingAdviser.Application.Positions;

public sealed record ExecutionOverview(
    long ExecutionId,
    long PositionId,
    string StockCode,
    string StockName,
    TradeDirection Direction,
    ExecutionSide Side,
    DateTime ExecutedAtJst,
    decimal Price,
    int Quantity,
    DateOnly? MarginDueDate,
    int CorrectionCount,
    bool IsPositionOpen,
    decimal? RealizedProfitAndLoss);

/// <summary>履歴タブの表示専用読み取り。約定は監査原票なので加工せずそのまま一覧化する。</summary>
public sealed class ExecutionOverviewReader(IDbContextFactory<SwingAdviserDbContext> contextFactory)
{
    public async Task<IReadOnlyList<ExecutionOverview>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var positions = await context.Positions
            .Include(p => p.Executions)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var stockCodes = positions.Select(p => p.StockCode).Distinct().ToArray();
        var stockNames = await context.Stocks
            .Where(s => stockCodes.Contains(s.StockCode))
            .ToDictionaryAsync(s => s.StockCode, s => s.Name, cancellationToken).ConfigureAwait(false);

        var results = new List<ExecutionOverview>();
        foreach (var position in positions)
        {
            foreach (var execution in position.Executions)
            {
                results.Add(new ExecutionOverview(
                    execution.Id,
                    position.Id,
                    position.StockCode,
                    stockNames.GetValueOrDefault(position.StockCode) ?? "（銘柄名未確認）",
                    position.Direction,
                    execution.Side,
                    TimeZoneInfo.ConvertTimeFromUtc(execution.ExecutedAtUtc, Jst.TimeZone),
                    execution.Price,
                    execution.Quantity,
                    execution.MarginDueDate,
                    execution.CorrectionLog.Count,
                    position.Status == PositionStatus.Open,
                    execution.Side == ExecutionSide.Close ? position.RealizedProfitAndLossOf(execution) : null));
            }
        }

        return results.OrderByDescending(e => e.ExecutedAtJst).ToList();
    }
}
