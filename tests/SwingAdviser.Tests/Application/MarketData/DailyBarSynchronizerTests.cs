using Microsoft.EntityFrameworkCore;
using SwingAdviser.Application.Common;
using SwingAdviser.Application.MarketData;
using SwingAdviser.Domain.Common;
using SwingAdviser.Domain.MarketData;
using SwingAdviser.Domain.Positions;
using SwingAdviser.Infrastructure.Configuration;
using SwingAdviser.Infrastructure.MarketData;
using SwingAdviser.Tests.Application.TestSupport;

namespace SwingAdviser.Tests.Application.MarketData;

public class DailyBarSynchronizerTests
{
    private static YahooFinanceOptions DefaultOptions() => new()
    {
        BaseUrl = "https://example.com/",
        MaxRequestsPerSecond = 5,
        TimeoutSeconds = 10,
        InitialFetchCalendarDays = 400,
        DailyBarFinalizedTimeJst = "16:00",
    };

    /// <summary>JST 2026-01-10 17:00（大引け後、確定時刻を過ぎている）。</summary>
    private static readonly DateTimeOffset AfterFinalizedNow = new(2026, 1, 10, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SyncAsync_NoStoredBars_FetchesSinceInitialLookbackFromToday()
    {
        using var contextFactory = new SqliteInMemoryContextFactory();
        var timeProvider = new FixedTimeProvider(AfterFinalizedNow);
        var yahoo = new FakeYahooFinanceClient();
        yahoo.SetResponse("7203", [new FetchedDailyBar(new DateOnly(2026, 1, 9), 100, 110, 90, 105, 1000)]);

        var synchronizer = new DailyBarSynchronizer(yahoo, contextFactory, DefaultOptions(), timeProvider);
        var result = await synchronizer.SyncAsync("7203");

        var call = Assert.Single(yahoo.Calls);
        Assert.Equal("7203", call.Code);
        var todayJst = Jst.TodayJst(timeProvider);
        Assert.Equal(todayJst.AddDays(-400), call.Since);
        Assert.Equal(1, result.UpsertedBarCount);
    }

    [Fact]
    public async Task SyncAsync_ExistingBars_FetchesSinceLastStoredDateAndUpsertsBoth()
    {
        using var contextFactory = new SqliteInMemoryContextFactory();
        var lastStored = new DateOnly(2026, 1, 8);
        await using (var context = contextFactory.CreateDbContext())
        {
            context.DailyBars.Add(new DailyBar("7203", lastStored, 100, 110, 90, 100, 1000));
            await context.SaveChangesAsync();
        }

        var timeProvider = new FixedTimeProvider(AfterFinalizedNow);
        var yahoo = new FakeYahooFinanceClient();
        yahoo.SetResponse("7203",
        [
            new FetchedDailyBar(lastStored, 100, 112, 90, 108, 1200), // Yahoo側の訂正値
            new FetchedDailyBar(lastStored.AddDays(1), 108, 115, 105, 110, 1500),
        ]);

        var synchronizer = new DailyBarSynchronizer(yahoo, contextFactory, DefaultOptions(), timeProvider);
        var result = await synchronizer.SyncAsync("7203");

        var call = Assert.Single(yahoo.Calls);
        Assert.Equal(lastStored, call.Since);
        Assert.Equal(2, result.UpsertedBarCount);

        await using var verify = contextFactory.CreateDbContext();
        var bars = verify.DailyBars.OrderBy(b => b.TradeDate).ToList();
        Assert.Equal(2, bars.Count);
        Assert.Equal(108m, bars[0].Close);
        Assert.Equal(110m, bars[1].Close);
    }

    [Fact]
    public async Task SyncAsync_BeforeFinalizedTimeJst_DropsTodayBar()
    {
        using var contextFactory = new SqliteInMemoryContextFactory();
        // JST 2026-01-10 10:00（確定時刻16:00より前）
        var beforeFinalizedNow = new DateTimeOffset(2026, 1, 10, 1, 0, 0, TimeSpan.Zero);
        var timeProvider = new FixedTimeProvider(beforeFinalizedNow);
        var yahoo = new FakeYahooFinanceClient();
        var today = Jst.TodayJst(timeProvider);
        yahoo.SetResponse("7203", [new FetchedDailyBar(today, 100, 110, 90, 105, 1000)]);

        var synchronizer = new DailyBarSynchronizer(yahoo, contextFactory, DefaultOptions(), timeProvider);
        var result = await synchronizer.SyncAsync("7203");

        Assert.Equal(0, result.UpsertedBarCount);
        await using var verify = contextFactory.CreateDbContext();
        Assert.Empty(verify.DailyBars);
    }

    [Fact]
    public async Task SyncAsync_SplitEvent_AdjustsStoredBarAndOpenPositionWithExecutionsBeforeEffectiveDate()
    {
        using var contextFactory = new SqliteInMemoryContextFactory();
        var storedDate = new DateOnly(2026, 1, 5);
        var effectiveDate = new DateOnly(2026, 1, 6);
        var executedAtUtc = new DateTime(2026, 1, 5, 3, 0, 0, DateTimeKind.Utc); // JST 2026-01-05 正午

        long positionId;
        await using (var context = contextFactory.CreateDbContext())
        {
            context.DailyBars.Add(new DailyBar("7203", storedDate, 1000, 1010, 990, 1000, 100_000));
            var position = Position.Open(
                "7203", TradeDirection.Long, false, executedAtUtc, 1000m, 100, null, 30m, 910m, null, executedAtUtc);
            context.Positions.Add(position);
            await context.SaveChangesAsync();
            positionId = position.Id;
        }

        var timeProvider = new FixedTimeProvider(AfterFinalizedNow);
        var yahoo = new FakeYahooFinanceClient();
        yahoo.SetResponse(
            "7203",
            [new FetchedDailyBar(effectiveDate, 500, 510, 490, 500, 200_000)],
            [new YahooSplitEvent(effectiveDate, 2m)]);

        var synchronizer = new DailyBarSynchronizer(yahoo, contextFactory, DefaultOptions(), timeProvider);
        var result = await synchronizer.SyncAsync("7203");

        Assert.Empty(result.Warnings);

        await using var verify = contextFactory.CreateDbContext();
        var oldBar = verify.DailyBars.Single(b => b.TradeDate == storedDate);
        Assert.Equal(500m, oldBar.Close);
        Assert.Equal(200_000, oldBar.Volume);

        var reloadedPosition = await verify.Positions.Include(p => p.Executions).SingleAsync(p => p.Id == positionId);
        Assert.Equal(455m, reloadedPosition.StopLossPrice);
        Assert.Equal(15m, reloadedPosition.InitialAtr);
        Assert.Equal(2m, reloadedPosition.Executions.Single().SplitFactor);
    }

    [Fact]
    public async Task SyncAsync_SplitEvent_PositionWithExecutionOnOrAfterEffectiveDate_WarnsAndSkipsAdjustment()
    {
        using var contextFactory = new SqliteInMemoryContextFactory();
        var effectiveDate = new DateOnly(2026, 1, 6);
        var executedAtUtc = new DateTime(2026, 1, 6, 3, 0, 0, DateTimeKind.Utc); // JST 2026-01-06 正午（効力発生日当日）

        long positionId;
        await using (var context = contextFactory.CreateDbContext())
        {
            var position = Position.Open(
                "7203", TradeDirection.Long, false, executedAtUtc, 1000m, 100, null, 30m, 910m, null, executedAtUtc);
            context.Positions.Add(position);
            await context.SaveChangesAsync();
            positionId = position.Id;
        }

        var timeProvider = new FixedTimeProvider(AfterFinalizedNow);
        var yahoo = new FakeYahooFinanceClient();
        yahoo.SetResponse(
            "7203",
            [new FetchedDailyBar(effectiveDate, 500, 510, 490, 500, 200_000)],
            [new YahooSplitEvent(effectiveDate, 2m)]);

        var synchronizer = new DailyBarSynchronizer(yahoo, contextFactory, DefaultOptions(), timeProvider);
        var result = await synchronizer.SyncAsync("7203");

        Assert.Single(result.Warnings);

        await using var verify = contextFactory.CreateDbContext();
        var reloadedPosition = await verify.Positions.SingleAsync(p => p.Id == positionId);
        Assert.Equal(910m, reloadedPosition.StopLossPrice);
        Assert.Equal(30m, reloadedPosition.InitialAtr);
    }
}
