using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SwingAdviser.Domain.Ai;
using SwingAdviser.Domain.Analysis;
using SwingAdviser.Domain.Common;
using SwingAdviser.Domain.MarketData;
using SwingAdviser.Domain.Positions;
using SwingAdviser.Domain.Risk;
using SwingAdviser.Domain.Stocks;
using SwingAdviser.Infrastructure.Persistence;

namespace SwingAdviser.Tests.Infrastructure.Persistence;

/// <summary>
/// EF Coreマッピングそのものの正しさだけを見る（計算式の再検証はしない）。開いたままの:memory:接続で
/// 保存→再読込の往復を確認する。
/// </summary>
public class SwingAdviserDbContextTests
{
    private static readonly DateTime NowUtc = new(2026, 1, 5, 0, 30, 0, DateTimeKind.Utc);

    private static SwingAdviserDbContext CreateContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<SwingAdviserDbContext>()
            .UseSqlite(connection)
            .UseSnakeCaseNamingConvention()
            .Options;

        var context = new SwingAdviserDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    [Fact]
    public void Position_SaveAndReload_PreservesExecutionsSplitAndCorrectionLog()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        using (var context = CreateContext(connection))
        {
            var position = Position.Open(
                stockCode: "7203",
                direction: TradeDirection.Long,
                isMargin: false,
                executedAtUtc: NowUtc,
                price: 1000m,
                quantity: 100,
                marginDueDate: null,
                initialAtr: 30m,
                stopLossPrice: 910m,
                memo: "テストメモ",
                nowUtc: NowUtc);
            position.AddCloseExecution(NowUtc.AddDays(1), 1050m, 40, NowUtc.AddDays(1));
            position.ApplySplit(2m, NowUtc.AddDays(2));
            var openExecution = position.Executions.First(e => e.Side == ExecutionSide.Open);
            position.CorrectExecution(openExecution, openExecution.Price, openExecution.Quantity, openExecution.ExecutedAtUtc, "訂正理由", NowUtc.AddDays(3));

            context.Positions.Add(position);
            context.SaveChanges();
        }

