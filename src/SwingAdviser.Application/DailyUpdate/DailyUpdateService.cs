using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using SwingAdviser.Application.MarketData;
using SwingAdviser.Domain.Analysis;
using SwingAdviser.Domain.Positions;
using SwingAdviser.Domain.Risk;
using SwingAdviser.Domain.Stocks;
using SwingAdviser.Domain.Strategy;
using SwingAdviser.Infrastructure.Configuration;
using SwingAdviser.Infrastructure.MarketData;
using SwingAdviser.Infrastructure.Persistence;

namespace SwingAdviser.Application.DailyUpdate;

public sealed record DailyUpdateProgress(string Stage, int Completed, int Total);

public sealed record DailyUpdateFailure(string StockCode, string Reason);

public sealed record DailyUpdateResult(
    DateOnly EvaluationDate,
    int SyncedCount,
    IReadOnlyList<DailyUpdateFailure> Failures,
    int CandidateCount,
    int HoldingEvaluationCount,
    IReadOnlyList<string> Warnings);

/// <summary>
/// 運用サイクルの1〜2（更新→候補抽出・保有再評価）をまとめる（CLAUDE.md「運用サイクル」）。
/// 1銘柄の取得失敗が他銘柄の同期・評価を止めない。
/// </summary>
public sealed class DailyUpdateService(
    StockMasterSynchronizer stockMasterSynchronizer,
    DailyBarSynchronizer dailyBarSynchronizer,
    IDbContextFactory<SwingAdviserDbContext> contextFactory,
    StrategyParameters strategyParameters,
    LiquidityFilterOptions liquidityFilterOptions,
    YahooFinanceOptions yahooFinanceOptions,
    TimeProvider timeProvider)
{
    public async Task<DailyUpdateResult> RunAsync(IProgress<DailyUpdateProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var warnings = new List<string>();

        progress?.Report(new DailyUpdateProgress("銘柄マスタ同期", 0, 1));
        var stockMasterResult = await stockMasterSynchronizer.SyncAsync(cancellationToken).ConfigureAwait(false);
        if (stockMasterResult.Warning is not null)
        {
            warnings.Add(stockMasterResult.Warning);
        }

        progress?.Report(new DailyUpdateProgress("地合い指数同期", 0, 1));
        DailyBarSyncResult regimeResult;
        try
        {
            regimeResult = await dailyBarSynchronizer.SyncAsync(StockMasterSynchronizer.MarketRegimeStockCode, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new InvalidOperationException("地合い指数(1306)の取得に失敗したため、日次更新を中断しました。", exception);
        }

        warnings.AddRange(regimeResult.Warnings);

        DateOnly evaluationDate;
        await using (var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            var latestRegimeDate = await context.DailyBars
                .Where(b => b.StockCode == StockMasterSynchronizer.MarketRegimeStockCode)
                .OrderByDescending(b => b.TradeDate)
                .Select(b => (DateOnly?)b.TradeDate)
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

            if (latestRegimeDate is null)
            {
                throw new InvalidOperationException("地合い指数(1306)のバーが1件も保存されていません。");
            }

            evaluationDate = latestRegimeDate.Value;
        }

        var targetCodes = await DetermineSyncTargetsAsync(evaluationDate, cancellationToken).ConfigureAwait(false);

        var failures = new ConcurrentBag<DailyUpdateFailure>();
        var syncWarnings = new ConcurrentBag<string>();
        var syncedCount = 0;
        var completed = 0;
        var total = targetCodes.Count;
        progress?.Report(new DailyUpdateProgress("日足同期", 0, total));

        await Parallel.ForEachAsync(
            targetCodes,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, yahooFinanceOptions.MaxRequestsPerSecond),
                CancellationToken = cancellationToken,
            },
            async (code, ct) =>
            {
                try
                {
                    var result = await dailyBarSynchronizer.SyncAsync(code, ct).ConfigureAwait(false);
                    foreach (var warning in result.Warnings)
                    {
                        syncWarnings.Add(warning);
                    }

                    Interlocked.Increment(ref syncedCount);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    failures.Add(new DailyUpdateFailure(code, Summarize(exception)));
                }
                finally
                {
                    var done = Interlocked.Increment(ref completed);
                    progress?.Report(new DailyUpdateProgress("日足同期", done, total));
                }
            }).ConfigureAwait(false);

        warnings.AddRange(syncWarnings);

        progress?.Report(new DailyUpdateProgress("候補抽出・保有再評価", 0, 1));
        var (candidateCount, holdingCount) = await EvaluateAsync(evaluationDate, cancellationToken).ConfigureAwait(false);
        progress?.Report(new DailyUpdateProgress("候補抽出・保有再評価", 1, 1));

        return new DailyUpdateResult(evaluationDate, syncedCount, failures.ToArray(), candidateCount, holdingCount, warnings);
    }

    /// <summary>
    /// 候補抽出・保有再評価だけを指定日で実行する（過去日の再判定やテストにも使う）。
    /// asOfDate 以前のバーしか読まないため未来データは混入しない（<see cref="BarRepository"/>）。
    /// 判定日ごとに1行を保つため、同日分の既存行は削除してから挿入する。
    /// </summary>
    public async Task<(int CandidateCount, int HoldingEvaluationCount)> EvaluateAsync(
        DateOnly evaluationDate, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;

        await context.CandidateEvaluations.Where(c => c.EvaluationDate == evaluationDate)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await context.HoldingEvaluations.Where(h => h.EvaluationDate == evaluationDate)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);

        var candidateCount = await EvaluateCandidatesAsync(context, evaluationDate, nowUtc, cancellationToken).ConfigureAwait(false);
        var holdingCount = await EvaluateHoldingsAsync(context, evaluationDate, nowUtc, cancellationToken).ConfigureAwait(false);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return (candidateCount, holdingCount);
    }

    private async Task<int> EvaluateCandidatesAsync(
        SwingAdviserDbContext context, DateOnly evaluationDate, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var regimeBars = await BarRepository.LoadBarsAsOfAsync(
            context, StockMasterSynchronizer.MarketRegimeStockCode, evaluationDate, strategyParameters.AnalysisWindow.BarsToFetch, cancellationToken)
            .ConfigureAwait(false);

        if (regimeBars.Count == 0 || regimeBars[^1].TradeDate != evaluationDate)
        {
            return 0;
        }

        var candidateCodes = await context.Stocks
            .Where(s => s.IsActive && s.MarketSegment != MarketSegment.Etf)
            .Select(s => s.StockCode)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var scanner = new CandidateScanner(strategyParameters);
        var candidateCount = 0;

        foreach (var code in candidateCodes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var bars = await BarRepository.LoadBarsAsOfAsync(
                context, code, evaluationDate, strategyParameters.AnalysisWindow.BarsToFetch, cancellationToken).ConfigureAwait(false);

            if (bars.Count < strategyParameters.AnalysisWindow.MinimumRequiredBars || bars[^1].TradeDate != evaluationDate)
            {
                continue;
            }

            if (!LiquidityFilter.IsEligible(bars, liquidityFilterOptions))
            {
                continue;
            }

            try
            {
                var candidates = scanner.Evaluate(code, bars, regimeBars, nowUtc);
                if (candidates.Count > 0)
                {
                    context.CandidateEvaluations.AddRange(candidates);
                    candidateCount += candidates.Count;
                }
            }
            catch (ArgumentException)
            {
                // 指標計算に必要な本数・整合性が瞬間的に不足。1銘柄の失敗として無視し次銘柄へ進む。
            }
        }

        return candidateCount;
    }

    private async Task<int> EvaluateHoldingsAsync(
        SwingAdviserDbContext context, DateOnly evaluationDate, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var openPositions = await context.Positions
            .Include(p => p.Executions)
            .Where(p => p.Status == PositionStatus.Open)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var evaluator = new HoldingRiskEvaluator(strategyParameters);
        var holdingCount = 0;

        foreach (var position in openPositions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (position.OpenedDate > evaluationDate)
            {
                continue;
            }

            var bars = await BarRepository.LoadBarsAsOfAsync(
                context, position.StockCode, evaluationDate, strategyParameters.AnalysisWindow.BarsToFetch, cancellationToken).ConfigureAwait(false);

            if (bars.Count == 0 || bars[^1].TradeDate != evaluationDate)
            {
                continue;
            }

            try
            {
                var evaluation = evaluator.Evaluate(position, bars, nowUtc);
                context.HoldingEvaluations.Add(evaluation);
                holdingCount++;
            }
            catch (ArgumentException)
            {
                // 本数不足等。1ポジションの失敗として無視し次へ進む。
            }
        }

        return holdingCount;
    }

    private async Task<IReadOnlyList<string>> DetermineSyncTargetsAsync(DateOnly evaluationDate, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var heldCodes = await context.Positions
            .Where(p => p.Status == PositionStatus.Open)
            .Select(p => p.StockCode)
            .Distinct()
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var activeCodes = await context.Stocks
            .Where(s => s.IsActive && s.StockCode != StockMasterSynchronizer.MarketRegimeStockCode)
            .Select(s => s.StockCode)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var latestBarDateByCode = await context.DailyBars
            .Where(b => activeCodes.Contains(b.StockCode))
            .GroupBy(b => b.StockCode)
            .Select(g => new { StockCode = g.Key, LatestDate = g.Max(b => b.TradeDate) })
            .ToDictionaryAsync(x => x.StockCode, x => x.LatestDate, cancellationToken).ConfigureAwait(false);

        var recheckCutoffDate = evaluationDate.AddDays(-liquidityFilterOptions.RecheckIntervalDays);
        var targets = new HashSet<string>(heldCodes, StringComparer.Ordinal);

        foreach (var code in activeCodes)
        {
            if (targets.Contains(code))
            {
                continue;
            }

            if (!latestBarDateByCode.TryGetValue(code, out var latestDate) || latestDate < recheckCutoffDate)
            {
                targets.Add(code);
                continue;
            }

            var bars = await BarRepository.LoadBarsAsOfAsync(
                context, code, evaluationDate, liquidityFilterOptions.TurnoverAveragePeriodDays, cancellationToken).ConfigureAwait(false);

            if (LiquidityFilter.IsEligible(bars, liquidityFilterOptions))
            {
                targets.Add(code);
            }
        }

        return targets.ToArray();
    }

    private static string Summarize(Exception exception)
    {
        var message = exception.Message;
        return message.Length <= 300 ? message : message[..300];
    }
}
