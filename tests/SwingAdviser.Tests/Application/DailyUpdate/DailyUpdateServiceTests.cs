using Microsoft.EntityFrameworkCore;
using SwingAdviser.Application.DailyUpdate;
using SwingAdviser.Application.MarketData;
using SwingAdviser.Domain.Common;
using SwingAdviser.Domain.MarketData;
using SwingAdviser.Domain.Positions;
using SwingAdviser.Domain.Risk;
using SwingAdviser.Domain.Stocks;
using SwingAdviser.Domain.Strategy;
using SwingAdviser.Infrastructure.Configuration;
using SwingAdviser.Infrastructure.MarketData;
using SwingAdviser.Tests.Application.TestSupport;
using SwingAdviser.Tests.Domain;

namespace SwingAdviser.Tests.Application.DailyUpdate;

public class DailyUpdateServiceTests
{
    /// <summary>JST 2026-01-10 17:00（大引け後）。</summary>
    private static readonly DateTimeOffset FixedNow = new(2026, 1, 10, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task EvaluateAsync_StoresCandidatesKeyedToEvaluationDate_AndIsIdempotentOnRerun()
    {
        using var contextFactory = new SqliteInMemoryContextFactory();
        var parameters = TestFixtures.DefaultStrategyParameters();
        var stockBars = TestFixtures.BuildTrendWithPullbackAndRecovery("7203");
        var regimeBars = TestFixtures.BuildTrendWithPullbackAndRecovery("1306");
        var crossIndex = TestFixtures.FindFreshGoldenCrossIndex(stockBars, parameters);
        var bars = stockBars.Take(crossIndex + 1).ToList();
        var regime = regimeBars.Take(crossIndex + 1).ToList();
        var evaluationDate = bars[^1].TradeDate;

        await using (var context = contextFactory.CreateDbContext())
        {
            context.Stocks.Add(new Stock("7203", "テスト銘柄", MarketSegment.Prime, true, DateTime.UtcNow));
            context.DailyBars.AddRange(bars);
            context.DailyBars.AddRange(regime);
            await context.SaveChangesAsync();
        }

        var (service, _, _, _) = BuildService(contextFactory, parameters);

        var (candidateCount1, _) = await service.EvaluateAsync(evaluationDate);
        Assert.Equal(1, candidateCount1);

        var (candidateCount2, _) = await service.EvaluateAsync(evaluationDate);
        Assert.Equal(1, candidateCount2);

        await using var verify = contextFactory.CreateDbContext();
        var rows = verify.CandidateEvaluations.Where(c => c.EvaluationDate == evaluationDate).ToList();
        Assert.Single(rows);
    }

    [Fact]
    public async Task EvaluateAsync_StoresHoldingEvaluationForOpenPosition()
    {
        using var contextFactory = new SqliteInMemoryContextFactory();
        var parameters = TestFixtures.DefaultStrategyParameters();
        var (evaluationDate, positionId) = await SeedHoldingScenarioAsync(contextFactory);

        var (service, _, _, _) = BuildService(contextFactory, parameters);
        var (_, holdingCount) = await service.EvaluateAsync(evaluationDate);

        Assert.Equal(1, holdingCount);
        await using var verify = contextFactory.CreateDbContext();
        var evaluation = verify.HoldingEvaluations.Single();
        Assert.Equal(positionId, evaluation.PositionId);
        Assert.Equal(evaluationDate, evaluation.EvaluationDate);
    }

    [Fact]
    public async Task EvaluateAsync_FutureBarsDoNotAffectPastEvaluationDate()
    {
        using var contextFactory = new SqliteInMemoryContextFactory();
        var parameters = TestFixtures.DefaultStrategyParameters();
        var (evaluationDate, _) = await SeedHoldingScenarioAsync(contextFactory);

        var (service, _, _, _) = BuildService(contextFactory, parameters);
        await service.EvaluateAsync(evaluationDate);

        HoldingDecision firstDecision;
        decimal firstAchievedR;
        await using (var verify1 = contextFactory.CreateDbContext())
        {
            var e = verify1.HoldingEvaluations.Single();
            firstDecision = e.Decision;
            firstAchievedR = e.AchievedRMultiple;
        }

        // 評価日より後の極端な「未来」バーを追加しても、evaluationDate時点の再評価は変わらないはず。
        await using (var context = contextFactory.CreateDbContext())
        {
            context.DailyBars.Add(new DailyBar("9999", evaluationDate.AddDays(1), 1m, 100_000m, 1m, 99_999m, 1_000_000));
            await context.SaveChangesAsync();
        }

        await service.EvaluateAsync(evaluationDate);

        await using var verify2 = contextFactory.CreateDbContext();
        var evaluations = verify2.HoldingEvaluations.Where(h => h.EvaluationDate == evaluationDate).ToList();
        var evaluation2 = Assert.Single(evaluations);
        Assert.Equal(firstDecision, evaluation2.Decision);
        Assert.Equal(firstAchievedR, evaluation2.AchievedRMultiple);
    }

    [Fact]
    public async Task RunAsync_OneStockFailsToSync_OthersStillSucceedAndAreReported()
    {
        using var contextFactory = new SqliteInMemoryContextFactory();
        var parameters = TestFixtures.DefaultStrategyParameters();
        var nowUtc = FixedNow.UtcDateTime;

        await using (var context = contextFactory.CreateDbContext())
        {
            context.Stocks.Add(new Stock("AAA", "AAA Corp", MarketSegment.Prime, true, nowUtc));
            context.Stocks.Add(new Stock("BBB", "BBB Corp", MarketSegment.Prime, true, nowUtc));
            await context.SaveChangesAsync();
        }

        var (service, yahoo, _, timeProvider) = BuildService(contextFactory, parameters);
        var today = SwingAdviser.Application.Common.Jst.TodayJst(timeProvider);
        yahoo.SetResponse("1306", [new FetchedDailyBar(today, 2000, 2010, 1990, 2000, 500_000)]);
        yahoo.SetResponse("AAA", [new FetchedDailyBar(today, 1000, 1010, 990, 1000, 500_000)]);
        yahoo.SetFailure("BBB", new InvalidOperationException("テスト用の取得失敗。"));

        var result = await service.RunAsync();

        Assert.Equal(1, result.SyncedCount);
        var failure = Assert.Single(result.Failures);
        Assert.Equal("BBB", failure.StockCode);
    }

    [Fact]
    public async Task RunAsync_MarketRegimeSyncFails_AbortsBeforeEvaluating()
    {
        using var contextFactory = new SqliteInMemoryContextFactory();
        var parameters = TestFixtures.DefaultStrategyParameters();

        var (service, yahoo, _, _) = BuildService(contextFactory, parameters);
        yahoo.SetFailure("1306", new InvalidOperationException("地合い取得失敗。"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RunAsync());

        await using var verify = contextFactory.CreateDbContext();
        Assert.Empty(verify.CandidateEvaluations);
        Assert.Empty(verify.HoldingEvaluations);
    }

    [Fact]
    public async Task RunAsync_DoesNotCreatePositionsOrExecutions()
    {
        using var contextFactory = new SqliteInMemoryContextFactory();
        var parameters = TestFixtures.DefaultStrategyParameters();
        var nowUtc = FixedNow.UtcDateTime;

        long positionId;
        await using (var context = contextFactory.CreateDbContext())
        {
            var position = Position.Open("AAA", TradeDirection.Long, false, nowUtc.AddDays(-1), 1000m, 100, null, 20m, 940m, null, nowUtc);
            context.Positions.Add(position);
            context.Stocks.Add(new Stock("AAA", "AAA Corp", MarketSegment.Prime, true, nowUtc));
            await context.SaveChangesAsync();
            positionId = position.Id;
        }

        var (service, yahoo, _, timeProvider) = BuildService(contextFactory, parameters);
        var today = SwingAdviser.Application.Common.Jst.TodayJst(timeProvider);
        yahoo.SetResponse("1306", [new FetchedDailyBar(today, 2000, 2010, 1990, 2000, 500_000)]);
        yahoo.SetResponse("AAA", [new FetchedDailyBar(today, 1000, 1010, 990, 1000, 500_000)]);

        await service.RunAsync();

        await using var verify = contextFactory.CreateDbContext();
        Assert.Equal(1, verify.Positions.Count());
        var reloadedPosition = await verify.Positions.Include(p => p.Executions).SingleAsync(p => p.Id == positionId);
        Assert.Single(reloadedPosition.Executions);
    }

    private static async Task<(DateOnly EvaluationDate, long PositionId)> SeedHoldingScenarioAsync(SqliteInMemoryContextFactory contextFactory)
    {
        var holdingBars = TestFixtures.BuildTrendWithPullbackAndRecovery("9999").Take(210).ToList();
        var evaluationDate = holdingBars[^1].TradeDate;
        var openedDate = holdingBars[0].TradeDate;
        var openedAtUtc = new DateTime(openedDate.Year, openedDate.Month, openedDate.Day, 3, 0, 0, DateTimeKind.Utc);

        long positionId;
        await using (var context = contextFactory.CreateDbContext())
        {
            context.DailyBars.AddRange(holdingBars);
            var position = Position.Open(
                "9999", TradeDirection.Long, false, openedAtUtc, holdingBars[0].Close, 100, null, 20m, holdingBars[0].Close - 60m, null, openedAtUtc);
            context.Positions.Add(position);
            await context.SaveChangesAsync();
            positionId = position.Id;
        }

        return (evaluationDate, positionId);
    }

    private static (DailyUpdateService Service, FakeYahooFinanceClient Yahoo, FakeJpxListedIssuesClient Jpx, FixedTimeProvider TimeProvider) BuildService(
        SqliteInMemoryContextFactory contextFactory, StrategyParameters parameters)
    {
        var yahoo = new FakeYahooFinanceClient();
        var jpx = new FakeJpxListedIssuesClient();
        var timeProvider = new FixedTimeProvider(FixedNow);
        var yahooOptions = new YahooFinanceOptions
        {
            BaseUrl = "https://example.com/",
            MaxRequestsPerSecond = 5,
            TimeoutSeconds = 10,
            InitialFetchCalendarDays = 400,
            DailyBarFinalizedTimeJst = "16:00",
        };
        var jpxOptions = new JpxOptions { ListedIssuesUrl = "https://example.com/list.xlsx", RefreshIntervalDays = 7 };
        var liquidityOptions = new LiquidityFilterOptions
        {
            MinimumAverageTurnoverJpy = 100_000_000m,
            TurnoverAveragePeriodDays = 20,
            RecheckIntervalDays = 7,
        };

        var stockMaster = new StockMasterSynchronizer(jpx, contextFactory, jpxOptions, timeProvider);
        var barSynchronizer = new DailyBarSynchronizer(yahoo, contextFactory, yahooOptions, timeProvider);
        var service = new DailyUpdateService(stockMaster, barSynchronizer, contextFactory, parameters, liquidityOptions, yahooOptions, timeProvider);
        return (service, yahoo, jpx, timeProvider);
    }
}
