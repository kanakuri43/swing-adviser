using SwingAdviser.Domain.Analysis;
using SwingAdviser.Domain.Common;
using SwingAdviser.Domain.MarketData;
using SwingAdviser.Domain.Strategy;

namespace SwingAdviser.Tests.Domain;

public class CandidateScannerTests
{
    private static readonly DateTime NowUtc = new(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);

    private static StrategyParameters TestStrategyParameters() => TestFixtures.DefaultStrategyParameters();

    private static List<DailyBar> BuildTrendWithPullbackAndRecovery(string stockCode) =>
        TestFixtures.BuildTrendWithPullbackAndRecovery(stockCode);

    private static int FindFreshGoldenCrossIndex(IReadOnlyList<DailyBar> bars, StrategyParameters parameters) =>
        TestFixtures.FindFreshGoldenCrossIndex(bars, parameters);

    [Fact]
    public void Evaluate_FreshGoldenCrossInUptrend_ReturnsLongCandidateWithConsistentScore()
    {
        var parameters = TestStrategyParameters();
        var stockBars = BuildTrendWithPullbackAndRecovery("7203");
        var regimeBars = BuildTrendWithPullbackAndRecovery("1306");

        var crossIndex = FindFreshGoldenCrossIndex(stockBars, parameters);
        var bars = stockBars.Take(crossIndex + 1).ToList();
        var regime = regimeBars.Take(crossIndex + 1).ToList();

        var scanner = new CandidateScanner(parameters);
        var results = scanner.Evaluate("7203", bars, regime, NowUtc);

        var candidate = Assert.Single(results);
        Assert.Equal(TradeDirection.Long, candidate.Direction);
        Assert.Equal(0, candidate.MacdCrossAgeDays);
        Assert.False(candidate.IsEarlySignal);
        Assert.Equal(
            candidate.MacdFreshnessScore + candidate.MacdPositionScore + candidate.MacdMomentumScore
            + candidate.TrendStrengthScore + candidate.VolumeScore + candidate.MarketRegimeScore,
            candidate.Score);
        Assert.InRange(candidate.Score, 0, 100);
    }

    [Fact]
    public void Evaluate_ReflectedPriceAxis_ReturnsShortCandidateWithIdenticalScoreComponents()
    {
        var parameters = TestStrategyParameters();
        var stockBars = BuildTrendWithPullbackAndRecovery("7203");
        var regimeBars = BuildTrendWithPullbackAndRecovery("1306");
        var crossIndex = FindFreshGoldenCrossIndex(stockBars, parameters);

        var longBars = stockBars.Take(crossIndex + 1).ToList();
        var longRegime = regimeBars.Take(crossIndex + 1).ToList();

        const decimal axis = 100_000m;
        var shortBars = TestFixtures.Reflect(longBars, "7203", axis);
        var shortRegime = TestFixtures.Reflect(longRegime, "1306", axis);

        var scanner = new CandidateScanner(parameters);
        var longCandidate = Assert.Single(scanner.Evaluate("7203", longBars, longRegime, NowUtc));
        var shortCandidate = Assert.Single(scanner.Evaluate("7203", shortBars, shortRegime, NowUtc));

        Assert.Equal(TradeDirection.Short, shortCandidate.Direction);
        Assert.Equal(longCandidate.MacdCrossAgeDays, shortCandidate.MacdCrossAgeDays);
        Assert.Equal(longCandidate.MarketRegimeAligned, shortCandidate.MarketRegimeAligned);
        Assert.Equal(longCandidate.Atr14, shortCandidate.Atr14);
        Assert.Equal(longCandidate.VolumeRatio, shortCandidate.VolumeRatio);
        Assert.Equal(longCandidate.MacdFreshnessScore, shortCandidate.MacdFreshnessScore);
        Assert.Equal(longCandidate.MacdPositionScore, shortCandidate.MacdPositionScore);
        Assert.Equal(longCandidate.MacdMomentumScore, shortCandidate.MacdMomentumScore);
        Assert.Equal(longCandidate.TrendStrengthScore, shortCandidate.TrendStrengthScore);
        Assert.Equal(longCandidate.VolumeScore, shortCandidate.VolumeScore);
        Assert.Equal(longCandidate.MarketRegimeScore, shortCandidate.MarketRegimeScore);
        Assert.Equal(longCandidate.Score, shortCandidate.Score);
        Assert.Equal(longCandidate.Confidence, shortCandidate.Confidence);
    }

