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
    TradeDirection Direction,
    ExecutionSide Side,
    DateTime ExecutedAtJst,
    decimal Price,
    int Quantity,
    DateOnly? MarginDueDate,
    int CorrectionCount,
    bool IsPositionOpen);

/// <summary>履歴タブの表示専用読み取り。約定は監査原票なので加工せずそのまま一覧化する。</summary>
public sealed class ExecutionOverviewReader(IDbContextFactory<SwingAdviserDbContext> contextFactory)
{
    public async Task<IReadOnlyList<ExecutionOverview>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var positions = await context.Positions
            .Include(p => p.Executions)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var results = new List<ExecutionOverview>();
        foreach (var position in positions)
        {
            foreach (var execution in position.Executions)
            {
                results.Add(new ExecutionOverview(
                    execution.Id,
                    position.Id,
                    position.StockCode,
                    position.Direction,
                    execution.Side,
                    TimeZoneInfo.ConvertTimeFromUtc(execution.ExecutedAtUtc, Jst.TimeZone),
                    execution.Price,
                    execution.Quantity,
                    execution.MarginDueDate,
                    execution.CorrectionLog.Count,
                    position.Status == PositionStatus.Open));
            }
        }

        return results.OrderByDescending(e => e.ExecutedAtJst).ToList();
    }
}
