using SwingAdviser.Backtest;
using SwingAdviser.Domain.Common;
using SwingAdviser.Domain.MarketData;
using SwingAdviser.Tests.Domain;

namespace SwingAdviser.Tests.Backtest;

/// <summary>
/// 平らな日足（始値=終値=1000、高値1005、安値995）を土台に、シグナル翌日以降の特定の日だけ値を差し替えて約定モデルを検証する。
/// 損切はATR=10のとき Long: 建値−30、Short: 建値+25（CLAUDE.md「リスク管理」の仮値）。
/// </summary>
public class TradeSimulatorTests
{
    private const int SignalIndex = 220;
    private const int EntryIndex = SignalIndex + 1;
    private const decimal Atr = 10m;

    private readonly TradeSimulator _simulator = new(TestFixtures.DefaultStrategyParameters());

    [Fact]
    public void Simulate_EntersAtNextDayOpen_NotSignalDayClose()
    {
        var bars = FlatBars();
        Set(bars, EntryIndex, open: 1010, high: 1015, low: 1005, close: 1010);
        FlatFrom(bars, EntryIndex + 1, 1010);

        var trade = _simulator.Simulate("1000", TradeDirection.Long, bars, SignalIndex, Atr, bars[^1].TradeDate)!;

        Assert.Equal(bars[SignalIndex].TradeDate, trade.SignalDate);
        Assert.Equal(bars[EntryIndex].TradeDate, trade.EntryDate);
        Assert.Equal(1010m, trade.EntryPrice);
        Assert.Equal(980m, trade.StopLossPrice);
    }

    [Fact]
    public void Simulate_Long_StopTouchedIntraday_FillsAtStopPrice()
    {
        var bars = FlatBars();
        Set(bars, EntryIndex + 2, open: 1000, high: 1005, low: 960, close: 990);
        FlatFrom(bars, EntryIndex + 3, 990);

        var trade = _simulator.Simulate("1000", TradeDirection.Long, bars, SignalIndex, Atr, bars[^1].TradeDate)!;

        Assert.Equal(ExitReason.StopLoss, trade.ExitReason);
        Assert.Equal(bars[EntryIndex + 2].TradeDate, trade.ExitDate);
        Assert.Equal(970m, trade.StopLossPrice);
        Assert.Equal(970m, trade.AverageExitPrice);
        Assert.Equal(-1m, trade.ResultR);
    }

    [Fact]
    public void Simulate_Long_GapDownThroughStop_FillsAtOpen()
    {
        var bars = FlatBars();
        Set(bars, EntryIndex + 2, open: 940, high: 950, low: 930, close: 945);
        FlatFrom(bars, EntryIndex + 3, 945);

        var trade = _simulator.Simulate("1000", TradeDirection.Long, bars, SignalIndex, Atr, bars[^1].TradeDate)!;

        Assert.Equal(ExitReason.StopLoss, trade.ExitReason);
        Assert.Equal(940m, trade.AverageExitPrice);
        Assert.Equal(-2m, trade.ResultR);
    }

    [Fact]
    public void Simulate_Long_StopTouchedOnEntryDay_IsStoppedOut()
    {
        var bars = FlatBars();
        Set(bars, EntryIndex, open: 1000, high: 1005, low: 960, close: 990);
        FlatFrom(bars, EntryIndex + 1, 990);

        var trade = _simulator.Simulate("1000", TradeDirection.Long, bars, SignalIndex, Atr, bars[^1].TradeDate)!;

        Assert.Equal(ExitReason.StopLoss, trade.ExitReason);
        Assert.Equal(bars[EntryIndex].TradeDate, trade.ExitDate);
        Assert.Equal(970m, trade.AverageExitPrice);
        Assert.Equal(0, trade.HoldingBusinessDays);
    }

    [Fact]
    public void Simulate_Short_StopTouchedIntraday_FillsAtStopPrice_AndGapFillsAtOpen()
    {
        var touched = FlatBars();
        Set(touched, EntryIndex + 2, open: 1000, high: 1040, low: 995, close: 1010);
        FlatFrom(touched, EntryIndex + 3, 1010);
        var tradeTouched = _simulator.Simulate("1000", TradeDirection.Short, touched, SignalIndex, Atr, touched[^1].TradeDate)!;

        Assert.Equal(1025m, tradeTouched.StopLossPrice);
        Assert.Equal(1025m, tradeTouched.AverageExitPrice);
        Assert.Equal(-1m, tradeTouched.ResultR);

        var gapped = FlatBars();
        Set(gapped, EntryIndex + 2, open: 1050, high: 1060, low: 1045, close: 1055);
        FlatFrom(gapped, EntryIndex + 3, 1055);
        var tradeGapped = _simulator.Simulate("1000", TradeDirection.Short, gapped, SignalIndex, Atr, gapped[^1].TradeDate)!;

        Assert.Equal(ExitReason.StopLoss, tradeGapped.ExitReason);
        Assert.Equal(1050m, tradeGapped.AverageExitPrice);
        Assert.Equal(-2m, tradeGapped.ResultR);
    }

