using SwingAdviser.Application.Positions;
using SwingAdviser.Domain.Common;
using SwingAdviser.Domain.MarketData;
using SwingAdviser.Domain.Positions;
using SwingAdviser.Tests.Application.TestSupport;

namespace SwingAdviser.Tests.Application.Positions;

public class ProfitAndLossHistoryReaderTests
{
    // 03:00 UTC = 12:00 JST なので日付はJSTでも同じ。
    private static readonly DateTime Day1 = new(2026, 1, 5, 3, 0, 0, DateTimeKind.Utc);

    private static DateOnly D(int dayOffset) => DateOnly.FromDateTime(Day1.AddDays(dayOffset));

    private static DailyBar Bar(string code, int dayOffset, decimal close) =>
        new(code, D(dayOffset), close, close, close, close, 1000);

    [Fact]
    public async Task GetDailyAsync_NoPositions_ReturnsEmpty()
    {
        using var contextFactory = new SqliteInMemoryContextFactory();

        Assert.Empty(await new ProfitAndLossHistoryReader(contextFactory).GetDailyAsync());
    }

    [Fact]
    public async Task GetDailyAsync_LongPartialClose_RealizedStepsOnCloseDayAndUnrealizedUsesRemaining()
    {
        using var contextFactory = new SqliteInMemoryContextFactory();
        await using (var context = contextFactory.CreateDbContext())
        {
            var position = Position.Open("7203", TradeDirection.Long, false, Day1, 1000m, 100, null, 20m, 940m, null, Day1);
            position.AddCloseExecution(Day1.AddDays(1), 1100m, 40, Day1.AddDays(1));
            context.Positions.Add(position);
            context.DailyBars.AddRange(Bar("7203", 0, 1010m), Bar("7203", 1, 1100m), Bar("7203", 2, 1050m));
            await context.SaveChangesAsync();
        }

        var points = await new ProfitAndLossHistoryReader(contextFactory).GetDailyAsync();

        Assert.Equal(3, points.Count);
        Assert.Equal((0m, 1000m), (points[0].Realized, points[0].Unrealized));
        Assert.Equal((4000m, 6000m), (points[1].Realized, points[1].Unrealized));
        Assert.Equal((4000m, 3000m), (points[2].Realized, points[2].Unrealized));
    }

    [Fact]
    public async Task GetDailyAsync_Short_GainsWhenPriceFalls()
    {
        using var contextFactory = new SqliteInMemoryContextFactory();
        await using (var context = contextFactory.CreateDbContext())
        {
            context.Positions.Add(Position.Open("7203", TradeDirection.Short, true, Day1, 1000m, 100, null, 20m, 1050m, null, Day1));
            context.DailyBars.AddRange(Bar("7203", 0, 1000m), Bar("7203", 1, 900m));
            await context.SaveChangesAsync();
        }

        var points = await new ProfitAndLossHistoryReader(contextFactory).GetDailyAsync();

        Assert.Equal(10000m, points[1].Unrealized);
    }

    [Fact]
    public async Task GetDailyAsync_DoesNotUseBarsBeforeFirstExecutionOrPositionsBeforeTheyOpen()
    {
        using var contextFactory = new SqliteInMemoryContextFactory();
        await using (var context = contextFactory.CreateDbContext())
        {
            var early = Position.Open("7203", TradeDirection.Long, false, Day1, 1000m, 100, null, 20m, 940m, null, Day1);
            var late = Position.Open("9984", TradeDirection.Long, false, Day1.AddDays(2), 500m, 100, null, 10m, 470m, null, Day1);
            context.Positions.AddRange(early, late);
            context.DailyBars.AddRange(
                Bar("7203", -3, 100m), Bar("7203", 0, 1000m), Bar("7203", 2, 1000m),
                Bar("9984", 0, 300m), Bar("9984", 2, 510m));
            await context.SaveChangesAsync();
        }

        var points = await new ProfitAndLossHistoryReader(contextFactory).GetDailyAsync();

        Assert.Equal(D(0), points[0].Date);
        Assert.Equal(0m, points[0].Unrealized);
        Assert.Equal(1000m, points[1].Unrealized);
    }

    [Fact]
    public async Task GetDailyAsync_MissingBarCarriesForwardLastKnownClose()
    {
        using var contextFactory = new SqliteInMemoryContextFactory();
        await using (var context = contextFactory.CreateDbContext())
        {
            context.Positions.AddRange(
                Position.Open("7203", TradeDirection.Long, false, Day1, 1000m, 100, null, 20m, 940m, null, Day1),
                Position.Open("9984", TradeDirection.Long, false, Day1, 500m, 100, null, 10m, 470m, null, Day1));
            // 7203はD1の終値が無い。9984の終値がある日でも7203は直近のD0終値を引き継ぐ。
            context.DailyBars.AddRange(
                Bar("7203", 0, 1020m), Bar("7203", 2, 1100m),
                Bar("9984", 0, 500m), Bar("9984", 1, 500m), Bar("9984", 2, 500m));
            await context.SaveChangesAsync();
        }

        var points = await new ProfitAndLossHistoryReader(contextFactory).GetDailyAsync();

        Assert.Equal(2000m, points.Single(p => p.Date == D(1)).Unrealized);
    }

    [Fact]
    public async Task GetDailyAsync_SplitKeepsProfitAndLossContinuous()
    {
        using var contextFactory = new SqliteInMemoryContextFactory();
        await using (var context = contextFactory.CreateDbContext())
        {
            var position = Position.Open("7203", TradeDirection.Long, false, Day1, 1000m, 100, null, 20m, 940m, null, Day1);
            position.ApplySplit(2m, Day1.AddDays(1));
            context.Positions.Add(position);
            // 日足は分割後基準に書き換え済み（1:2分割なので過去の終値も半分）。
            context.DailyBars.AddRange(Bar("7203", 0, 505m), Bar("7203", 1, 520m));
            await context.SaveChangesAsync();
        }

        var points = await new ProfitAndLossHistoryReader(contextFactory).GetDailyAsync();

        Assert.Equal(1000m, points[0].Unrealized);
        Assert.Equal(4000m, points[1].Unrealized);
    }
}
