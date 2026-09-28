using SwingAdviser.Application.Risk;
using SwingAdviser.Domain.Common;
using SwingAdviser.Domain.MarketData;
using SwingAdviser.Domain.Positions;
using SwingAdviser.Domain.Risk;
using SwingAdviser.Tests.Application.TestSupport;
using SwingAdviser.Tests.Domain;

namespace SwingAdviser.Tests.Application.Risk;

public class HoldingOverviewReaderTests
{
    /// <summary>JST 2026-01-05（月曜）10:00。</summary>
    private static readonly DateTimeOffset Today = new(2026, 1, 5, 1, 0, 0, TimeSpan.Zero);

    private static HoldingOverviewReader BuildReader(SqliteInMemoryContextFactory contextFactory) =>
        new(contextFactory, TestFixtures.DefaultStrategyParameters(), new FixedTimeProvider(Today));

    [Fact]
    public async Task GetOpenPositionsAsync_NotMarginPosition_ReturnsNotMargin()
    {
        using var contextFactory = new SqliteInMemoryContextFactory();
        await using (var context = contextFactory.CreateDbContext())
        {
            var position = Position.Open("7203", TradeDirection.Long, false, Today.UtcDateTime, 1000m, 100, null, 20m, 940m, null, Today.UtcDateTime);
            context.Positions.Add(position);
            await context.SaveChangesAsync();
        }

        var result = Assert.Single(await BuildReader(contextFactory).GetOpenPositionsAsync());

        Assert.Equal(MarginDueStatus.NotMargin, result.MarginDueStatus);
        Assert.Null(result.MarginDueDate);
    }

    [Fact]
    public async Task GetOpenPositionsAsync_MarginWithoutDueDate_ReturnsUnconfirmed()
    {
        using var contextFactory = new SqliteInMemoryContextFactory();
        await using (var context = contextFactory.CreateDbContext())
        {
            var position = Position.Open("7203", TradeDirection.Long, true, Today.UtcDateTime, 1000m, 100, null, 20m, 940m, null, Today.UtcDateTime);
            context.Positions.Add(position);
            await context.SaveChangesAsync();
        }

        var result = Assert.Single(await BuildReader(contextFactory).GetOpenPositionsAsync());

        Assert.Equal(MarginDueStatus.Unconfirmed, result.MarginDueStatus);
    }

    [Fact]
    public async Task GetOpenPositionsAsync_DueDateTodayOrPast_ReturnsOverdue()
    {
        using var contextFactory = new SqliteInMemoryContextFactory();
        await using (var context = contextFactory.CreateDbContext())
        {
            var position = Position.Open(
                "7203", TradeDirection.Long, true, Today.UtcDateTime, 1000m, 100, DateOnly.FromDateTime(Today.UtcDateTime), 20m, 940m, null, Today.UtcDateTime);
            context.Positions.Add(position);
            await context.SaveChangesAsync();
        }

        var result = Assert.Single(await BuildReader(contextFactory).GetOpenPositionsAsync());

        Assert.Equal(MarginDueStatus.Overdue, result.MarginDueStatus);
    }

    [Fact]
    public async Task GetOpenPositionsAsync_DueDateWithinWarningThreshold_ReturnsWarning()
    {
        // 今日(月)から数えて5営業日後(次の月曜) = 警告閾値(5営業日)ちょうど。
        using var contextFactory = new SqliteInMemoryContextFactory();
        await using (var context = contextFactory.CreateDbContext())
        {
            var position = Position.Open(
                "7203", TradeDirection.Long, true, Today.UtcDateTime, 1000m, 100, new DateOnly(2026, 1, 12), 20m, 940m, null, Today.UtcDateTime);
            context.Positions.Add(position);
            await context.SaveChangesAsync();
        }

        var result = Assert.Single(await BuildReader(contextFactory).GetOpenPositionsAsync());

        Assert.Equal(MarginDueStatus.Warning, result.MarginDueStatus);
    }

    [Fact]
    public async Task GetOpenPositionsAsync_DueDateBeyondWarningThreshold_ReturnsNormal()
    {
        using var contextFactory = new SqliteInMemoryContextFactory();
        await using (var context = contextFactory.CreateDbContext())
        {
            var position = Position.Open(
                "7203", TradeDirection.Long, true, Today.UtcDateTime, 1000m, 100, new DateOnly(2026, 1, 13), 20m, 940m, null, Today.UtcDateTime);
            context.Positions.Add(position);
            await context.SaveChangesAsync();
        }

        var result = Assert.Single(await BuildReader(contextFactory).GetOpenPositionsAsync());

        Assert.Equal(MarginDueStatus.Normal, result.MarginDueStatus);
    }

