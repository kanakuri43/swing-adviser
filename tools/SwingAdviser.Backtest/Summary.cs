using System.Globalization;

namespace SwingAdviser.Backtest;

public sealed record GroupStats(
    string Group, string Label, int Count, double WinRate, double AverageR, double MedianR, double ProfitFactor, double MaxDrawdownR);

public static class Summary
{
    public static GroupStats Compute(string group, string label, IReadOnlyList<BacktestTrade> trades)
    {
        if (trades.Count == 0)
        {
            return new GroupStats(group, label, 0, 0, 0, 0, 0, 0);
        }

        var results = trades.Select(t => (double)t.Trade.ResultR).ToList();
        var sorted = results.OrderBy(r => r).ToList();
        var median = sorted.Count % 2 == 1
            ? sorted[sorted.Count / 2]
            : (sorted[(sorted.Count / 2) - 1] + sorted[sorted.Count / 2]) / 2;

        var gains = results.Where(r => r > 0).Sum();
        var losses = -results.Where(r => r < 0).Sum();

        return new GroupStats(
            group, label, trades.Count,
            results.Count(r => r > 0) / (double)results.Count,
            results.Average(),
            median,
            losses == 0 ? double.PositiveInfinity : gains / losses,
            MaxDrawdown(trades));
    }

    /// <summary>決済日順に並べた累積Rの、ピークからの最大下落幅（R）。</summary>
    public static double MaxDrawdown(IReadOnlyList<BacktestTrade> trades)
    {
        double cumulative = 0, peak = 0, maxDrawdown = 0;
        foreach (var trade in trades.OrderBy(t => t.Trade.ExitDate).ThenBy(t => t.Trade.StockCode, StringComparer.Ordinal))
        {
            cumulative += (double)trade.Trade.ResultR;
            peak = Math.Max(peak, cumulative);
            maxDrawdown = Math.Max(maxDrawdown, peak - cumulative);
        }

        return maxDrawdown;
    }

    /// <summary>切り口（方向・確定/早期・スコア帯など）ごとの集計を作る。</summary>
    public static IReadOnlyList<GroupStats> Breakdown(IReadOnlyList<BacktestTrade> trades, DateOnly splitDate)
    {
        var slices = new (string Name, Func<BacktestTrade, string> Key)[]
        {
            ("方向", t => t.Trade.Direction.ToString()),
            ("MACD状態", t => t.Candidate.IsEarlySignal ? "早期(未確定)" : "確定"),
            ("方向×MACD状態", t => $"{t.Trade.Direction}/{(t.Candidate.IsEarlySignal ? "早期" : "確定")}"),
            ("地合い", t => t.Candidate.MarketRegimeAligned ? "一致" : "不一致"),
            ("方向×地合い", t => $"{t.Trade.Direction}/{(t.Candidate.MarketRegimeAligned ? "一致" : "不一致")}"),
            ("信頼度", t => t.Candidate.Confidence.ToString()),
            ("スコア帯", t => $"{t.Candidate.Score / 10 * 10:000}-{(t.Candidate.Score / 10 * 10) + 9:000}"),
            ("決済理由", t => t.Trade.ExitReason.ToString()),
            ("部分利確", t => t.Trade.PartialTakeProfitDone ? "あり" : "なし"),
            ("シグナル年", t => t.Trade.SignalDate.Year.ToString(CultureInfo.InvariantCulture)),
            ("期間", t => t.Trade.SignalDate < splitDate ? $"前半(<{splitDate:yyyy-MM-dd})" : $"後半(>={splitDate:yyyy-MM-dd})"),
        };

        var rows = new List<GroupStats> { Compute("全体", "全体", trades) };
        foreach (var (name, key) in slices)
        {
            foreach (var group in trades.GroupBy(key).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                rows.Add(Compute(name, group.Key, group.ToList()));
            }
        }

        return rows;
    }

    public static string FormatRow(GroupStats s) =>
        string.Create(CultureInfo.InvariantCulture,
            $"{s.Group,-12} {s.Label,-18} n={s.Count,6}  勝率={s.WinRate,6:P1}  平均R={s.AverageR,7:F3}  中央値R={s.MedianR,7:F3}  PF={Pf(s.ProfitFactor),6}  最大DD={s.MaxDrawdownR,8:F1}R");

    private static string Pf(double pf) => double.IsInfinity(pf) ? "-" : pf.ToString("F2", CultureInfo.InvariantCulture);
}
