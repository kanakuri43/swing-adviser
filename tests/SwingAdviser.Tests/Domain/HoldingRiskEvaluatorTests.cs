using SwingAdviser.Domain.Common;
using SwingAdviser.Domain.MarketData;
using SwingAdviser.Domain.Positions;
using SwingAdviser.Domain.Risk;

namespace SwingAdviser.Tests.Domain;

public class HoldingRiskEvaluatorTests
{
    private static readonly DateTime NowUtc = new(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);

    private static Position OpenPosition(TradeDirection direction, string stockCode, DateOnly openedDate, decimal averageEntryPrice, decimal stopLossPrice)
    {
        var executedAtUtc = new DateTime(openedDate.Year, openedDate.Month, openedDate.Day, 0, 30, 0, DateTimeKind.Utc);

        return Position.Open(
            stockCode: stockCode,
            direction: direction,
            isMargin: direction == TradeDirection.Short,
            executedAtUtc: executedAtUtc,
            price: averageEntryPrice,
            quantity: 100,
            marginDueDate: null,
            initialAtr: 10m,
            stopLossPrice: stopLossPrice,
            memo: null,
            nowUtc: executedAtUtc);
    }

    /// <summary>
    /// 緩やかに上昇し、直近40日だけ加速する系列。純粋な一定傾きだとMACDヒストグラムが0近辺に収束してしまい
    /// ゴールデンクロス状態の判定が不安定になるため、直近を加速させて明確に正のヒストグラムを保つ。
    /// </summary>
    private static List<DailyBar> BuildSteadyUptrend(string stockCode, int days, decimal startPrice, decimal dailyDrift)
    {
        var bars = new List<DailyBar>();
        var date = new DateOnly(2024, 1, 1);
        var price = startPrice;
        for (var i = 0; i < days; i++)
        {
            var step = i < days - 40 ? dailyDrift : dailyDrift * 3m;
            price += step;
            var close = price;
            var open = price - (step / 2m);
            var high = Math.Max(open, close) + 3m;
            var low = Math.Min(open, close) - 3m;
            bars.Add(new DailyBar(stockCode, date.AddDays(i), open, high, low, close, 200_000L));
        }

        return bars;
    }

    /// <summary>上昇トレンド後に急落する系列。末尾でMACDがLong側のデッドクロス状態になる想定。</summary>
    private static List<DailyBar> BuildUptrendThenDecline(string stockCode, int totalDays, int declineDays, decimal upStep, decimal downStep)
    {
        var bars = new List<DailyBar>();
        var date = new DateOnly(2024, 1, 1);
        var price = 1000m;
        var uptrendDays = totalDays - declineDays;
        for (var i = 0; i < totalDays; i++)
        {
            var step = i < uptrendDays ? upStep : -downStep;
            price += step;
            var close = price;
            var open = price - (step / 2m);
            var high = Math.Max(open, close) + 3m;
            var low = Math.Min(open, close) - 3m;
            bars.Add(new DailyBar(stockCode, date.AddDays(i), open, high, low, close, 200_000L));
        }

        return bars;
    }

    [Fact]
    public void Evaluate_StopLossAndTimeStopBothTrue_StopLossWins()
    {
        var parameters = TestFixtures.DefaultStrategyParameters();
        var bars = BuildSteadyUptrend("7203", 230, 1000m, 1m);
        var todayClose = bars[^1].Close;
        var openedDate = bars[^25].TradeDate; // 24営業日前 → 時間ストップ(20)も同時成立

        var position = OpenPosition(TradeDirection.Long, "7203", openedDate, averageEntryPrice: todayClose - 100m, stopLossPrice: todayClose + 10m);

        var evaluator = new HoldingRiskEvaluator(parameters);
        var result = evaluator.Evaluate(position, bars, NowUtc);

        Assert.Equal(HoldingDecision.StopLoss, result.Decision);
    }

