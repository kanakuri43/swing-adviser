using System.Globalization;
using SwingAdviser.Domain.MarketData;
using SwingAdviser.Infrastructure.MarketData;

namespace SwingAdviser.Backtest;

/// <summary>銘柄ごとの日足CSVキャッシュ（date,open,high,low,close,volume）。</summary>
public static class BarCache
{
    public static string PathOf(string cacheDirectory, string code) => Path.Combine(cacheDirectory, $"{code}.csv");

    public static void Write(string path, IEnumerable<FetchedDailyBar> bars)
    {
        var lines = bars
            .OrderBy(b => b.TradeDate)
            .Select(b => string.Create(CultureInfo.InvariantCulture,
                $"{b.TradeDate:yyyy-MM-dd},{b.Open},{b.High},{b.Low},{b.Close},{b.Volume}"));
        File.WriteAllLines(path, lines);
    }

    public static List<DailyBar> Read(string path)
    {
        var code = Path.GetFileNameWithoutExtension(path);
        var bars = new List<DailyBar>();
        foreach (var line in File.ReadLines(path))
        {
            var f = line.Split(',');
            bars.Add(new DailyBar(
                code,
                DateOnly.ParseExact(f[0], "yyyy-MM-dd", CultureInfo.InvariantCulture),
                decimal.Parse(f[1], CultureInfo.InvariantCulture),
                decimal.Parse(f[2], CultureInfo.InvariantCulture),
                decimal.Parse(f[3], CultureInfo.InvariantCulture),
                decimal.Parse(f[4], CultureInfo.InvariantCulture),
                long.Parse(f[5], CultureInfo.InvariantCulture)));
        }

        return bars.OrderBy(b => b.TradeDate).ToList();
    }
}
