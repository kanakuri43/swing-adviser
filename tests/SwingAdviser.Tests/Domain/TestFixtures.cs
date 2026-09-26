using SwingAdviser.Domain.MarketData;
using SwingAdviser.Domain.Strategy;

namespace SwingAdviser.Tests.Domain;

/// <summary>複数のテストクラスで使う共通のStrategyParameters既定値と合成バー生成ヘルパー。</summary>
internal static class TestFixtures
{
    public static StrategyParameters DefaultStrategyParameters() => new()
    {
        Indicators = new IndicatorParameters
        {
            MacdFastPeriod = 16,
            MacdSlowPeriod = 34,
            MacdSignalPeriod = 9,
            EmaShortPeriod = 20,
            EmaMediumPeriod = 100,
            AtrPeriod = 14,
            VolumeAveragePeriod = 20,
        },
        Gates = new GateParameters
        {
            MacdCrossMaxAgeDays = 3,
            TrendSlopeLookbackDays = 20,
            OverextendedAtrMultiple = 2.0m,
            MarketRegimeSymbol = "1306",
        },
        Scoring = new ScoringParameters
        {
            MacdFreshnessPoints = 20,
            MacdPositionPoints = 15,
            MacdMomentumPoints = 15,
            TrendStrengthPoints = 20,
            VolumePoints = 15,
            MarketRegimePoints = 15,
            TrendStrengthFullScoreAtrMultiple = 1.0m,
            MomentumFullScoreAtrMultiple = 0.3m,
            VolumeRatioZeroScore = 1.0m,
            VolumeRatioFullScore = 2.0m,
            HighConfidenceThreshold = 70,
            MediumConfidenceThreshold = 50,
        },
        Risk = new RiskParameters
        {
            LongStopLossAtrMultiple = 3.0m,
            ShortStopLossAtrMultiple = 2.5m,
            PartialTakeProfitRMultiple = 1.5m,
            PartialTakeProfitRatio = 0.5m,
            TimeStopBusinessDays = 20,
            MarginDueWarningBusinessDays = 5,
        },
        AnalysisWindow = new AnalysisWindowParameters
        {
            BarsToFetch = 250,
            MinimumRequiredBars = 200,
        },
    };

    /// <summary>価格軸をaxisで点対称に反転する（High/Lowも入れ替える）。出来高は不変。</summary>
    public static List<DailyBar> Reflect(IReadOnlyList<DailyBar> bars, string stockCode, decimal axis)
    {
        return bars
            .Select(b => new DailyBar(stockCode, b.TradeDate, axis - b.Open, axis - b.Low, axis - b.High, axis - b.Close, b.Volume))
            .ToList();
    }
}
