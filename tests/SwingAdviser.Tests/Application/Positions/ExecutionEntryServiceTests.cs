using Microsoft.EntityFrameworkCore;
using SwingAdviser.Application.MarketData;
using SwingAdviser.Application.Positions;
using SwingAdviser.Domain.Common;
using SwingAdviser.Domain.MarketData;
using SwingAdviser.Domain.Positions;
using SwingAdviser.Infrastructure.Configuration;
using SwingAdviser.Infrastructure.MarketData;
using SwingAdviser.Tests.Application.TestSupport;
using SwingAdviser.Tests.Domain;

namespace SwingAdviser.Tests.Application.Positions;

public class ExecutionEntryServiceTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 1, 6, 8, 0, 0, TimeSpan.Zero); // JST 2026-01-06 17:00

    private static YahooFinanceOptions YahooOptions() => new()
    {
        BaseUrl = "https://example.com/",
        MaxRequestsPerSecond = 5,
        TimeoutSeconds = 10,
        InitialFetchCalendarDays = 400,
        DailyBarFinalizedTimeJst = "16:00",
    };

    private static List<DailyBar> BuildBars(string code, DateOnly endDate, int count)
    {
        var bars = new List<DailyBar>();
        var price = 1000m;
        var startDate = endDate.AddDays(-(count - 1));
        for (var i = 0; i < count; i++)
        {
            price += 5m;
            var close = price;
            var open = price - 2m;
            var high = price + 5m;
            var low = price - 7m;
            bars.Add(new DailyBar(code, startDate.AddDays(i), open, high, low, close, 300_000L));
        }

        return bars;
    }

    private static FetchedDailyBar ToFetchedDailyBar(DailyBar bar) => new(bar.TradeDate, bar.Open, bar.High, bar.Low, bar.Close, bar.Volume);

    private static (ExecutionEntryService Service, SqliteInMemoryContextFactory ContextFactory, List<DailyBar> Bars) BuildServiceWithBars(
        string code = "7203", int barCount = 30)
    {
        var contextFactory = new SqliteInMemoryContextFactory();
        var bars = BuildBars(code, new DateOnly(2026, 1, 2), barCount);
        using (var context = contextFactory.CreateDbContext())
        {
            context.DailyBars.AddRange(bars);
            context.SaveChanges();
        }

        var yahoo = new FakeYahooFinanceClient();
        yahoo.SetResponse(code, [ToFetchedDailyBar(bars[^1])]);
        var timeProvider = new FixedTimeProvider(FixedNow);
        var barSynchronizer = new DailyBarSynchronizer(yahoo, contextFactory, YahooOptions(), timeProvider);
        var service = new ExecutionEntryService(barSynchronizer, contextFactory, TestFixtures.DefaultStrategyParameters(), timeProvider);
        return (service, contextFactory, bars);
    }

    [Fact]
    public async Task PreviewOpenPositionAsync_DoesNotPersistAnything()
    {
        var (service, contextFactory, _) = BuildServiceWithBars();
        using var _1 = contextFactory;

        var input = new OpenPositionInput("7203", TradeDirection.Long, false, new DateTime(2026, 1, 5, 9, 0, 0), 1200m, 100, null, null);
        var preview = await service.PreviewOpenPositionAsync(input);

        Assert.True(preview.Atr14 > 0);
        await using var verify = contextFactory.CreateDbContext();
        Assert.Empty(verify.Positions);
        Assert.Empty(verify.DailyBars.Where(b => b.TradeDate > new DateOnly(2026, 1, 2)));
    }

    [Fact]
    public async Task ConfirmOpenPositionAsync_PersistsPreviewStopLossAndAtrExactly()
    {
        var (service, contextFactory, _) = BuildServiceWithBars();
        using var _1 = contextFactory;

        var input = new OpenPositionInput("7203", TradeDirection.Long, false, new DateTime(2026, 1, 5, 9, 0, 0), 1200m, 100, null, null);
        var preview = await service.PreviewOpenPositionAsync(input);

        var positionId = await service.ConfirmOpenPositionAsync(preview);

        await using var verify = contextFactory.CreateDbContext();
        var position = verify.Positions.Single(p => p.Id == positionId);
        Assert.Equal(preview.StopLossPrice, position.StopLossPrice);
        Assert.Equal(preview.Atr14, position.InitialAtr);
    }

    [Fact]
    public async Task PreviewOpenPositionAsync_Short_StopLossIsAbovePrice()
    {
        var (service, contextFactory, _) = BuildServiceWithBars();
        using var _1 = contextFactory;

        var input = new OpenPositionInput("7203", TradeDirection.Short, true, new DateTime(2026, 1, 5, 9, 0, 0), 1200m, 100, new DateOnly(2026, 2, 1), null);
        var preview = await service.PreviewOpenPositionAsync(input);

        Assert.True(preview.StopLossPrice > input.Price);
    }

    [Fact]
    public async Task PreviewOpenPositionAsync_MarginWithoutDueDate_AddsWarning()
    {
        var (service, contextFactory, _) = BuildServiceWithBars();
        using var _1 = contextFactory;

        var input = new OpenPositionInput("7203", TradeDirection.Long, true, new DateTime(2026, 1, 5, 9, 0, 0), 1200m, 100, null, null);
        var preview = await service.PreviewOpenPositionAsync(input);

        Assert.Single(preview.Warnings);
    }

    [Fact]
    public async Task PreviewOpenPositionAsync_FutureExecutedAt_Throws()
    {
        var (service, contextFactory, _) = BuildServiceWithBars();
        using var _1 = contextFactory;

        var input = new OpenPositionInput("7203", TradeDirection.Long, false, new DateTime(2026, 1, 10, 9, 0, 0), 1000m, 100, null, null);

        await Assert.ThrowsAsync<ArgumentException>(() => service.PreviewOpenPositionAsync(input));
    }

    [Fact]
    public async Task PreviewOpenPositionAsync_Short_WithoutMargin_Throws()
    {
        var (service, contextFactory, _) = BuildServiceWithBars();
        using var _1 = contextFactory;

        var input = new OpenPositionInput("7203", TradeDirection.Short, false, new DateTime(2026, 1, 5, 9, 0, 0), 1000m, 100, null, null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.PreviewOpenPositionAsync(input));
    }

    [Fact]
    public async Task PreviewAddExecutionAsync_QuantityExceedsRemaining_Throws()
    {
        using var contextFactory = new SqliteInMemoryContextFactory();
        var timeProvider = new FixedTimeProvider(FixedNow);
        long positionId;
        await using (var context = contextFactory.CreateDbContext())
        {
            var position = Position.Open(
                "7203", TradeDirection.Long, false, new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc), 1000m, 100, null, 20m, 940m, null, DateTime.UtcNow);
            context.Positions.Add(position);
            await context.SaveChangesAsync();
            positionId = position.Id;
        }

        var yahoo = new FakeYahooFinanceClient();
        var barSynchronizer = new DailyBarSynchronizer(yahoo, contextFactory, YahooOptions(), timeProvider);
        var service = new ExecutionEntryService(barSynchronizer, contextFactory, TestFixtures.DefaultStrategyParameters(), timeProvider);

        var input = new AddExecutionInput(positionId, ExecutionSide.Close, new DateTime(2026, 1, 6, 9, 0, 0), 1100m, 150, null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.PreviewAddExecutionAsync(input));
    }

    [Fact]
    public async Task PreviewAddExecutionAsync_FutureExecutedAt_Throws()
    {
        using var contextFactory = new SqliteInMemoryContextFactory();
        var timeProvider = new FixedTimeProvider(FixedNow);
        long positionId;
        await using (var context = contextFactory.CreateDbContext())
        {
            var position = Position.Open(
                "7203", TradeDirection.Long, false, new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc), 1000m, 100, null, 20m, 940m, null, DateTime.UtcNow);
            context.Positions.Add(position);
            await context.SaveChangesAsync();
            positionId = position.Id;
        }

        var yahoo = new FakeYahooFinanceClient();
        var barSynchronizer = new DailyBarSynchronizer(yahoo, contextFactory, YahooOptions(), timeProvider);
        var service = new ExecutionEntryService(barSynchronizer, contextFactory, TestFixtures.DefaultStrategyParameters(), timeProvider);

        var input = new AddExecutionInput(positionId, ExecutionSide.Close, new DateTime(2026, 1, 10, 9, 0, 0), 1100m, 10, null);

        await Assert.ThrowsAsync<ArgumentException>(() => service.PreviewAddExecutionAsync(input));
    }

    [Fact]
    public async Task ConfirmAddExecutionAsync_ClosePartialQuantity_UpdatesRemainingQuantity()
    {
        using var contextFactory = new SqliteInMemoryContextFactory();
        var timeProvider = new FixedTimeProvider(FixedNow);
        long positionId;
        await using (var context = contextFactory.CreateDbContext())
        {
            var position = Position.Open(
                "7203", TradeDirection.Long, false, new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc), 1000m, 100, null, 20m, 940m, null, DateTime.UtcNow);
            context.Positions.Add(position);
            await context.SaveChangesAsync();
            positionId = position.Id;
        }

        var yahoo = new FakeYahooFinanceClient();
        var barSynchronizer = new DailyBarSynchronizer(yahoo, contextFactory, YahooOptions(), timeProvider);
        var service = new ExecutionEntryService(barSynchronizer, contextFactory, TestFixtures.DefaultStrategyParameters(), timeProvider);

        var input = new AddExecutionInput(positionId, ExecutionSide.Close, new DateTime(2026, 1, 6, 9, 0, 0), 1100m, 40, null);
        var preview = await service.PreviewAddExecutionAsync(input);
        Assert.Equal(60m, preview.RemainingQuantityAfter);
        Assert.False(preview.WillFullyClose);

        await service.ConfirmAddExecutionAsync(preview);

        await using var verify = contextFactory.CreateDbContext();
        var position2 = await verify.Positions.Include(p => p.Executions).SingleAsync(p => p.Id == positionId);
        Assert.Equal(60m, position2.RemainingQuantity);
        Assert.Equal(PositionStatus.Open, position2.Status);
    }
}
