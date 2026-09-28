using SwingAdviser.Application.Analysis;
using SwingAdviser.Domain.Ai;
using SwingAdviser.Domain.Analysis;
using SwingAdviser.Domain.Common;
using SwingAdviser.Domain.Stocks;
using SwingAdviser.Tests.Application.TestSupport;
using SwingAdviser.Tests.Domain;

namespace SwingAdviser.Tests.Application.Analysis;

public class CandidateOverviewReaderTests
{
    private static readonly DateTime NowUtc = new(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task GetLatestAsync_NoCandidates_ReturnsEmpty()
    {
        using var contextFactory = new SqliteInMemoryContextFactory();
        var reader = new CandidateOverviewReader(contextFactory, TestFixtures.DefaultStrategyParameters());

        var results = await reader.GetLatestAsync();

        Assert.Empty(results);
    }

    [Fact]
    public async Task GetLatestAsync_ReturnsOnlyLatestEvaluationDateOrderedByScoreDescending()
    {
        using var contextFactory = new SqliteInMemoryContextFactory();
        await using (var context = contextFactory.CreateDbContext())
        {
            context.Stocks.Add(new Stock("1111", "銘柄A", MarketSegment.Prime, true, NowUtc));
            context.Stocks.Add(new Stock("2222", "銘柄B", MarketSegment.Prime, true, NowUtc));
            context.CandidateEvaluations.Add(BuildCandidate("1111", new DateOnly(2026, 1, 4), score: 90));
            context.CandidateEvaluations.Add(BuildCandidate("2222", new DateOnly(2026, 1, 5), score: 60));
            context.CandidateEvaluations.Add(BuildCandidate("1111", new DateOnly(2026, 1, 5), score: 75));
            await context.SaveChangesAsync();
        }

        var reader = new CandidateOverviewReader(contextFactory, TestFixtures.DefaultStrategyParameters());
        var results = await reader.GetLatestAsync();

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Equal(new DateOnly(2026, 1, 5), r.EvaluationDate));
        Assert.Equal("1111", results[0].StockCode);
        Assert.Equal(75, results[0].Score);
        Assert.Equal("銘柄A", results[0].StockName);
        Assert.Equal("2222", results[1].StockCode);
    }

    [Fact]
    public async Task GetLatestAsync_NoAiEvaluation_AiFieldsAreNull()
    {
        using var contextFactory = new SqliteInMemoryContextFactory();
        await using (var context = contextFactory.CreateDbContext())
        {
            context.CandidateEvaluations.Add(BuildCandidate("1111", new DateOnly(2026, 1, 5), score: 80));
            await context.SaveChangesAsync();
        }

        var reader = new CandidateOverviewReader(contextFactory, TestFixtures.DefaultStrategyParameters());
        var result = Assert.Single(await reader.GetLatestAsync());

        Assert.Null(result.AiStatus);
        Assert.Null(result.AiVerdict);
        Assert.Null(result.AiSummary);
        Assert.Empty(result.AiPositiveFactors);
    }

    [Fact]
    public async Task GetLatestAsync_MultipleAiEvaluations_ReturnsMostRecentByRequestedAtUtc()
    {
        using var contextFactory = new SqliteInMemoryContextFactory();
        await using (var context = contextFactory.CreateDbContext())
        {
            context.CandidateEvaluations.Add(BuildCandidate("1111", new DateOnly(2026, 1, 5), score: 80));

            var older = AiEvaluation.Create("1111", TradeDirection.Long, NowUtc.AddHours(-2));
            older.MarkRunning(NowUtc.AddHours(-1));
            older.MarkSucceeded(AiVerdict.Bearish, ConfidenceLevel.Low, "古い結果", [], [], [], [], NowUtc.AddHours(-1));

            var newer = AiEvaluation.Create("1111", TradeDirection.Long, NowUtc);
            newer.MarkRunning(NowUtc.AddMinutes(1));
            newer.MarkSucceeded(AiVerdict.Bullish, ConfidenceLevel.High, "新しい結果", ["好材料"], [], [], [], NowUtc.AddMinutes(2));

            context.AiEvaluations.AddRange(older, newer);
            await context.SaveChangesAsync();
        }

        var reader = new CandidateOverviewReader(contextFactory, TestFixtures.DefaultStrategyParameters());
        var result = Assert.Single(await reader.GetLatestAsync());

        Assert.Equal(AiEvaluationStatus.Succeeded, result.AiStatus);
        Assert.Equal(AiVerdict.Bullish, result.AiVerdict);
        Assert.Equal("新しい結果", result.AiSummary);
        Assert.Equal(["好材料"], result.AiPositiveFactors);
    }

    [Theory]
    [InlineData(TradeDirection.Long)]
    [InlineData(TradeDirection.Short)]
    public async Task GetLatestAsync_ReferenceStopLossPrice_IsOnTheCorrectSideOfClose(TradeDirection direction)
    {
        using var contextFactory = new SqliteInMemoryContextFactory();
        await using (var context = contextFactory.CreateDbContext())
        {
            context.CandidateEvaluations.Add(BuildCandidate("1111", new DateOnly(2026, 1, 5), score: 80, direction: direction, close: 1000m, atr: 20m));
            await context.SaveChangesAsync();
        }

        var reader = new CandidateOverviewReader(contextFactory, TestFixtures.DefaultStrategyParameters());
        var result = Assert.Single(await reader.GetLatestAsync());

        if (direction == TradeDirection.Long)
        {
            Assert.True(result.ReferenceStopLossPrice < result.Close);
        }
        else
        {
            Assert.True(result.ReferenceStopLossPrice > result.Close);
        }
    }

    private static CandidateEvaluation BuildCandidate(
        string stockCode, DateOnly evaluationDate, int score, TradeDirection direction = TradeDirection.Long, decimal close = 1000m, decimal atr = 10m) =>
        new()
        {
            EvaluationDate = evaluationDate,
            StockCode = stockCode,
            Direction = direction,
            Score = score,
            Confidence = ConfidenceLevel.Medium,
            Close = close,
            MacdLine = 1m,
            MacdSignal = 0.5m,
            MacdHistogram = 0.5m,
            PreviousMacdHistogram = 0.4m,
            Ema20 = close - 5m,
            Ema100 = close - 20m,
            Ema100TwentyDaysAgo = close - 30m,
            Atr14 = atr,
            VolumeRatio = 1.5m,
            MacdCrossAgeDays = 0,
            MarketRegimeAligned = true,
            MacdFreshnessScore = 20,
            MacdPositionScore = 15,
            MacdMomentumScore = 10,
            TrendStrengthScore = 10,
            VolumeScore = 10,
            MarketRegimeScore = 15,
            StrategyParametersJson = "{}",
            CreatedAtUtc = NowUtc,
        };
}