        using (var context = CreateContext(connection))
        {
            var reloaded = context.Positions.Include(p => p.Executions).Single();

            Assert.Equal("テストメモ", reloaded.Memo);
            Assert.Equal(2, reloaded.Executions.Count);
            Assert.Equal(120m, reloaded.RemainingQuantity); // (100-40)*2（分割後）
            Assert.Equal(500m, reloaded.AverageEntryPrice); // 1000/2（分割調整後）

            var reloadedOpenExecution = reloaded.Executions.Single(e => e.Side == ExecutionSide.Open);
            Assert.Equal(2m, reloadedOpenExecution.SplitFactor);
            Assert.Equal(1000m, reloadedOpenExecution.Price); // 監査原票は変更されない
            Assert.Single(reloadedOpenExecution.CorrectionLog);
            Assert.Contains("訂正理由", reloadedOpenExecution.CorrectionLog[0]);
        }
    }

    [Fact]
    public void DailyBar_DuplicateStockCodeAndTradeDate_ViolatesUniqueConstraint()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var context = CreateContext(connection);

        var tradeDate = new DateOnly(2026, 1, 5);
        context.DailyBars.Add(new DailyBar("7203", tradeDate, 1000m, 1010m, 990m, 1005m, 100_000));
        context.SaveChanges();

        context.DailyBars.Add(new DailyBar("7203", tradeDate, 1006m, 1020m, 1000m, 1010m, 100_000));

        Assert.Throws<DbUpdateException>(() => context.SaveChanges());
    }

    [Fact]
    public void AiEvaluation_SaveAndReload_PreservesAllListsAndTerminalState()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        using (var context = CreateContext(connection))
        {
            var evaluation = AiEvaluation.Create("7203", TradeDirection.Long, NowUtc);
            evaluation.MarkRunning(NowUtc.AddMinutes(1));
            evaluation.MarkSucceeded(
                AiVerdict.Bullish,
                ConfidenceLevel.High,
                "summary",
                ["好材料1", "好材料2"],
                ["リスク1"],
                ["無効化条件1"],
                ["https://example.com/a", "https://example.com/b"],
                NowUtc.AddMinutes(2));

            context.AiEvaluations.Add(evaluation);
            context.SaveChanges();
        }

        using (var context = CreateContext(connection))
        {
            var reloaded = context.AiEvaluations.Single();

            Assert.Equal(AiEvaluationStatus.Succeeded, reloaded.Status);
            Assert.Equal(AiVerdict.Bullish, reloaded.Verdict);
            Assert.Equal(ConfidenceLevel.High, reloaded.Confidence);
            Assert.Equal(["好材料1", "好材料2"], reloaded.PositiveFactors);
            Assert.Equal(["リスク1"], reloaded.RiskFactors);
            Assert.Equal(["無効化条件1"], reloaded.InvalidationConditions);
            Assert.Equal(["https://example.com/a", "https://example.com/b"], reloaded.ReferenceUrls);
        }
    }

    [Fact]
    public void CandidateEvaluation_SaveAndReload_RoundTripsAllFields()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var evaluation = new CandidateEvaluation
        {
            EvaluationDate = new DateOnly(2026, 1, 5),
            StockCode = "7203",
            Direction = TradeDirection.Long,
            Score = 82,
            Confidence = ConfidenceLevel.High,
            Close = 1700m,
            MacdLine = 4.93m,
            MacdSignal = 3.00m,
            MacdHistogram = 1.93m,
            PreviousMacdHistogram = 1.5m,
            Ema20 = 1650m,
            Ema100 = 1600m,
            Ema100TwentyDaysAgo = 1560m,
            Atr14 = 8.5m,
            VolumeRatio = 1.8m,
            MacdCrossAgeDays = 0,
            MarketRegimeAligned = true,
            MacdFreshnessScore = 20,
            MacdPositionScore = 15,
            MacdMomentumScore = 12,
            TrendStrengthScore = 15,
            VolumeScore = 10,
            MarketRegimeScore = 15,
            StrategyParametersJson = "{}",
            CreatedAtUtc = NowUtc,
        };

        using (var context = CreateContext(connection))
        {
            context.CandidateEvaluations.Add(evaluation);
            context.SaveChanges();
        }

        using (var context = CreateContext(connection))
        {
            var reloaded = context.CandidateEvaluations.Single();

            Assert.Equal(evaluation.EvaluationDate, reloaded.EvaluationDate);
            Assert.Equal(evaluation.Direction, reloaded.Direction);
            Assert.Equal(evaluation.Score, reloaded.Score);
            Assert.Equal(evaluation.Confidence, reloaded.Confidence);
            Assert.Equal(evaluation.Ema100TwentyDaysAgo, reloaded.Ema100TwentyDaysAgo);
            Assert.Equal(evaluation.MarketRegimeAligned, reloaded.MarketRegimeAligned);
        }
    }

    [Fact]
    public void HoldingEvaluation_SaveAndReload_RoundTripsAllFields()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        long positionId;
        using (var context = CreateContext(connection))
        {
            var position = Position.Open("7203", TradeDirection.Long, false, NowUtc, 1000m, 100, null, 30m, 910m, null, NowUtc);
            context.Positions.Add(position);
            context.SaveChanges();
            positionId = position.Id;
        }

        using (var context = CreateContext(connection))
        {
            context.HoldingEvaluations.Add(new HoldingEvaluation
            {
                EvaluationDate = new DateOnly(2026, 1, 6),
                PositionId = positionId,
                Decision = HoldingDecision.Hold,
                Reason = "テスト理由",
                Close = 1020m,
                Atr14 = 30m,
                MacdLine = 5m,
                MacdSignal = 3m,
                Ema20 = 1000m,
                StopLossPrice = 910m,
                AchievedRMultiple = 1.2m,
                HoldingBusinessDays = 3,
                StrategyParametersJson = "{}",
                CreatedAtUtc = NowUtc,
            });
            context.SaveChanges();
        }

        using (var context = CreateContext(connection))
        {
            var reloaded = context.HoldingEvaluations.Single();

            Assert.Equal(positionId, reloaded.PositionId);
            Assert.Equal(HoldingDecision.Hold, reloaded.Decision);
            Assert.Equal(1.2m, reloaded.AchievedRMultiple);
        }
    }

    [Fact]
    public void EnumColumns_AreStoredAsText()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        using (var context = CreateContext(connection))
        {
            context.Stocks.Add(new Stock("7203", "トヨタ自動車", MarketSegment.Prime, true, NowUtc));
            context.SaveChanges();
        }

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT typeof(market_segment) FROM stocks LIMIT 1";
        var typeName = (string)command.ExecuteScalar()!;

        Assert.Equal("text", typeName);
    }

    [Fact]
    public void DecimalColumns_AreStoredAsText()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        using (var context = CreateContext(connection))
        {
            context.DailyBars.Add(new DailyBar("7203", new DateOnly(2026, 1, 5), 1000m, 1010m, 990m, 1005m, 100_000));
            context.SaveChanges();
        }

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT typeof(close) FROM daily_bars LIMIT 1";
        var typeName = (string)command.ExecuteScalar()!;

        Assert.Equal("text", typeName);
    }
}
