using SwingAdviser.Application.Ai;
using SwingAdviser.Application.Analysis;
using SwingAdviser.Application.DailyUpdate;
using SwingAdviser.Application.MarketData;
using SwingAdviser.Application.Positions;
using SwingAdviser.Application.Risk;
using SwingAdviser.Domain.Ai;
using SwingAdviser.Domain.Common;
using SwingAdviser.Domain.MarketData;
using SwingAdviser.Domain.Positions;
using SwingAdviser.Domain.Stocks;
using SwingAdviser.Infrastructure.Analysis;
using SwingAdviser.Infrastructure.Configuration;
using SwingAdviser.Infrastructure.MarketData;
using SwingAdviser.Presentation.ViewModels;
using SwingAdviser.Tests.Application.TestSupport;
using SwingAdviser.Tests.Domain;

namespace SwingAdviser.Tests.Presentation.ViewModels;

public class MainWindowViewModelTests
{
    /// <summary>JST 2026-01-10 17:00（大引け後）。</summary>
    private static readonly DateTimeOffset FixedNow = new(2026, 1, 10, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RunDailyUpdateCommand_Success_SetsCompletionStatusAndResetsRunningState()
    {
        using var contextFactory = new SqliteInMemoryContextFactory();
        var timeProvider = new FixedTimeProvider(FixedNow);
        var yahoo = new FakeYahooFinanceClient();
        var today = SwingAdviser.Application.Common.Jst.TodayJst(timeProvider);
        yahoo.SetResponse("1306", [new FetchedDailyBar(today, 2000, 2010, 1990, 2000, 500_000)]);

        var viewModel = BuildViewModel(contextFactory, timeProvider, yahoo, new FakeJpxListedIssuesClient());

        Assert.True(viewModel.RunDailyUpdateCommand.CanExecute(null));
        await viewModel.RunDailyUpdateCommand.ExecuteAsync();

        Assert.Contains("完了", viewModel.StatusMessage);
        Assert.False(viewModel.RunDailyUpdateCommand.IsRunning);
        Assert.True(viewModel.RunDailyUpdateCommand.CanExecute(null));
    }

    [Fact]
    public async Task RunDailyUpdateCommand_MarketRegimeSyncFails_SetsFailureStatusWithoutThrowing()
    {
        using var contextFactory = new SqliteInMemoryContextFactory();
        var timeProvider = new FixedTimeProvider(FixedNow);
        var yahoo = new FakeYahooFinanceClient();
        yahoo.SetFailure("1306", new InvalidOperationException("地合い取得失敗。"));

        var viewModel = BuildViewModel(contextFactory, timeProvider, yahoo, new FakeJpxListedIssuesClient());

        await viewModel.RunDailyUpdateCommand.ExecuteAsync();

        Assert.Contains("失敗", viewModel.StatusMessage);
        Assert.False(viewModel.RunDailyUpdateCommand.IsRunning);
        Assert.True(viewModel.RunDailyUpdateCommand.CanExecute(null));
    }

    [Fact]
    public async Task InitializeAsync_RecoversInterruptedAiEvaluations_ReportsCountInStatusMessage()
    {
        using var contextFactory = new SqliteInMemoryContextFactory();
        var timeProvider = new FixedTimeProvider(FixedNow);

        await using (var context = contextFactory.CreateDbContext())
        {
            context.AiEvaluations.Add(AiEvaluation.Create("7203", null, FixedNow.UtcDateTime));
            await context.SaveChangesAsync();
        }

        var viewModel = BuildViewModel(contextFactory, timeProvider, new FakeYahooFinanceClient(), new FakeJpxListedIssuesClient());

        await viewModel.InitializeAsync();

        Assert.Contains("1件", viewModel.StatusMessage);
    }

    private static MainWindowViewModel BuildViewModel(
        SqliteInMemoryContextFactory contextFactory, FixedTimeProvider timeProvider, FakeYahooFinanceClient yahoo, FakeJpxListedIssuesClient jpx)
    {
        var aiEvaluationService = new AiEvaluationService(
            new FakeAiCliExecutor((_, _) => throw new InvalidOperationException("このテストでは呼ばれないはず。")), contextFactory, timeProvider);

        return BuildViewModel(contextFactory, timeProvider, yahoo, jpx, aiEvaluationService);
    }

    private static MainWindowViewModel BuildViewModel(
        SqliteInMemoryContextFactory contextFactory, FixedTimeProvider timeProvider, AiEvaluationService aiEvaluationService) =>
        BuildViewModel(contextFactory, timeProvider, new FakeYahooFinanceClient(), new FakeJpxListedIssuesClient(), aiEvaluationService);

    private static MainWindowViewModel BuildViewModel(
        SqliteInMemoryContextFactory contextFactory, FixedTimeProvider timeProvider,
        FakeYahooFinanceClient yahoo, FakeJpxListedIssuesClient jpx, AiEvaluationService aiEvaluationService)
    {
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
        var strategyParameters = TestFixtures.DefaultStrategyParameters();
        var dailyUpdateService = new DailyUpdateService(
            stockMaster, barSynchronizer, contextFactory, strategyParameters, liquidityOptions, yahooOptions, timeProvider);

        return new MainWindowViewModel(
            dailyUpdateService,
            aiEvaluationService,
            new CandidateOverviewReader(contextFactory, strategyParameters),
            new HoldingOverviewReader(contextFactory, strategyParameters, timeProvider),
            new ExecutionOverviewReader(contextFactory),
            new ProfitAndLossHistoryReader(contextFactory));
    }

    [Fact]
    public async Task ReloadDisplayDataAsync_PopulatesCandidatesPositionsAndExecutions()
    {
        using var contextFactory = new SqliteInMemoryContextFactory();
        var timeProvider = new FixedTimeProvider(FixedNow);

        await using (var context = contextFactory.CreateDbContext())
        {
            context.CandidateEvaluations.Add(new SwingAdviser.Domain.Analysis.CandidateEvaluation
            {
                EvaluationDate = new DateOnly(2026, 1, 10),
                StockCode = "1111",
                Direction = TradeDirection.Long,
                Score = 80,
                Confidence = SwingAdviser.Domain.Common.ConfidenceLevel.High,
                Close = 1000m,
                MacdLine = 1m,
                MacdSignal = 0.5m,
                MacdHistogram = 0.5m,
                PreviousMacdHistogram = 0.4m,
                Ema20 = 990m,
                Ema100 = 950m,
                Ema100TwentyDaysAgo = 900m,
                Atr14 = 10m,
                VolumeRatio = 1.5m,
                MacdCrossAgeDays = 0,
                IsEarlySignal = false,
                MarketRegimeAligned = true,
                MacdFreshnessScore = 20,
                MacdPositionScore = 15,
                MacdMomentumScore = 10,
                TrendStrengthScore = 10,
                VolumeScore = 10,
                MarketRegimeScore = 15,
                StrategyParametersJson = "{}",
                CreatedAtUtc = FixedNow.UtcDateTime,
            });

            var position = Position.Open("2222", TradeDirection.Long, false, FixedNow.UtcDateTime, 500m, 100, null, 10m, 470m, null, FixedNow.UtcDateTime);
            context.Positions.Add(position);
            await context.SaveChangesAsync();
        }

        var aiEvaluationService = new AiEvaluationService(
            new FakeAiCliExecutor((_, _) => throw new InvalidOperationException("このテストでは呼ばれないはず。")), contextFactory, timeProvider);
        var viewModel = BuildViewModel(contextFactory, timeProvider, aiEvaluationService);

        await viewModel.ReloadDisplayDataAsync();

        Assert.Single(viewModel.Candidates);
        Assert.Equal("1111", viewModel.Candidates[0].StockCode);
        Assert.Single(viewModel.Positions);
        Assert.Equal("2222", viewModel.Positions[0].StockCode);
        Assert.Single(viewModel.Executions);
        Assert.Equal("2222", viewModel.Executions[0].StockCode);
    }

    [Fact]
    public async Task CandidateRow_RunAiEvaluationCommand_InvokesAiEvaluationServiceAndUpdatesStatusAfterReload()
    {
        using var contextFactory = new SqliteInMemoryContextFactory();
        var timeProvider = new FixedTimeProvider(FixedNow);

        await using (var context = contextFactory.CreateDbContext())
        {
            context.CandidateEvaluations.Add(new SwingAdviser.Domain.Analysis.CandidateEvaluation
            {
                EvaluationDate = new DateOnly(2026, 1, 10),
                StockCode = "1111",
                Direction = TradeDirection.Long,
                Score = 80,
                Confidence = SwingAdviser.Domain.Common.ConfidenceLevel.High,
                Close = 1000m,
                MacdLine = 1m,
                MacdSignal = 0.5m,
                MacdHistogram = 0.5m,
                PreviousMacdHistogram = 0.4m,
                Ema20 = 990m,
                Ema100 = 950m,
                Ema100TwentyDaysAgo = 900m,
                Atr14 = 10m,
                VolumeRatio = 1.5m,
                MacdCrossAgeDays = 0,
                IsEarlySignal = false,
                MarketRegimeAligned = true,
                MacdFreshnessScore = 20,
                MacdPositionScore = 15,
                MacdMomentumScore = 10,
                TrendStrengthScore = 10,
                VolumeScore = 10,
                MarketRegimeScore = 15,
                StrategyParametersJson = "{}",
                CreatedAtUtc = FixedNow.UtcDateTime,
            });
            await context.SaveChangesAsync();
        }

        const string successJson = """{ "verdict": "Bullish", "confidence": "High", "summary": "良好。" }""";
        var executor = new FakeAiCliExecutor((_, _) => Task.FromResult(
            new SwingAdviser.Infrastructure.Analysis.AiCliResult(successJson, string.Empty, 0, SwingAdviser.Infrastructure.Analysis.AiCliCompletion.Completed)));
        var aiEvaluationService = new AiEvaluationService(executor, contextFactory, timeProvider);
        var viewModel = BuildViewModel(contextFactory, timeProvider, aiEvaluationService);

        await viewModel.ReloadDisplayDataAsync();
        var candidate = Assert.Single(viewModel.Candidates);

        await candidate.RunAiEvaluationCommand.ExecuteAsync();

        var reloaded = Assert.Single(viewModel.Candidates);
        Assert.Equal(AiEvaluationStatus.Succeeded, reloaded.Overview.AiStatus);
        Assert.Equal("良好。", reloaded.AiSummary);
    }

    [Fact]
    public async Task CandidateRow_RunAiEvaluationCommand_DisabledWhileRunningAndRestoredAfterOtherRowReload()
    {
        using var contextFactory = new SqliteInMemoryContextFactory();
        var timeProvider = new FixedTimeProvider(FixedNow);

        await using (var context = contextFactory.CreateDbContext())
        {
            context.CandidateEvaluations.Add(BuildCandidateEvaluation("1111"));
            context.CandidateEvaluations.Add(BuildCandidateEvaluation("2222"));
            await context.SaveChangesAsync();
        }

        var gate = new TaskCompletionSource();
        var executor = new FakeAiCliExecutor(async (prompt, ct) =>
        {
            if (prompt.Contains("銘柄コード: 1111"))
            {
                await gate.Task;
            }

            return new AiCliResult("""{ "verdict": "Bullish", "confidence": "High", "summary": "ok" }""", string.Empty, 0, AiCliCompletion.Completed);
        });
        var aiEvaluationService = new AiEvaluationService(executor, contextFactory, timeProvider);
        var viewModel = BuildViewModel(contextFactory, timeProvider, aiEvaluationService);

        await viewModel.ReloadDisplayDataAsync();
        var row1 = viewModel.Candidates.Single(c => c.StockCode == "1111");
        var row2 = viewModel.Candidates.Single(c => c.StockCode == "2222");

        var runTask = row1.RunAiEvaluationCommand.ExecuteAsync();
        await row2.RunAiEvaluationCommand.ExecuteAsync();

        var reloadedRow1 = viewModel.Candidates.Single(c => c.StockCode == "1111");
        Assert.False(reloadedRow1.RunAiEvaluationCommand.CanExecute(null));

        gate.SetResult();
        await runTask;

        var finalRow1 = viewModel.Candidates.Single(c => c.StockCode == "1111");
        Assert.True(finalRow1.RunAiEvaluationCommand.CanExecute(null));
    }

    [Fact]
    public async Task ReloadDisplayDataAsync_PreservesSelectedCandidateAcrossReload()
    {
        using var contextFactory = new SqliteInMemoryContextFactory();
        var timeProvider = new FixedTimeProvider(FixedNow);

        await using (var context = contextFactory.CreateDbContext())
        {
            context.CandidateEvaluations.Add(BuildCandidateEvaluation("1111"));
            context.CandidateEvaluations.Add(BuildCandidateEvaluation("2222"));
            await context.SaveChangesAsync();
        }

        var executor = new FakeAiCliExecutor((_, _) => throw new InvalidOperationException("このテストでは呼ばれないはず。"));
        var viewModel = BuildViewModel(contextFactory, timeProvider, new AiEvaluationService(executor, contextFactory, timeProvider));

        await viewModel.ReloadDisplayDataAsync();
        viewModel.SelectedCandidate = viewModel.Candidates.Single(c => c.StockCode == "2222");

        await viewModel.ReloadDisplayDataAsync();

        Assert.NotNull(viewModel.SelectedCandidate);
        Assert.Equal("2222", viewModel.SelectedCandidate!.StockCode);
    }

    [Fact]
    public async Task RunDailyUpdateCommand_ProducesHighConfidenceCandidate_AutomaticallyTriggersAiEvaluation()
    {
        using var contextFactory = new SqliteInMemoryContextFactory();
        var timeProvider = new FixedTimeProvider(FixedNow);
        var parameters = TestFixtures.DefaultStrategyParameters();

        var stockBars = TestFixtures.BuildTrendWithPullbackAndRecovery("7203");
        var regimeBars = TestFixtures.BuildTrendWithPullbackAndRecovery("1306");
        var crossIndex = TestFixtures.FindFreshGoldenCrossIndex(stockBars, parameters);

        // 出来高スコアも確保するため、クロス当日の出来高を平常の3倍にする。
        var crossDay = stockBars[crossIndex];
        stockBars[crossIndex] = new DailyBar(
            crossDay.StockCode, crossDay.TradeDate, crossDay.Open, crossDay.High, crossDay.Low, crossDay.Close, crossDay.Volume * 3);

        var seededStockBars = stockBars.Take(crossIndex).ToList();
        var seededRegimeBars = regimeBars.Take(crossIndex).ToList();

        await using (var context = contextFactory.CreateDbContext())
        {
            context.Stocks.Add(new Stock("7203", "テスト銘柄", MarketSegment.Prime, true, FixedNow.UtcDateTime));
            context.DailyBars.AddRange(seededStockBars);
            context.DailyBars.AddRange(seededRegimeBars);
            await context.SaveChangesAsync();
        }

        var yahoo = new FakeYahooFinanceClient();
        yahoo.SetResponse("1306", [ToFetchedBar(regimeBars[crossIndex])]);
        yahoo.SetResponse("7203", [ToFetchedBar(stockBars[crossIndex])]);

        var executor = new FakeAiCliExecutor((_, _) => Task.FromResult(
            new AiCliResult("""{ "verdict": "Bullish", "confidence": "High", "summary": "良好。" }""", string.Empty, 0, AiCliCompletion.Completed)));
        var aiEvaluationService = new AiEvaluationService(executor, contextFactory, timeProvider);
        var viewModel = BuildViewModel(contextFactory, timeProvider, yahoo, new FakeJpxListedIssuesClient(), aiEvaluationService);

        await viewModel.RunDailyUpdateCommand.ExecuteAsync();

        var candidate = Assert.Single(viewModel.Candidates);
        Assert.Equal("7203", candidate.StockCode);
        Assert.Equal(ConfidenceLevel.High, candidate.Overview.Confidence);
        Assert.Equal(AiEvaluationStatus.Succeeded, candidate.Overview.AiStatus);
        Assert.Single(executor.Prompts);
        Assert.Contains("AI総合評価", viewModel.StatusMessage);
    }

    private static FetchedDailyBar ToFetchedBar(DailyBar bar) => new(bar.TradeDate, bar.Open, bar.High, bar.Low, bar.Close, bar.Volume);

    private static SwingAdviser.Domain.Analysis.CandidateEvaluation BuildCandidateEvaluation(string stockCode) => new()
    {
        EvaluationDate = new DateOnly(2026, 1, 10),
        StockCode = stockCode,
        Direction = TradeDirection.Long,
        Score = 80,
        Confidence = ConfidenceLevel.High,
        Close = 1000m,
        MacdLine = 1m,
        MacdSignal = 0.5m,
        MacdHistogram = 0.5m,
        PreviousMacdHistogram = 0.4m,
        Ema20 = 990m,
        Ema100 = 950m,
        Ema100TwentyDaysAgo = 900m,
        Atr14 = 10m,
        VolumeRatio = 1.5m,
        MacdCrossAgeDays = 0,
        IsEarlySignal = false,
        MarketRegimeAligned = true,
        MacdFreshnessScore = 20,
        MacdPositionScore = 15,
        MacdMomentumScore = 10,
        TrendStrengthScore = 10,
        VolumeScore = 10,
        MarketRegimeScore = 15,
        StrategyParametersJson = "{}",
        CreatedAtUtc = FixedNow.UtcDateTime,
    };
}
