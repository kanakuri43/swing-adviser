using Microsoft.EntityFrameworkCore;
using SwingAdviser.Domain.Ai;
using SwingAdviser.Domain.Common;
using SwingAdviser.Domain.Strategy;
using SwingAdviser.Infrastructure.Persistence;

namespace SwingAdviser.Application.Analysis;

public sealed record CandidateOverview(
    string StockCode,
    string StockName,
    TradeDirection Direction,
    DateOnly EvaluationDate,
    int Score,
    ConfidenceLevel Confidence,
    decimal Close,
    decimal MacdLine,
    decimal MacdSignal,
    decimal MacdHistogram,
    decimal Ema20,
    decimal Ema100,
    decimal Atr14,
    decimal VolumeRatio,
    bool MarketRegimeAligned,
    decimal ReferenceStopLossPrice,
    AiEvaluationStatus? AiStatus,
    AiVerdict? AiVerdict,
    ConfidenceLevel? AiConfidence,
    string? AiSummary,
    string? AiErrorMessage,
    DateTime? AiRequestedAtUtc,
    IReadOnlyList<string> AiPositiveFactors,
    IReadOnlyList<string> AiRiskFactors,
    IReadOnlyList<string> AiInvalidationConditions);

/// <summary>
/// 候補タブの表示専用読み取り。最新評価日の候補だけをスコア降順で返す。参考損切ラインは表示用の計算値であり、
/// 実際の損切ライン（建玉時に固定）とは別物（CLAUDE.md「リスク管理」節）。
/// </summary>
public sealed class CandidateOverviewReader(IDbContextFactory<SwingAdviserDbContext> contextFactory, StrategyParameters strategyParameters)
{
    public async Task<IReadOnlyList<CandidateOverview>> GetLatestAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var latestDate = await context.CandidateEvaluations
            .OrderByDescending(c => c.EvaluationDate)
            .Select(c => (DateOnly?)c.EvaluationDate)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (latestDate is null)
        {
            return [];
        }

        var candidates = await context.CandidateEvaluations
            .Where(c => c.EvaluationDate == latestDate)
            .OrderByDescending(c => c.Score)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var stockCodes = candidates.Select(c => c.StockCode).Distinct().ToArray();
        var stockNames = await context.Stocks
            .Where(s => stockCodes.Contains(s.StockCode))
            .ToDictionaryAsync(s => s.StockCode, s => s.Name, cancellationToken).ConfigureAwait(false);

        var aiEvaluations = await context.AiEvaluations
            .Where(a => stockCodes.Contains(a.StockCode))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var latestAiByKey = aiEvaluations
            .GroupBy(a => (a.StockCode, a.Direction))
            .ToDictionary(g => g.Key, g => g.OrderByDescending(a => a.RequestedAtUtc).First());

        var risk = strategyParameters.Risk;
        var results = new List<CandidateOverview>(candidates.Count);

        foreach (var candidate in candidates)
        {
            var sign = candidate.Direction == TradeDirection.Long ? 1m : -1m;
            var stopLossMultiple = candidate.Direction == TradeDirection.Long ? risk.LongStopLossAtrMultiple : risk.ShortStopLossAtrMultiple;
            var referenceStopLossPrice = candidate.Close - (sign * stopLossMultiple * candidate.Atr14);

            latestAiByKey.TryGetValue((candidate.StockCode, (TradeDirection?)candidate.Direction), out var aiEvaluation);
            stockNames.TryGetValue(candidate.StockCode, out var stockName);

            results.Add(new CandidateOverview(
                candidate.StockCode,
                stockName ?? "（銘柄名未確認）",
                candidate.Direction,
                candidate.EvaluationDate,
                candidate.Score,
                candidate.Confidence,
                candidate.Close,
                candidate.MacdLine,
                candidate.MacdSignal,
                candidate.MacdHistogram,
                candidate.Ema20,
                candidate.Ema100,
                candidate.Atr14,
                candidate.VolumeRatio,
                candidate.MarketRegimeAligned,
                referenceStopLossPrice,
                aiEvaluation?.Status,
                aiEvaluation?.Verdict,
                aiEvaluation?.Confidence,
                aiEvaluation?.Summary,
                aiEvaluation?.ErrorMessage,
                aiEvaluation?.RequestedAtUtc,
                aiEvaluation?.PositiveFactors ?? [],
                aiEvaluation?.RiskFactors ?? [],
                aiEvaluation?.InvalidationConditions ?? []));
        }

        return results;
    }
}