    [Fact]
    public void Evaluate_TimeStopAndExitBothTrue_TimeStopWins()
    {
        var parameters = TestFixtures.DefaultStrategyParameters();
        var bars = BuildUptrendThenDecline("7203", totalDays: 230, declineDays: 20, upStep: 3m, downStep: 6m);
        var todayClose = bars[^1].Close;
        var openedDate = bars[^25].TradeDate; // 24営業日前 → 時間ストップ成立

        // R=10、到達2.0R（利確ラインを超過）。損切ラインはcloseから十分離す。
        var position = OpenPosition(TradeDirection.Long, "7203", openedDate, averageEntryPrice: todayClose - 20m, stopLossPrice: todayClose - 30m);

        var evaluator = new HoldingRiskEvaluator(parameters);
        var result = evaluator.Evaluate(position, bars, NowUtc);

        Assert.Equal(HoldingDecision.TimeStop, result.Decision);
    }

    [Fact]
    public void Evaluate_ExitAndTakeProfitBothTrue_ExitWins()
    {
        var parameters = TestFixtures.DefaultStrategyParameters();
        var bars = BuildUptrendThenDecline("7203", totalDays: 230, declineDays: 20, upStep: 3m, downStep: 6m);
        var todayClose = bars[^1].Close;
        var openedDate = bars[^10].TradeDate; // 9営業日前 → 時間ストップは不成立

        var position = OpenPosition(TradeDirection.Long, "7203", openedDate, averageEntryPrice: todayClose - 20m, stopLossPrice: todayClose - 30m);

        var evaluator = new HoldingRiskEvaluator(parameters);
        var result = evaluator.Evaluate(position, bars, NowUtc);

        Assert.Equal(HoldingDecision.Exit, result.Decision);
    }

    [Fact]
    public void Evaluate_AchievedR_JustBelowThreshold_IsHold()
    {
        var parameters = TestFixtures.DefaultStrategyParameters();
        var bars = BuildSteadyUptrend("7203", 230, 1000m, 1m);
        var close = bars[^1].Close;
        var openedDate = bars[^10].TradeDate;

        // R=100、到達1.49R（利確ライン1.5R未満）。
        var position = OpenPosition(TradeDirection.Long, "7203", openedDate, averageEntryPrice: close - 149m, stopLossPrice: close - 249m);

        var evaluator = new HoldingRiskEvaluator(parameters);
        var result = evaluator.Evaluate(position, bars, NowUtc);

        Assert.Equal(HoldingDecision.Hold, result.Decision);
        Assert.Equal(1.49m, result.AchievedRMultiple);
    }

    [Fact]
    public void Evaluate_AchievedR_AtThreshold_IsTakeProfit()
    {
        var parameters = TestFixtures.DefaultStrategyParameters();
        var bars = BuildSteadyUptrend("7203", 230, 1000m, 1m);
        var close = bars[^1].Close;
        var openedDate = bars[^10].TradeDate;

        // R=100、到達1.50R（利確ラインちょうど）。
        var position = OpenPosition(TradeDirection.Long, "7203", openedDate, averageEntryPrice: close - 150m, stopLossPrice: close - 250m);

        var evaluator = new HoldingRiskEvaluator(parameters);
        var result = evaluator.Evaluate(position, bars, NowUtc);

        Assert.Equal(HoldingDecision.TakeProfit, result.Decision);
        Assert.Equal(1.50m, result.AchievedRMultiple);
    }

    [Fact]
    public void Evaluate_AlreadyPartiallyClosed_TakeProfitIsNotRepeated()
    {
        var parameters = TestFixtures.DefaultStrategyParameters();
        var bars = BuildSteadyUptrend("7203", 230, 1000m, 1m);
        var close = bars[^1].Close;
        var openedDate = bars[^10].TradeDate;

        var position = OpenPosition(TradeDirection.Long, "7203", openedDate, averageEntryPrice: close - 150m, stopLossPrice: close - 250m);
        position.AddCloseExecution(NowUtc.AddDays(-1), close - 50m, 50, NowUtc.AddDays(-1)); // 既に一部利確済み

        var evaluator = new HoldingRiskEvaluator(parameters);
        var result = evaluator.Evaluate(position, bars, NowUtc);

        Assert.NotEqual(HoldingDecision.TakeProfit, result.Decision);
    }

