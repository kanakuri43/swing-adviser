using SwingAdviser.Domain.Analysis;
using SwingAdviser.Domain.MarketData;
using SwingAdviser.Domain.Strategy;

namespace SwingAdviser.Tests.Domain;

/// <summary>複数のテストクラスで使う共通のStrategyParameters既定値と合成バー生成ヘルパー。</summary>
public static class TestFixtures
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
            EarlySignalMinRisingDays = 2,
            EarlySignalMaxGapAtrMultiple = 0.5m,
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
            EarlySignalFreshnessScoreCapRatio = 0.6m,
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

    /// <summary>緩やかな上昇トレンド→押し目→反発のV字を作る。反発の途中でMACDのゴールデンクロスが起きる想定。</summary>
    public static List<DailyBar> BuildTrendWithPullbackAndRecovery(string stockCode, DateOnly? startDate = null)
    {
        var bars = new List<DailyBar>();
        var date = startDate ?? new DateOnly(2024, 1, 1);
        var price = 1000m;
        const int totalDays = 260;
        const int recoveryDays = 25;
        const int pullbackDays = 20;
        var pullbackStart = totalDays - recoveryDays - pullbackDays;

        for (var i = 0; i < totalDays; i++)
        {
            decimal step = i < pullbackStart ? 3m
                : i < pullbackStart + pullbackDays ? -3m
                : 4m;

            price += step;
            var close = price;
            var open = price - (step / 2m);
            var high = Math.Max(open, close) + 3m;
            var low = Math.Min(open, close) - 3m;

            bars.Add(new DailyBar(stockCode, date.AddDays(i), open, high, low, close, 200_000L));
        }

        return bars;
    }

    /// <summary>直近側から遡って最初に見つかるゴールデンクロス（当日）のインデックスを返す。</summary>
    public static int FindFreshGoldenCrossIndex(IReadOnlyList<DailyBar> bars, StrategyParameters parameters)
    {
        var closes = bars.Select(b => b.Close).ToArray();
        var macd = TechnicalIndicators.Macd(closes, parameters.Indicators.MacdFastPeriod, parameters.Indicators.MacdSlowPeriod, parameters.Indicators.MacdSignalPeriod);

        for (var i = closes.Length - 1; i >= parameters.Indicators.MacdSlowPeriod; i--)
        {
            if (macd.Line[i] > macd.Signal[i] && macd.Line[i - 1] <= macd.Signal[i - 1])
            {
                return i;
            }
        }

        throw new InvalidOperationException("合成データにゴールデンクロスが見つかりませんでした。BuildTrendWithPullbackAndRecoveryの調整が必要です。");
    }
}
