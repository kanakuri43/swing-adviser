using System.Text.Json;
using SwingAdviser.Domain.Common;
using SwingAdviser.Domain.MarketData;
using SwingAdviser.Domain.Strategy;

namespace SwingAdviser.Domain.Analysis;

/// <summary>
/// 候補化ゲート（MACDトリガー/勢い/トレンド環境/過熱除外）とスコアリングを実装する。
/// 方向ごとの符号（Long:+1/Short:-1）で正規化し、Long/Shortで同じ比較式を使い回す（CLAUDE.md「テクニカル分析」節）。
/// 地合い（1306.T）はゲートではなくスコア専用。
/// </summary>
public sealed class CandidateScanner
{
    private readonly StrategyParameters _parameters;
    private readonly string _strategyParametersJson;

    public CandidateScanner(StrategyParameters parameters)
    {
        _parameters = parameters;
        _strategyParametersJson = JsonSerializer.Serialize(parameters);
    }

    public IReadOnlyList<CandidateEvaluation> Evaluate(
        string stockCode,
        IReadOnlyList<DailyBar> bars,
        IReadOnlyList<DailyBar> marketRegimeBars,
        DateTime nowUtc)
    {
        var indicators = _parameters.Indicators;
        var gates = _parameters.Gates;
        var scoring = _parameters.Scoring;

        if (bars.Count < _parameters.AnalysisWindow.MinimumRequiredBars)
        {
            throw new ArgumentException(
                $"分析には最低{_parameters.AnalysisWindow.MinimumRequiredBars}本のバーが必要です（現在{bars.Count}本）。", nameof(bars));
        }

        if (bars[^1].TradeDate != marketRegimeBars[^1].TradeDate)
        {
            throw new ArgumentException("銘柄と地合い指数の評価日が一致していません。", nameof(marketRegimeBars));
        }

        var todayIndex = bars.Count - 1;
        if (todayIndex - gates.TrendSlopeLookbackDays < 0)
        {
            throw new ArgumentException("トレンド傾き判定に必要な本数が不足しています。", nameof(bars));
        }

        var closes = bars.Select(b => b.Close).ToArray();
        var macd = TechnicalIndicators.Macd(closes, indicators.MacdFastPeriod, indicators.MacdSlowPeriod, indicators.MacdSignalPeriod);
        var ema20 = TechnicalIndicators.Ema(closes, indicators.EmaShortPeriod);
        var ema100 = TechnicalIndicators.Ema(closes, indicators.EmaMediumPeriod);
        var atr = TechnicalIndicators.AtrWilder(bars, indicators.AtrPeriod);
        var volumeRatio = TechnicalIndicators.VolumeRatio(bars, indicators.VolumeAveragePeriod);

        var regimeCloses = marketRegimeBars.Select(b => b.Close).ToArray();
        var regimeMacd = TechnicalIndicators.Macd(regimeCloses, indicators.MacdFastPeriod, indicators.MacdSlowPeriod, indicators.MacdSignalPeriod);
        var regimeTodayIndex = marketRegimeBars.Count - 1;

        var evaluationDate = bars[todayIndex].TradeDate;
        var candidates = new List<CandidateEvaluation>();

        foreach (var direction in new[] { TradeDirection.Long, TradeDirection.Short })
        {
            var sign = direction == TradeDirection.Long ? 1m : -1m;

            var crossAgeDays = FindDirectionalCrossAge(sign, macd.Line, macd.Signal, todayIndex);
            var isConfirmedCross = crossAgeDays < gates.MacdCrossMaxAgeDays;

            var isEarlySignal = false;
            if (!isConfirmedCross)
            {
                var risingStreak = CountRisingStreak(sign, macd.Histogram, todayIndex);
                var gapAtrMultiple = sign * (macd.Signal[todayIndex] - macd.Line[todayIndex]) / atr[todayIndex];
                isEarlySignal = risingStreak >= gates.EarlySignalMinRisingDays
                    && gapAtrMultiple > 0
                    && gapAtrMultiple <= gates.EarlySignalMaxGapAtrMultiple;
            }

            if (!isConfirmedCross && !isEarlySignal)
            {
                continue;
            }

            var momentumDelta = sign * (macd.Histogram[todayIndex] - macd.Histogram[todayIndex - 1]);
            if (momentumDelta <= 0)
            {
                continue;
            }

            var priceVsEma100 = sign * (closes[todayIndex] - ema100[todayIndex]);
            var trendSlope = sign * (ema100[todayIndex] - ema100[todayIndex - gates.TrendSlopeLookbackDays]);
            if (priceVsEma100 <= 0 || trendSlope <= 0)
            {
                continue;
            }

            var extensionAtrMultiple = sign * (closes[todayIndex] - ema20[todayIndex]) / atr[todayIndex];
            if (extensionAtrMultiple > gates.OverextendedAtrMultiple)
            {
                continue;
            }

            var marketRegimeAligned = sign * (regimeMacd.Line[regimeTodayIndex] - regimeMacd.Signal[regimeTodayIndex]) > 0;

            var freshnessScore = isConfirmedCross
                ? ScoreLinear(
                    scoring.MacdFreshnessPoints,
                    1m - ((decimal)crossAgeDays / (gates.MacdCrossMaxAgeDays - 1)))
                : (int)Math.Round(scoring.MacdFreshnessPoints * scoring.EarlySignalFreshnessScoreCapRatio, MidpointRounding.AwayFromZero);

            int positionScore;
            if (isConfirmedCross)
            {
                var crossDayIndex = todayIndex - crossAgeDays;
                positionScore = sign * macd.Line[crossDayIndex] > 0 ? scoring.MacdPositionPoints : 0;
            }
            else
            {
                positionScore = sign * macd.Line[todayIndex] > 0 ? scoring.MacdPositionPoints : 0;
            }

            var momentumScore = ScoreLinear(
                scoring.MacdMomentumPoints,
                momentumDelta / atr[todayIndex] / scoring.MomentumFullScoreAtrMultiple);

            var trendScore = ScoreLinear(
                scoring.TrendStrengthPoints,
                trendSlope / atr[todayIndex] / scoring.TrendStrengthFullScoreAtrMultiple);

            var volumeScore = ScoreLinear(
                scoring.VolumePoints,
                (volumeRatio[todayIndex] - scoring.VolumeRatioZeroScore) / (scoring.VolumeRatioFullScore - scoring.VolumeRatioZeroScore));

            var marketRegimeScore = marketRegimeAligned ? scoring.MarketRegimePoints : 0;

            var totalScore = freshnessScore + positionScore + momentumScore + trendScore + volumeScore + marketRegimeScore;
            var confidence = totalScore >= scoring.HighConfidenceThreshold
                ? ConfidenceLevel.High
                : totalScore >= scoring.MediumConfidenceThreshold
                    ? ConfidenceLevel.Medium
                    : ConfidenceLevel.Low;

            candidates.Add(new CandidateEvaluation
            {
                EvaluationDate = evaluationDate,
                StockCode = stockCode,
                Direction = direction,
                Score = totalScore,
                Confidence = confidence,
                Close = closes[todayIndex],
                MacdLine = macd.Line[todayIndex],
                MacdSignal = macd.Signal[todayIndex],
                MacdHistogram = macd.Histogram[todayIndex],
                PreviousMacdHistogram = macd.Histogram[todayIndex - 1],
                Ema20 = ema20[todayIndex],
                Ema100 = ema100[todayIndex],
                Ema100TwentyDaysAgo = ema100[todayIndex - gates.TrendSlopeLookbackDays],
                Atr14 = atr[todayIndex],
                VolumeRatio = volumeRatio[todayIndex],
                MacdCrossAgeDays = isConfirmedCross ? crossAgeDays : null,
                IsEarlySignal = isEarlySignal,
                MarketRegimeAligned = marketRegimeAligned,
                MacdFreshnessScore = freshnessScore,
                MacdPositionScore = positionScore,
                MacdMomentumScore = momentumScore,
                TrendStrengthScore = trendScore,
                VolumeScore = volumeScore,
                MarketRegimeScore = marketRegimeScore,
                StrategyParametersJson = _strategyParametersJson,
                CreatedAtUtc = nowUtc,
            });
        }

        return candidates;
    }