    [Theory]
    [InlineData(TradeDirection.Long, 1100, true)]
    [InlineData(TradeDirection.Long, 900, false)]
    [InlineData(TradeDirection.Short, 900, true)]
    [InlineData(TradeDirection.Short, 1100, false)]
    public async Task GetOpenPositionsAsync_CurrentProfitAndLoss_HasCorrectSignForDirection(TradeDirection direction, decimal latestClose, bool expectPositive)
    {
        using var contextFactory = new SqliteInMemoryContextFactory();
        await using (var context = contextFactory.CreateDbContext())
        {
            var position = Position.Open("7203", direction, direction == TradeDirection.Short, Today.UtcDateTime, 1000m, 100, null, 20m, 940m, null, Today.UtcDateTime);
            context.Positions.Add(position);
            context.DailyBars.Add(new DailyBar("7203", DateOnly.FromDateTime(Today.UtcDateTime), latestClose, latestClose + 5, latestClose - 5, latestClose, 100_000));
            await context.SaveChangesAsync();
        }

        var result = Assert.Single(await BuildReader(contextFactory).GetOpenPositionsAsync());

        Assert.NotNull(result.CurrentProfitAndLoss);
        Assert.Equal(expectPositive, result.CurrentProfitAndLoss > 0);
    }

    [Fact]
    public async Task GetOpenPositionsAsync_NoHoldingEvaluation_DecisionIsNull()
    {
        using var contextFactory = new SqliteInMemoryContextFactory();
        await using (var context = contextFactory.CreateDbContext())
        {
            var position = Position.Open("7203", TradeDirection.Long, false, Today.UtcDateTime, 1000m, 100, null, 20m, 940m, null, Today.UtcDateTime);
            context.Positions.Add(position);
            await context.SaveChangesAsync();
        }

        var result = Assert.Single(await BuildReader(contextFactory).GetOpenPositionsAsync());

        Assert.Null(result.Decision);
        Assert.Null(result.DecisionReason);
    }

    [Fact]
    public async Task GetOpenPositionsAsync_WithHoldingEvaluation_ReturnsLatestByEvaluationDate()
    {
        using var contextFactory = new SqliteInMemoryContextFactory();
        long positionId;
        await using (var context = contextFactory.CreateDbContext())
        {
            var position = Position.Open("7203", TradeDirection.Long, false, Today.UtcDateTime, 1000m, 100, null, 20m, 940m, null, Today.UtcDateTime);
            context.Positions.Add(position);
            await context.SaveChangesAsync();
            positionId = position.Id;

            context.HoldingEvaluations.Add(new HoldingEvaluation
            {
                EvaluationDate = new DateOnly(2026, 1, 4),
                PositionId = positionId,
                Decision = HoldingDecision.Hold,
                Reason = "古い判定",
                Close = 1000m,
                Atr14 = 20m,
                MacdLine = 1m,
                MacdSignal = 0.5m,
                Ema20 = 995m,
                StopLossPrice = 940m,
                AchievedRMultiple = 0.5m,
                HoldingBusinessDays = 1,
                StrategyParametersJson = "{}",
                CreatedAtUtc = Today.UtcDateTime,
            });
            context.HoldingEvaluations.Add(new HoldingEvaluation
            {
                EvaluationDate = new DateOnly(2026, 1, 5),
                PositionId = positionId,
                Decision = HoldingDecision.TakeProfit,
                Reason = "新しい判定",
                Close = 1050m,
                Atr14 = 20m,
                MacdLine = 1m,
                MacdSignal = 0.5m,
                Ema20 = 1000m,
                StopLossPrice = 940m,
                AchievedRMultiple = 1.6m,
                HoldingBusinessDays = 2,
                StrategyParametersJson = "{}",
                CreatedAtUtc = Today.UtcDateTime,
            });
            await context.SaveChangesAsync();
        }

        var result = Assert.Single(await BuildReader(contextFactory).GetOpenPositionsAsync());

        Assert.Equal(HoldingDecision.TakeProfit, result.Decision);
        Assert.Equal("新しい判定", result.DecisionReason);
    }
}