    [Fact]
    public void Simulate_StopLossIsNotRecomputedAsPriceMoves()
    {
        // 建値1000・損切970。途中で値が上がってATRが変わっても、970を割るまで保有し続け、970で約定する。
        var bars = FlatBars();
        Set(bars, EntryIndex + 1, open: 1000, high: 1020, low: 995, close: 1015);
        Set(bars, EntryIndex + 2, open: 1015, high: 1020, low: 1005, close: 1010);
        Set(bars, EntryIndex + 3, open: 1000, high: 1005, low: 965, close: 980);
        FlatFrom(bars, EntryIndex + 4, 980);

        var trade = _simulator.Simulate("1000", TradeDirection.Long, bars, SignalIndex, Atr, bars[^1].TradeDate)!;

        Assert.Equal(970m, trade.StopLossPrice);
        Assert.Equal(970m, trade.AverageExitPrice);
        Assert.Equal(bars[EntryIndex + 3].TradeDate, trade.ExitDate);
    }

    [Fact]
    public void Simulate_TimeStop_ExitsAtNextOpenAfterTwentyBusinessDays()
    {
        var bars = FlatBars();

        var trade = _simulator.Simulate("1000", TradeDirection.Long, bars, SignalIndex, Atr, bars[^1].TradeDate)!;

        // 建玉から20営業日目の大引けで判定 → 翌営業日（21日目）の始値で決済
        Assert.Equal(ExitReason.TimeStop, trade.ExitReason);
        Assert.Equal(21, trade.HoldingBusinessDays);
        Assert.Equal(bars[EntryIndex + 21].TradeDate, trade.ExitDate);
        Assert.Equal(0m, trade.ResultR);
    }

    [Fact]
    public void Simulate_TakeProfit_PartialAtNextOpen_ThenRemainderClosedAtDataEnd()
    {
        // 建値1000・1R=30。EntryIndex+1の終値1050=1.67Rで利確判定 → 翌日始値1055で半分決済。残りはデータ末尾の終値で決済。
        var bars = FlatBars(count: EntryIndex + 4);
        Set(bars, EntryIndex + 1, open: 1000, high: 1055, low: 995, close: 1050);
        Set(bars, EntryIndex + 2, open: 1055, high: 1060, low: 1045, close: 1050);
        Set(bars, EntryIndex + 3, open: 1050, high: 1055, low: 1045, close: 1050);

        var trade = _simulator.Simulate("1000", TradeDirection.Long, bars, SignalIndex, Atr, bars[^1].TradeDate)!;

        Assert.True(trade.PartialTakeProfitDone);
        // (500×(1055−1000) + 500×(1050−1000)) ÷ 1000 ÷ 30
        Assert.Equal(1.75m, trade.ResultR, 4);
    }

    [Fact]
    public void Simulate_DataEndsBeforeGlobalLastDate_IsDataEnd_OtherwisePeriodEnd()
    {
        var bars = FlatBars(count: EntryIndex + 5);

        var delisted = _simulator.Simulate("1000", TradeDirection.Long, bars, SignalIndex, Atr, bars[^1].TradeDate.AddDays(30))!;
        var periodEnd = _simulator.Simulate("1000", TradeDirection.Long, bars, SignalIndex, Atr, bars[^1].TradeDate)!;

        Assert.Equal(ExitReason.DataEnd, delisted.ExitReason);
        Assert.Equal(ExitReason.PeriodEnd, periodEnd.ExitReason);
        Assert.Equal(bars[^1].TradeDate, periodEnd.ExitDate);
    }

    [Fact]
    public void Simulate_SignalOnLastBar_CannotEnter()
    {
        var bars = FlatBars(count: SignalIndex + 1);

        Assert.Null(_simulator.Simulate("1000", TradeDirection.Long, bars, SignalIndex, Atr, bars[^1].TradeDate));
    }

    private static List<DailyBar> FlatBars(int count = 300)
    {
        var start = new DateOnly(2024, 1, 1);
        return Enumerable.Range(0, count)
            .Select(i => new DailyBar("1000", start.AddDays(i), 1000, 1005, 995, 1000, 200_000))
            .ToList();
    }

    private static void Set(List<DailyBar> bars, int index, decimal open, decimal high, decimal low, decimal close) =>
        bars[index] = new DailyBar("1000", bars[index].TradeDate, open, high, low, close, 200_000);

    private static void FlatFrom(List<DailyBar> bars, int startIndex, decimal price)
    {
        for (var i = startIndex; i < bars.Count; i++)
        {
            Set(bars, i, price, price + 5, price - 5, price);
        }
    }
}