    /// <summary>
    /// 今日の方向的MACD位置（sign*(Line-Signal)）が不利なら int.MaxValue（ゲートで自然に落ちる）。
    /// 有利なら、直前日も有利な間だけ日数を遡ってカウントする（クロス当日=0）。
    /// </summary>
    private static int FindDirectionalCrossAge(decimal sign, decimal[] macdLine, decimal[] signal, int todayIndex)
    {
        if (sign * (macdLine[todayIndex] - signal[todayIndex]) <= 0)
        {
            return int.MaxValue;
        }

        var age = 0;
        var i = todayIndex;
        while (i - 1 >= 0 && sign * (macdLine[i - 1] - signal[i - 1]) > 0)
        {
            age++;
            i--;
        }

        return age;
    }

    /// <summary>ヒストグラムがsign方向に連続拡大している日数（当日を含む）を、拡大が途切れるまで遡って数える。</summary>
    private static int CountRisingStreak(decimal sign, decimal[] histogram, int todayIndex)
    {
        var streak = 0;
        var i = todayIndex;
        while (i - 1 >= 0 && sign * (histogram[i] - histogram[i - 1]) > 0)
        {
            streak++;
            i--;
        }

        return streak;
    }

    private static int ScoreLinear(int maxPoints, decimal ratio) =>
        (int)Math.Round(maxPoints * Math.Clamp(ratio, 0m, 1m), MidpointRounding.AwayFromZero);
}