    [Fact]
    public void Evaluate_TimeStopBoundary_NineteenDaysIsHold_TwentyDaysIsTimeStop()
    {
        var parameters = TestFixtures.DefaultStrategyParameters();
        var bars = BuildSteadyUptrend("7203", 230, 1000m, 1m);
        var close = bars[^1].Close;

        // 到達Rを小さく保ち、利確・Exitが成立しないようにする。
        var position19 = OpenPosition(TradeDirection.Long, "7203", bars[^20].TradeDate, averageEntryPrice: close - 1m, stopLossPrice: close - 200m);
        var position20 = OpenPosition(TradeDirection.Long, "7203", bars[^21].TradeDate, averageEntryPrice: close - 1m, stopLossPrice: close - 200m);

        var evaluator = new HoldingRiskEvaluator(parameters);
        var result19 = evaluator.Evaluate(position19, bars, NowUtc);
        var result20 = evaluator.Evaluate(position20, bars, NowUtc);

        Assert.Equal(19, result19.HoldingBusinessDays);
        Assert.Equal(HoldingDecision.Hold, result19.Decision);
        Assert.Equal(20, result20.HoldingBusinessDays);
        Assert.Equal(HoldingDecision.TimeStop, result20.Decision);
    }

    [Fact]
    public void Evaluate_ReflectedPriceAxis_ProducesSymmetricShortResult()
    {
        var parameters = TestFixtures.DefaultStrategyParameters();
        var longBars = BuildSteadyUptrend("7203", 230, 1000m, 1m);
        var close = longBars[^1].Close;
        var openedDate = longBars[^10].TradeDate;

        var longPosition = OpenPosition(TradeDirection.Long, "7203", openedDate, averageEntryPrice: close - 150m, stopLossPrice: close - 250m);

        const decimal axis = 100_000m;
        var shortBars = TestFixtures.Reflect(longBars, "7203", axis);
        var shortPosition = OpenPosition(
            TradeDirection.Short,
            "7203",
            openedDate,
            averageEntryPrice: axis - (close - 150m),
            stopLossPrice: axis - (close - 250m));

        var evaluator = new HoldingRiskEvaluator(parameters);
        var longResult = evaluator.Evaluate(longPosition, longBars, NowUtc);
        var shortResult = evaluator.Evaluate(shortPosition, shortBars, NowUtc);

        Assert.Equal(longResult.Decision, shortResult.Decision);
        Assert.Equal(longResult.AchievedRMultiple, shortResult.AchievedRMultiple);
        Assert.Equal(longResult.HoldingBusinessDays, shortResult.HoldingBusinessDays);
        Assert.Equal(longResult.Atr14, shortResult.Atr14);
    }

    [Fact]
    public void Evaluate_ClosedPosition_Throws()
    {
        var parameters = TestFixtures.DefaultStrategyParameters();
        var bars = BuildSteadyUptrend("7203", 230, 1000m, 1m);
        var close = bars[^1].Close;
        var position = OpenPosition(TradeDirection.Long, "7203", bars[^10].TradeDate, averageEntryPrice: close - 10m, stopLossPrice: close - 50m);
        position.AddCloseExecution(NowUtc, close, 100, NowUtc);

        var evaluator = new HoldingRiskEvaluator(parameters);

        Assert.Throws<InvalidOperationException>(() => evaluator.Evaluate(position, bars, NowUtc));
    }

    [Fact]
    public void Evaluate_OpenedDateNotInBars_Throws()
    {
        var parameters = TestFixtures.DefaultStrategyParameters();
        var bars = BuildSteadyUptrend("7203", 230, 1000m, 1m);
        var close = bars[^1].Close;

        // 建玉日をbarsの範囲外にする。
        var position = OpenPosition(TradeDirection.Long, "7203", bars[0].TradeDate.AddDays(-1), averageEntryPrice: close - 10m, stopLossPrice: close - 50m);

        var evaluator = new HoldingRiskEvaluator(parameters);

        Assert.Throws<ArgumentException>(() => evaluator.Evaluate(position, bars, NowUtc));
    }
}