    [Fact]
    public void Evaluate_Overextended_RejectsCandidate()
    {
        var parameters = TestStrategyParameters();
        var stockBars = BuildTrendWithPullbackAndRecovery("7203");
        var regimeBars = BuildTrendWithPullbackAndRecovery("1306");
        var crossIndex = FindFreshGoldenCrossIndex(stockBars, parameters);

        var bars = stockBars.Take(crossIndex + 1).ToList();
        var regime = regimeBars.Take(crossIndex + 1).ToList();

        // 最終日だけ大きく跳ねさせ、EMA20から2.0ATRを超えて乖離させる。
        var last = bars[^1];
        var jumped = new DailyBar(last.StockCode, last.TradeDate, last.Open + 200m, last.High + 200m, last.Low + 200m, last.Close + 200m, 200_000L);
        bars[^1] = jumped;

        var scanner = new CandidateScanner(parameters);
        var results = scanner.Evaluate("7203", bars, regime, NowUtc);

        Assert.DoesNotContain(results, c => c.Direction == TradeDirection.Long);
    }

    [Fact]
    public void Evaluate_StaleCross_RejectsCandidate()
    {
        var parameters = TestStrategyParameters();
        var stockBars = BuildTrendWithPullbackAndRecovery("7203");
        var regimeBars = BuildTrendWithPullbackAndRecovery("1306");
        var crossIndex = FindFreshGoldenCrossIndex(stockBars, parameters);

        // クロスの4営業日後を「今日」にする（3営業日ルールを超過）。
        var todayIndex = crossIndex + 4;
        var bars = stockBars.Take(todayIndex + 1).ToList();
        var regime = regimeBars.Take(todayIndex + 1).ToList();

        var scanner = new CandidateScanner(parameters);
        var results = scanner.Evaluate("7203", bars, regime, NowUtc);

        Assert.DoesNotContain(results, c => c.Direction == TradeDirection.Long);
    }

    [Fact]
    public void Evaluate_DowntrendEnvironmentBlocksCounterTrendBounce()
    {
        var parameters = TestStrategyParameters();

        // 260日ずっと下降トレンド（EMA100は明確に下向き）の中で、直近だけ強く反発させてMACDをゴールデンクロスさせる。
        var bars = new List<DailyBar>();
        var regimeBars = new List<DailyBar>();
        var date = new DateOnly(2024, 1, 1);
        var price = 3000m;
        for (var i = 0; i < 260; i++)
        {
            var step = i < 252 ? -3m : 30m; // 直近8日だけ急反発させてMACDを一時的に上向きにする
            price += step;
            var close = price;
            var open = price - (step / 2m);
            var high = Math.Max(open, close) + 3m;
            var low = Math.Min(open, close) - 3m;
            bars.Add(new DailyBar("9999", date.AddDays(i), open, high, low, close, 200_000L));
            regimeBars.Add(new DailyBar("1306", date.AddDays(i), 100m, 103m, 97m, 100m, 200_000L)); // 地合いは横ばい（ゲート対象外なので無関係）
        }

        var scanner = new CandidateScanner(parameters);
        var results = scanner.Evaluate("9999", bars, regimeBars, NowUtc);

        Assert.DoesNotContain(results, c => c.Direction == TradeDirection.Long);
    }

