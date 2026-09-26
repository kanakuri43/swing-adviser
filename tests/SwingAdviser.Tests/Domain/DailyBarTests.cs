using SwingAdviser.Domain.MarketData;

namespace SwingAdviser.Tests.Domain;

public class DailyBarTests
{
    [Fact]
    public void ApplySplit_TwoForOne_HalvesPricesAndDoublesVolume_KeepsTurnoverUnchanged()
    {
        var bar = new DailyBar("7203", new DateOnly(2026, 1, 5), open: 1000m, high: 1050m, low: 990m, close: 1020m, volume: 1_000_000);
        var turnoverBefore = bar.Close * bar.Volume;

        bar.ApplySplit(2m);

        Assert.Equal(500m, bar.Open);
        Assert.Equal(525m, bar.High);
        Assert.Equal(495m, bar.Low);
        Assert.Equal(510m, bar.Close);
        Assert.Equal(2_000_000, bar.Volume);
        Assert.Equal(turnoverBefore, bar.Close * bar.Volume);
    }

    [Fact]
    public void Constructor_HighLessThanLow_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new DailyBar("7203", new DateOnly(2026, 1, 5), open: 1000m, high: 990m, low: 1000m, close: 995m, volume: 1_000));
    }
}
