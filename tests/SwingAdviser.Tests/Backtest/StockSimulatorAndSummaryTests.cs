using SwingAdviser.Backtest;
using SwingAdviser.Domain.Analysis;
using SwingAdviser.Domain.Common;
using SwingAdviser.Domain.MarketData;
using SwingAdviser.Infrastructure.Configuration;
using SwingAdviser.Tests.Domain;

namespace SwingAdviser.Tests.Backtest;

public class StockSimulatorAndSummaryTests
{
    private static readonly LiquidityFilterOptions Liquidity = new()
    {
        MinimumAverageTurnoverJpy = 100_000_000m,
        TurnoverAveragePeriodDays = 20,
        RecheckIntervalDays = 7,
    };

    [Fact]
    public void Run_ChangingBarsAfterEntryDay_DoesNotChangeSignalOrEntry()
    {
        var parameters = TestFixtures.DefaultStrategyParameters();
        var original = TestFixtures.BuildTrendWithPullbackAndRecovery("1000");
        var regime = new MarketCalendar(TestFixtures.BuildTrendWithPullbackAndRecovery("1306"));
        var simulator = new StockSimulator(parameters, Liquidity);

        var originalTrades = simulator.Run("1000", original, regime, null, null);
        Assert.NotEmpty(originalTrades);

        // 最初のシグナル日の翌日（建玉日）までは同一、それ以降の日足は価格を3倍にして書き換える
        var firstSignalDate = originalTrades.Min(t => t.Trade.SignalDate);
        var entryIndex = original.FindIndex(b => b.TradeDate == firstSignalDate) + 1;
        var tampered = original
            .Select((b, i) => i <= entryIndex
                ? b
                : new DailyBar("1000", b.TradeDate, b.Open * 3, b.High * 3, b.Low * 3, b.Close * 3, b.Volume))
            .ToList();

        var tamperedTrades = simulator.Run("1000", tampered, regime, null, firstSignalDate);

        var before = originalTrades.Single(t => t.Trade.SignalDate == firstSignalDate && t.Trade.Direction == originalTrades.First(x => x.Trade.SignalDate == firstSignalDate).Trade.Direction);
        var after = tamperedTrades.Single(t => t.Trade.SignalDate == firstSignalDate && t.Trade.Direction == before.Trade.Direction);
        Assert.Equal(before.Candidate.Score, after.Candidate.Score);
        Assert.Equal(before.Candidate.Atr14, after.Candidate.Atr14);
        Assert.Equal(before.Trade.EntryDate, after.Trade.EntryDate);
        Assert.Equal(before.Trade.EntryPrice, after.Trade.EntryPrice);
        Assert.Equal(before.Trade.StopLossPrice, after.Trade.StopLossPrice);
    }

    [Fact]
    public void Run_NeverHasTwoOverlappingTradesForSameStockAndDirection()
    {
        var parameters = TestFixtures.DefaultStrategyParameters();
        var bars = TestFixtures.BuildTrendWithPullbackAndRecovery("1000");
        var regime = new MarketCalendar(TestFixtures.BuildTrendWithPullbackAndRecovery("1306"));

        var trades = new StockSimulator(parameters, Liquidity).Run("1000", bars, regime, null, null);

        foreach (var group in trades.GroupBy(t => t.Trade.Direction))
        {
            var ordered = group.OrderBy(t => t.Trade.SignalDate).ToList();
            for (var i = 1; i < ordered.Count; i++)
            {
                Assert.True(ordered[i].Trade.SignalDate >= ordered[i - 1].Trade.ExitDate);
            }
        }
    }

    [Fact]
    public void Compute_ReturnsHandCalculatedStatistics()
    {
        // 決済日順の累積R: +2, +1, 0, +1 → ピーク2から0まで下落、最大DD=2。
        var trades = new[]
        {
            Trade(2m, new DateOnly(2025, 1, 10)),
            Trade(-1m, new DateOnly(2025, 1, 20)),
            Trade(-1m, new DateOnly(2025, 1, 30)),
            Trade(1m, new DateOnly(2025, 2, 10)),
        };

        var stats = Summary.Compute("全体", "全体", trades);

        Assert.Equal(4, stats.Count);
        Assert.Equal(0.5, stats.WinRate, 6);
        Assert.Equal(0.25, stats.AverageR, 6);
        Assert.Equal(0.0, stats.MedianR, 6);
        Assert.Equal(1.5, stats.ProfitFactor, 6);
        Assert.Equal(2.0, stats.MaxDrawdownR, 6);
    }

    [Fact]
    public void Compute_NoLosses_ProfitFactorIsInfinity_AndEmptyIsZero()
    {
        var wins = Summary.Compute("g", "l", [Trade(1m, new DateOnly(2025, 1, 1))]);
        var empty = Summary.Compute("g", "l", []);

        Assert.True(double.IsPositiveInfinity(wins.ProfitFactor));
        Assert.Equal(0, empty.Count);
    }

    [Fact]
    public void Breakdown_SplitsTradesByDirectionAndSplitDate()
    {
        var trades = new[]
        {
            Trade(1m, new DateOnly(2025, 1, 10), TradeDirection.Long),
            Trade(-1m, new DateOnly(2025, 6, 10), TradeDirection.Short),
        };

        var rows = Summary.Breakdown(trades, new DateOnly(2025, 4, 1));

        Assert.Equal(1, rows.Single(r => r.Group == "方向" && r.Label == "Long").Count);
        Assert.Equal(1, rows.Single(r => r.Group == "方向" && r.Label == "Short").Count);
        Assert.Equal(1, rows.Single(r => r.Group == "期間" && r.Label.StartsWith("前半", StringComparison.Ordinal)).Count);
        Assert.Equal(1, rows.Single(r => r.Group == "期間" && r.Label.StartsWith("後半", StringComparison.Ordinal)).Count);
    }

    private static BacktestTrade Trade(decimal resultR, DateOnly exitDate, TradeDirection direction = TradeDirection.Long)
    {
        var simulated = new SimulatedTrade(
            "1000", direction, exitDate.AddDays(-5), exitDate.AddDays(-4), 1000m, 970m, exitDate, 1000m + (resultR * 30m),
            resultR, Math.Max(resultR, 0m), Math.Min(resultR, 0m), 4, false, ExitReason.Exit, 0);

        var candidate = new CandidateEvaluation
        {
            EvaluationDate = simulated.SignalDate,
            StockCode = "1000",
            Direction = direction,
            Score = 55,
            Confidence = ConfidenceLevel.Medium,
            Close = 1000m,
            MacdLine = 1m,
            MacdSignal = 0m,
            MacdHistogram = 1m,
            PreviousMacdHistogram = 0.5m,
            Ema20 = 990m,
            Ema100 = 950m,
            Ema100TwentyDaysAgo = 940m,
            Atr14 = 10m,
            VolumeRatio = 1.2m,
            MacdCrossAgeDays = 1,
            IsEarlySignal = false,
            MarketRegimeAligned = true,
            MacdFreshnessScore = 10,
            MacdPositionScore = 10,
            MacdMomentumScore = 10,
            TrendStrengthScore = 10,
            VolumeScore = 5,
            MarketRegimeScore = 10,
            StrategyParametersJson = "{}",
            CreatedAtUtc = DateTime.UtcNow,
        };

        return new BacktestTrade(simulated, candidate);
    }
}