    [Fact]
    public void Evaluate_MarketRegimeMisaligned_StillProducesCandidateWithZeroRegimeScore()
    {
        var parameters = TestStrategyParameters();
        var stockBars = BuildTrendWithPullbackAndRecovery("7203");
        var crossIndex = FindFreshGoldenCrossIndex(stockBars, parameters);
        var bars = stockBars.Take(crossIndex + 1).ToList();

        // 地合いは終値がほぼ下落し続ける別系列にする（MACDが逆方向）。
        var regimeBars = new List<DailyBar>();
        var date = new DateOnly(2024, 1, 1);
        var price = 2000m;
        for (var i = 0; i <= crossIndex; i++)
        {
            price -= 3m;
            regimeBars.Add(new DailyBar("1306", date.AddDays(i), price + 1.5m, price + 3m, price - 3m, price, 200_000L));
        }

        var scanner = new CandidateScanner(parameters);
        var candidate = Assert.Single(scanner.Evaluate("7203", bars, regimeBars, NowUtc));

        Assert.False(candidate.MarketRegimeAligned);
        Assert.Equal(0, candidate.MarketRegimeScore);
    }

    [Fact]
    public void Evaluate_EarlySignalBeforeCross_ReturnsCandidateWithCappedFreshnessScore()
    {
        var parameters = TestStrategyParameters();
        var stockBars = BuildTrendWithPullbackAndRecovery("7203");
        var regimeBars = BuildTrendWithPullbackAndRecovery("1306");
        var crossIndex = FindFreshGoldenCrossIndex(stockBars, parameters);

        // クロスの4営業日前（まだ未クロス。ヒストグラムは底から連続拡大中、乖離幅0.39ATR）。
        var todayIndex = crossIndex - 4;
        var bars = stockBars.Take(todayIndex + 1).ToList();
        var regime = regimeBars.Take(todayIndex + 1).ToList();

        var scanner = new CandidateScanner(parameters);
        var candidate = Assert.Single(scanner.Evaluate("7203", bars, regime, NowUtc));

        Assert.Equal(TradeDirection.Long, candidate.Direction);
        Assert.True(candidate.IsEarlySignal);
        Assert.Null(candidate.MacdCrossAgeDays);
        Assert.Equal(12, candidate.MacdFreshnessScore); // 20 * 0.6（EarlySignalFreshnessScoreCapRatio）
        Assert.Equal(
            candidate.MacdFreshnessScore + candidate.MacdPositionScore + candidate.MacdMomentumScore
            + candidate.TrendStrengthScore + candidate.VolumeScore + candidate.MarketRegimeScore,
            candidate.Score);
    }

    [Fact]
    public void Evaluate_EarlySignalReflectedPriceAxis_ReturnsShortCandidateWithIdenticalScoreComponents()
    {
        var parameters = TestStrategyParameters();
        var stockBars = BuildTrendWithPullbackAndRecovery("7203");
        var regimeBars = BuildTrendWithPullbackAndRecovery("1306");
        var crossIndex = FindFreshGoldenCrossIndex(stockBars, parameters);

        var todayIndex = crossIndex - 4;
        var longBars = stockBars.Take(todayIndex + 1).ToList();
        var longRegime = regimeBars.Take(todayIndex + 1).ToList();

        const decimal axis = 100_000m;
        var shortBars = TestFixtures.Reflect(longBars, "7203", axis);
        var shortRegime = TestFixtures.Reflect(longRegime, "1306", axis);

        var scanner = new CandidateScanner(parameters);
        var longCandidate = Assert.Single(scanner.Evaluate("7203", longBars, longRegime, NowUtc));
        var shortCandidate = Assert.Single(scanner.Evaluate("7203", shortBars, shortRegime, NowUtc));

        Assert.True(longCandidate.IsEarlySignal);
        Assert.Equal(TradeDirection.Short, shortCandidate.Direction);
        Assert.Equal(longCandidate.IsEarlySignal, shortCandidate.IsEarlySignal);
        Assert.Equal(longCandidate.MacdCrossAgeDays, shortCandidate.MacdCrossAgeDays);
        Assert.Equal(longCandidate.Score, shortCandidate.Score);
    }

