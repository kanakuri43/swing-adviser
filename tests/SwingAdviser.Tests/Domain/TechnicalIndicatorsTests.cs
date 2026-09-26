using SwingAdviser.Domain.Analysis;
using SwingAdviser.Domain.MarketData;

namespace SwingAdviser.Tests.Domain;

public class TechnicalIndicatorsTests
{
    [Fact]
    public void Ema_MatchesHandComputedSeedAndRecursion()
    {
        decimal[] values = [1m, 2m, 3m, 4m, 5m];

        var result = TechnicalIndicators.Ema(values, period: 3);

        Assert.Equal(0m, result[0]);
        Assert.Equal(0m, result[1]);
        Assert.Equal(2m, result[2]); // seed = avg(1,2,3)
        Assert.Equal(3m, result[3]); // (4-2)*0.5+2
        Assert.Equal(4m, result[4]); // (5-3)*0.5+3
    }

    [Fact]
    public void Macd_SignalSeed_ExcludesUndefinedLeadingValues()
    {
        decimal[] closes = [10m, 11m, 9m, 12m, 13m, 11m];

        var result = TechnicalIndicators.Macd(closes, fastPeriod: 2, slowPeriod: 3, signalPeriod: 2);

        // 独立に展開した手計算（EMA2/EMA3を1ステップずつ再現）と突合する。
        var k2 = 2m / 3m;
        var ema2At2 = ((closes[2] - ((closes[0] + closes[1]) / 2m)) * k2) + ((closes[0] + closes[1]) / 2m);
        var ema2At3 = ((closes[3] - ema2At2) * k2) + ema2At2;
        var ema2At4 = ((closes[4] - ema2At3) * k2) + ema2At3;
        var ema2At5 = ((closes[5] - ema2At4) * k2) + ema2At4;

        var k3 = 1m / 2m;
        var ema3At2 = (closes[0] + closes[1] + closes[2]) / 3m;
        var ema3At3 = ((closes[3] - ema3At2) * k3) + ema3At2;
        var ema3At4 = ((closes[4] - ema3At3) * k3) + ema3At3;
        var ema3At5 = ((closes[5] - ema3At4) * k3) + ema3At4;

        var lineAt2 = ema2At2 - ema3At2;
        var lineAt3 = ema2At3 - ema3At3;
        var lineAt4 = ema2At4 - ema3At4;
        var lineAt5 = ema2At5 - ema3At5;

        // シグナルの種は「有効区間の最初の2件」(line[2],line[3])の平均。未定義区間(line[0],line[1]=0)は含まない。
        var signalAt3 = (lineAt2 + lineAt3) / 2m;
        var signalAt4 = ((lineAt4 - signalAt3) * k2) + signalAt3;
        var signalAt5 = ((lineAt5 - signalAt4) * k2) + signalAt4;

        Assert.Equal(lineAt2, result.Line[2]);
        Assert.Equal(lineAt3, result.Line[3]);
        Assert.Equal(lineAt4, result.Line[4]);
        Assert.Equal(lineAt5, result.Line[5]);

        Assert.Equal(0m, result.Signal[2]); // 種そのものなので、まだ「シグナル値」としては出力しない
        Assert.Equal(signalAt3, result.Signal[3]);
        Assert.Equal(signalAt4, result.Signal[4]);
        Assert.Equal(signalAt5, result.Signal[5]);

        Assert.Equal(lineAt3 - signalAt3, result.Histogram[3]);
        Assert.Equal(lineAt4 - signalAt4, result.Histogram[4]);
        Assert.Equal(lineAt5 - signalAt5, result.Histogram[5]);
    }

