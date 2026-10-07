using SwingAdviser.Infrastructure.MarketData;

namespace SwingAdviser.Backtest;

/// <summary>
/// Yahoo の日足の不備を、キャッシュに書く前に補正する。実データで確認した不備:
/// 分割イベントがあるのに過去の価格が未調整（7946 など）、分割イベント自体が欠けた段差（1306 など）、
/// 出来高0で価格が桁違いの不良バー（8303 など）。
/// 日本株の値幅制限上、1日で終値が3倍以上/1/3以下になることは実質無いため、その段差は分割・併合とみなす。
/// </summary>
public static class BarCleaner
{
    /// <summary>終値の前日比がこの範囲を外れたら、値動きではなく分割・併合の段差とみなす。</summary>
    private const decimal JumpThreshold = 3m;

    public static List<FetchedDailyBar> Clean(IReadOnlyList<FetchedDailyBar> bars, IReadOnlyList<YahooSplitEvent> splits)
    {
        var cleaned = DropZeroVolumeJumpBars(bars.OrderBy(b => b.TradeDate).ToList());

        // 1) Yahoo の分割イベント。効力発生日をまたぐ終値の段差が分割比に近ければ、過去側が未調整。
        foreach (var split in splits.Where(s => s.Ratio != 1m && s.Ratio > 0m).OrderByDescending(s => s.EffectiveDate))
        {
            var index = cleaned.FindIndex(b => b.TradeDate >= split.EffectiveDate);
            if (index <= 0)
            {
                continue;
            }

            var gap = (double)(cleaned[index].Close / cleaned[index - 1].Close);
            var ratio = (double)split.Ratio;
            if (Math.Abs(Math.Log(gap * ratio)) < Math.Abs(Math.Log(gap)))
            {
                AdjustBefore(cleaned, index, split.Ratio);
            }
        }

        // 2) イベントが無い大きな段差。比率は段差から推定する（例: 382.7→37.64 は 1:10 分割）。
        for (var i = cleaned.Count - 1; i >= 1; i--)
        {
            var gap = cleaned[i].Close / cleaned[i - 1].Close;
            if (gap < 1m / JumpThreshold)
            {
                AdjustBefore(cleaned, i, Math.Max(1m, Math.Round(1m / gap)));
            }
            else if (gap > JumpThreshold)
            {
                AdjustBefore(cleaned, i, 1m / Math.Max(1m, Math.Round(gap)));
            }
        }

        return cleaned;
    }

    /// <summary>出来高0で、直前の（残した）バーの終値から3倍超乖離したバーは不良データとして除く。</summary>
    private static List<FetchedDailyBar> DropZeroVolumeJumpBars(List<FetchedDailyBar> bars)
    {
        var kept = new List<FetchedDailyBar>(bars.Count);
        foreach (var bar in bars)
        {
            if (bar.Volume == 0 && kept.Count > 0)
            {
                var gap = bar.Close / kept[^1].Close;
                if (gap > JumpThreshold || gap < 1m / JumpThreshold)
                {
                    continue;
                }
            }

            kept.Add(bar);
        }

        return kept;
    }

    /// <summary>ratio=2 は1:2分割。index より前のバーの価格を ratio で割り、出来高を掛ける。</summary>
    private static void AdjustBefore(List<FetchedDailyBar> bars, int index, decimal ratio)
    {
        for (var i = 0; i < index; i++)
        {
            var b = bars[i];
            bars[i] = new FetchedDailyBar(
                b.TradeDate, b.Open / ratio, b.High / ratio, b.Low / ratio, b.Close / ratio,
                (long)Math.Round(b.Volume * ratio, MidpointRounding.AwayFromZero));
        }
    }
}
