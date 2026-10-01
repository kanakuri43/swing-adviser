using Microsoft.EntityFrameworkCore;
using SwingAdviser.Application.Common;
using SwingAdviser.Domain.Common;
using SwingAdviser.Domain.Positions;
using SwingAdviser.Domain.Risk;
using SwingAdviser.Domain.Strategy;
using SwingAdviser.Infrastructure.Persistence;

namespace SwingAdviser.Application.Risk;

public enum MarginDueStatus
{
    /// <summary>現物（信用取引ではない）。</summary>
    NotMargin,

    /// <summary>返済期限が未入力（CLAUDE.mdの「未確認」表示。決済済み扱いにはしない）。</summary>
    Unconfirmed,

    /// <summary>返済期限が今日以前。</summary>
    Overdue,

    /// <summary>残営業日が警告閾値以内。</summary>
    Warning,

    /// <summary>残営業日に余裕がある。</summary>
    Normal,
}

public sealed record HoldingOverview(
    long PositionId,
    string StockCode,
    string StockName,
    TradeDirection Direction,
    bool IsMargin,
    decimal RemainingQuantity,
    decimal AverageEntryPrice,
    decimal StopLossPrice,
    decimal? LatestClose,
    DateOnly? LatestCloseDate,
    decimal? CurrentProfitAndLoss,
    HoldingDecision? Decision,
    string? DecisionReason,
    decimal? AchievedRMultiple,
    MarginDueStatus MarginDueStatus,
    DateOnly? MarginDueDate);

/// <summary>
/// 保有タブの表示専用読み取り。優先順位判定そのものはPhase3の<see cref="HoldingRiskEvaluator"/>が
/// 既に行っており、ここでは最新の判定行と現在値を組み合わせて表示するだけ。
/// 残営業日は土日のみを除く簡易カウント（JPX休場日カレンダーは持たない。警告用の目安として割り切る）。
/// </summary>
public sealed class HoldingOverviewReader(
    IDbContextFactory<SwingAdviserDbContext> contextFactory, StrategyParameters strategyParameters, TimeProvider timeProvider)
{
    public async Task<IReadOnlyList<HoldingOverview>> GetOpenPositionsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var positions = await context.Positions
            .Include(p => p.Executions)
            .Where(p => p.Status == PositionStatus.Open)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        if (positions.Count == 0)
        {
            return [];
        }

        var stockCodes = positions.Select(p => p.StockCode).Distinct().ToArray();
        var stockNames = await context.Stocks
            .Where(s => stockCodes.Contains(s.StockCode))
            .ToDictionaryAsync(s => s.StockCode, s => s.Name, cancellationToken).ConfigureAwait(false);

        var positionIds = positions.Select(p => p.Id).ToArray();
        var holdingEvaluations = await context.HoldingEvaluations
            .Where(h => positionIds.Contains(h.PositionId))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var latestEvaluationByPosition = holdingEvaluations
            .GroupBy(h => h.PositionId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(h => h.EvaluationDate).First());

        var bars = await context.DailyBars
            .Where(b => stockCodes.Contains(b.StockCode))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var latestBarByCode = bars
            .GroupBy(b => b.StockCode)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(b => b.TradeDate).First());

        var today = Jst.TodayJst(timeProvider);
        var results = new List<HoldingOverview>(positions.Count);

        foreach (var position in positions)
        {
            stockNames.TryGetValue(position.StockCode, out var stockName);
            latestEvaluationByPosition.TryGetValue(position.Id, out var evaluation);
            latestBarByCode.TryGetValue(position.StockCode, out var latestBar);

            decimal? currentProfitAndLoss = null;
            if (latestBar is not null)
            {
                var sign = position.Direction == TradeDirection.Long ? 1m : -1m;
                currentProfitAndLoss = sign * (latestBar.Close - position.AverageEntryPrice) * position.RemainingQuantity;
            }

            var (marginDueStatus, marginDueDate) = ComputeMarginDueStatus(position, today);

            results.Add(new HoldingOverview(
                position.Id,
                position.StockCode,
                stockName ?? "（銘柄名未確認）",
                position.Direction,
                position.IsMargin,
                position.RemainingQuantity,
                position.AverageEntryPrice,
                position.StopLossPrice,
                latestBar?.Close,
                latestBar?.TradeDate,
                currentProfitAndLoss,
                evaluation?.Decision,
                evaluation?.Reason,
                evaluation?.AchievedRMultiple,
                marginDueStatus,
                marginDueDate));
        }

        return results;
    }

    /// <summary>全ポジション（保有中の部分決済ぶんを含む）の実現損益合計。</summary>
    public async Task<decimal> GetRealizedProfitAndLossAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var positions = await context.Positions
            .Include(p => p.Executions)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return positions.Sum(p => p.RealizedProfitAndLoss);
    }

    private (MarginDueStatus Status, DateOnly? DueDate) ComputeMarginDueStatus(Position position, DateOnly today)
    {
        if (!position.IsMargin)
        {
            return (MarginDueStatus.NotMargin, null);
        }

        if (position.IsMarginDueUnconfirmed)
        {
            return (MarginDueStatus.Unconfirmed, null);
        }

        var dueDates = position.Executions
            .Where(e => e.Side == ExecutionSide.Open && e.MarginDueDate is not null)
            .Select(e => e.MarginDueDate!.Value)
            .ToArray();
        if (dueDates.Length == 0)
        {
            return (MarginDueStatus.Unconfirmed, null);
        }

        var earliestDueDate = dueDates.Min();
        if (earliestDueDate <= today)
        {
            return (MarginDueStatus.Overdue, earliestDueDate);
        }

        var remainingBusinessDays = CountBusinessDays(today, earliestDueDate);
        var status = remainingBusinessDays <= strategyParameters.Risk.MarginDueWarningBusinessDays
            ? MarginDueStatus.Warning
            : MarginDueStatus.Normal;
        return (status, earliestDueDate);
    }

    private static int CountBusinessDays(DateOnly from, DateOnly to)
    {
        var count = 0;
        for (var date = from.AddDays(1); date <= to; date = date.AddDays(1))
        {
            if (date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                count++;
            }
        }

        return count;
    }
}
