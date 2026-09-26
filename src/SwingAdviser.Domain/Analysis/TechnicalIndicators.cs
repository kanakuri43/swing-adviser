using SwingAdviser.Domain.MarketData;

namespace SwingAdviser.Domain.Analysis;

/// <summary>
/// MACD/EMA/ATR/出来高倍率の計算式のみを提供する純粋関数群（候補化ゲート・スコアリングは<see cref="CandidateScanner"/>）。
/// すべて過去から現在への一方向の反復だけで計算するため、末尾に将来のバーを追加しても既存の値は変わらない。
/// </summary>
public static class TechnicalIndicators
{
    /// <summary>
    /// 先頭 period-1 件は0（未定義、呼び出し側は参照しない前提）。period-1件目をSMAで種付けし、以後 k=2/(period+1) の指数移動平均。
    /// </summary>
    public static decimal[] Ema(IReadOnlyList<decimal> values, int period)
    {
        if (period <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(period));
        }

        if (values.Count < period)
        {
            throw new ArgumentException($"少なくとも{period}件必要です（現在{values.Count}件）。", nameof(values));
        }

        var result = new decimal[values.Count];

        var seed = 0m;
        for (var i = 0; i < period; i++)
        {
            seed += values[i];
        }

        result[period - 1] = seed / period;

        var k = 2m / (period + 1);
        for (var i = period; i < values.Count; i++)
        {
            result[i] = ((values[i] - result[i - 1]) * k) + result[i - 1];
        }

        return result;
    }

    /// <summary>
    /// Line = EMAfast - EMAslow（slow-1件目から有効）。Signalはslow-1件目以降の有効区間だけを切り出してEMAを取り、
    /// 未定義区間の0がシグナルの種（SMA）に混入しないようにする。
    /// </summary>
    public static MacdSeries Macd(IReadOnlyList<decimal> closes, int fastPeriod, int slowPeriod, int signalPeriod)
    {
        if (fastPeriod >= slowPeriod)
        {
            throw new ArgumentException("fastPeriod は slowPeriod 未満である必要があります。", nameof(fastPeriod));
        }

        var fast = Ema(closes, fastPeriod);
        var slow = Ema(closes, slowPeriod);

        var line = new decimal[closes.Count];
        for (var i = slowPeriod - 1; i < closes.Count; i++)
        {
            line[i] = fast[i] - slow[i];
        }

        var validLineStart = slowPeriod - 1;
        var validLine = line.Skip(validLineStart).ToArray();
        var validSignal = Ema(validLine, signalPeriod);

        var signal = new decimal[closes.Count];
        var histogram = new decimal[closes.Count];
        for (var i = signalPeriod - 1; i < validSignal.Length; i++)
        {
            var fullIndex = i + validLineStart;
            signal[fullIndex] = validSignal[i];
            histogram[fullIndex] = line[fullIndex] - validSignal[i];
        }

        return new MacdSeries(line, signal, histogram);
    }

    /// <summary>
    /// Wilder方式。TRはindex1から定義され（index0は前日終値が無い）、period件目にSMAで種付けし、
    /// 以後 (prev*(period-1)+TR)/period で平滑化する。
    /// </summary>
    public static decimal[] AtrWilder(IReadOnlyList<DailyBar> bars, int period)
    {
        if (period <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(period));
        }

        if (bars.Count < period + 1)
        {
            throw new ArgumentException($"少なくとも{period + 1}本必要です（現在{bars.Count}本）。", nameof(bars));
        }

        var trueRanges = new decimal[bars.Count];
        for (var i = 1; i < bars.Count; i++)
        {
            var high = bars[i].High;
            var low = bars[i].Low;
            var previousClose = bars[i - 1].Close;
            trueRanges[i] = Math.Max(high - low, Math.Max(Math.Abs(high - previousClose), Math.Abs(low - previousClose)));
        }

        var atr = new decimal[bars.Count];

        var seed = 0m;
        for (var i = 1; i <= period; i++)
        {
            seed += trueRanges[i];
        }

        atr[period] = seed / period;

        for (var i = period + 1; i < bars.Count; i++)
        {
            atr[i] = ((atr[i - 1] * (period - 1)) + trueRanges[i]) / period;
        }

        return atr;
    }

    /// <summary>当日を含まない直近averagePeriod日平均出来高に対する、当日出来高の倍率。</summary>
    public static decimal[] VolumeRatio(IReadOnlyList<DailyBar> bars, int averagePeriod)
    {
        if (averagePeriod <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(averagePeriod));
        }

        if (bars.Count < averagePeriod + 1)
        {
            throw new ArgumentException($"少なくとも{averagePeriod + 1}本必要です（現在{bars.Count}本）。", nameof(bars));
        }

        var ratios = new decimal[bars.Count];
        for (var i = averagePeriod; i < bars.Count; i++)
        {
            long sum = 0;
            for (var j = i - averagePeriod; j < i; j++)
            {
                sum += bars[j].Volume;
            }

            var average = sum / (decimal)averagePeriod;
            ratios[i] = average == 0 ? 0m : bars[i].Volume / average;
        }

        return ratios;
    }
}