    [Fact]
    public void AtrWilder_MatchesHandComputedSeedAndRecursion()
    {
        var bars = new[]
        {
            new DailyBar("7203", new DateOnly(2026, 1, 1), 9m, 10m, 8m, 9m, 1000),
            new DailyBar("7203", new DateOnly(2026, 1, 2), 10m, 11m, 9m, 10m, 1000),
            new DailyBar("7203", new DateOnly(2026, 1, 3), 11m, 12m, 9m, 11m, 1000),
            new DailyBar("7203", new DateOnly(2026, 1, 4), 12m, 13m, 10m, 12m, 1000),
        };

        var atr = TechnicalIndicators.AtrWilder(bars, period: 2);

        // TR[1]=max(11-9,|11-9|,|9-9|)=2, TR[2]=max(12-9,|12-10|,|9-10|)=3, TR[3]=max(13-10,|13-11|,|10-11|)=3
        Assert.Equal(0m, atr[0]);
        Assert.Equal(0m, atr[1]);
        Assert.Equal(2.5m, atr[2]); // seed = avg(2,3)
        Assert.Equal(2.75m, atr[3]); // (2.5*1+3)/2
    }

    [Fact]
    public void Indicators_AppendingFutureBars_DoesNotChangeEarlierValues()
    {
        // 末尾に将来のバーを追加しても、それ以前のインデックスの指標値が変わらないこと
        // （すべて過去→現在への一方向の反復のみで計算しているための帰結。未来データ混入なしの回帰テスト）。
        var fullBars = new List<DailyBar>();
        var date = new DateOnly(2024, 1, 1);
        var price = 1000m;
        for (var i = 0; i < 230; i++)
        {
            var step = ((i % 7) - 3) * 2m; // -6..+6の緩い上下動
            price += step;
            var close = price;
            var open = price - (step / 2m);
            var high = Math.Max(open, close) + 3m;
            var low = Math.Min(open, close) - 3m;
            fullBars.Add(new DailyBar("7203", date.AddDays(i), open, high, low, close, 200_000L + (i * 100)));
        }

        var truncated = fullBars.Take(200).ToList();
        const int checkIndex = 199;

        var closesFull = fullBars.Select(b => b.Close).ToArray();
        var closesTruncated = truncated.Select(b => b.Close).ToArray();

        var macdFull = TechnicalIndicators.Macd(closesFull, 16, 34, 9);
        var macdTruncated = TechnicalIndicators.Macd(closesTruncated, 16, 34, 9);
        var ema100Full = TechnicalIndicators.Ema(closesFull, 100);
        var ema100Truncated = TechnicalIndicators.Ema(closesTruncated, 100);
        var atrFull = TechnicalIndicators.AtrWilder(fullBars, 14);
        var atrTruncated = TechnicalIndicators.AtrWilder(truncated, 14);
        var volumeFull = TechnicalIndicators.VolumeRatio(fullBars, 20);
        var volumeTruncated = TechnicalIndicators.VolumeRatio(truncated, 20);

        Assert.Equal(macdTruncated.Line[checkIndex], macdFull.Line[checkIndex]);
        Assert.Equal(macdTruncated.Signal[checkIndex], macdFull.Signal[checkIndex]);
        Assert.Equal(macdTruncated.Histogram[checkIndex], macdFull.Histogram[checkIndex]);
        Assert.Equal(ema100Truncated[checkIndex], ema100Full[checkIndex]);
        Assert.Equal(atrTruncated[checkIndex], atrFull[checkIndex]);
        Assert.Equal(volumeTruncated[checkIndex], volumeFull[checkIndex]);
    }

    [Fact]
    public void VolumeRatio_ExcludesCurrentDayFromAverage()
    {
        var bars = new List<DailyBar>();
        var date = new DateOnly(2026, 1, 1);
        for (var i = 0; i < 20; i++)
        {
            bars.Add(new DailyBar("7203", date.AddDays(i), 100m, 101m, 99m, 100m, 1000));
        }

        bars.Add(new DailyBar("7203", date.AddDays(20), 100m, 101m, 99m, 100m, 5000)); // 当日

        var ratios = TechnicalIndicators.VolumeRatio(bars, averagePeriod: 20);

        Assert.Equal(5m, ratios[20]); // 5000 / 平均1000（当日を含まない）
    }
}