    [Fact]
    public void Evaluate_EarlySignalGapTooLarge_RejectsCandidate()
    {
        var parameters = TestStrategyParameters();
        var stockBars = BuildTrendWithPullbackAndRecovery("7203");
        var regimeBars = BuildTrendWithPullbackAndRecovery("1306");
        var crossIndex = FindFreshGoldenCrossIndex(stockBars, parameters);

        // クロスの5営業日前（乖離幅0.51ATRでEarlySignalMaxGapAtrMultiple=0.5を超える）。
        var todayIndex = crossIndex - 5;
        var bars = stockBars.Take(todayIndex + 1).ToList();
        var regime = regimeBars.Take(todayIndex + 1).ToList();

        var scanner = new CandidateScanner(parameters);
        var results = scanner.Evaluate("7203", bars, regime, NowUtc);

        Assert.DoesNotContain(results, c => c.Direction == TradeDirection.Long);
    }

    [Fact]
    public void Evaluate_EarlySignalInsufficientRisingStreak_RejectsCandidateUnlessThresholdLowered()
    {
        var parameters = TestStrategyParameters();
        var stockBars = BuildTrendWithPullbackAndRecovery("7203");
        var regimeBars = BuildTrendWithPullbackAndRecovery("1306");
        var crossIndex = FindFreshGoldenCrossIndex(stockBars, parameters);

        var todayIndex = crossIndex - 4;
        var bars = stockBars.Take(todayIndex + 1).ToList();
        var regime = regimeBars.Take(todayIndex + 1).ToList();

        // 直近2日を「下げ→下げ」にしてから当日を強い上げ足にする。当日は前日比拡大するが、
        // 連続拡大日数は1日しかない（前日は前々日比で縮小している）。
        var twoDaysAgo = bars[^3];
        var yesterday = new DailyBar(
            "7203", bars[^2].TradeDate, twoDaysAgo.Close - 20m, twoDaysAgo.Close - 15m, twoDaysAgo.Close - 45m, twoDaysAgo.Close - 40m, 200_000L);
        var today = new DailyBar(
            "7203", bars[^1].TradeDate, yesterday.Close, yesterday.Close + 60m, yesterday.Close - 2m, yesterday.Close + 55m, 200_000L);
        bars[^2] = yesterday;
        bars[^1] = today;

        var defaultScanner = new CandidateScanner(parameters);
        Assert.DoesNotContain(defaultScanner.Evaluate("7203", bars, regime, NowUtc), c => c.Direction == TradeDirection.Long);

        var loosenedParameters = new StrategyParameters
        {
            Indicators = parameters.Indicators,
            Gates = new GateParameters
            {
                MacdCrossMaxAgeDays = parameters.Gates.MacdCrossMaxAgeDays,
                TrendSlopeLookbackDays = parameters.Gates.TrendSlopeLookbackDays,
                OverextendedAtrMultiple = parameters.Gates.OverextendedAtrMultiple,
                MarketRegimeSymbol = parameters.Gates.MarketRegimeSymbol,
                EarlySignalMinRisingDays = 1,
                EarlySignalMaxGapAtrMultiple = parameters.Gates.EarlySignalMaxGapAtrMultiple,
            },
            Scoring = parameters.Scoring,
            Risk = parameters.Risk,
            AnalysisWindow = parameters.AnalysisWindow,
        };
        var loosenedScanner = new CandidateScanner(loosenedParameters);
        var candidate = Assert.Single(loosenedScanner.Evaluate("7203", bars, regime, NowUtc));
        Assert.Equal(TradeDirection.Long, candidate.Direction);
        Assert.True(candidate.IsEarlySignal);
    }

    [Fact]
    public void Evaluate_InsufficientBars_Throws()
    {
        var parameters = TestStrategyParameters();
        var bars = BuildTrendWithPullbackAndRecovery("7203").Take(100).ToList();
        var regime = BuildTrendWithPullbackAndRecovery("1306").Take(100).ToList();

        var scanner = new CandidateScanner(parameters);

        Assert.Throws<ArgumentException>(() => scanner.Evaluate("7203", bars, regime, NowUtc));
    }
}
