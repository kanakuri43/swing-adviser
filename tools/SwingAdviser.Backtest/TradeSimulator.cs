using SwingAdviser.Domain.Common;
using SwingAdviser.Domain.MarketData;
using SwingAdviser.Domain.Positions;
using SwingAdviser.Domain.Risk;
using SwingAdviser.Domain.Strategy;

namespace SwingAdviser.Backtest;

/// <summary>
/// 1件のシグナルから決済までを日足で再現する。約定モデルは CLAUDE.md「バックテスト」節の通り:
/// シグナル翌日の始値で建て、損切は逆指値（ザラ場でライン到達ならライン価格、ギャップなら始値）、
/// それ以外は終値で <see cref="HoldingRiskEvaluator"/> を判定して翌営業日の始値で決済する。
/// </summary>
public sealed class TradeSimulator
{
    private const int Quantity = 1000;

    private readonly StrategyParameters _parameters;
    private readonly HoldingRiskEvaluator _evaluator;

    public TradeSimulator(StrategyParameters parameters)
    {
        _parameters = parameters;
        _evaluator = new HoldingRiskEvaluator(parameters);
    }

    /// <param name="signalIndex">シグナル日（大引け後に判定した日）の bars 上の位置。</param>
    /// <param name="atr">シグナル日のATR14。</param>
    /// <param name="lastDataDate">全体のデータ最終日。銘柄のデータがこれより前に終わっていれば「データ終了」とする。</param>
    /// <returns>翌日のバーが無く建てられない場合は null。</returns>
    public SimulatedTrade? Simulate(
        string stockCode,
        TradeDirection direction,
        IReadOnlyList<DailyBar> bars,
        int signalIndex,
        decimal atr,
        DateOnly lastDataDate)
    {
        var entryIndex = signalIndex + 1;
        if (entryIndex >= bars.Count)
        {
            return null;
        }

        var risk = _parameters.Risk;
        var sign = direction == TradeDirection.Long ? 1m : -1m;
        var stopMultiple = direction == TradeDirection.Long ? risk.LongStopLossAtrMultiple : risk.ShortStopLossAtrMultiple;

        var entryPrice = bars[entryIndex].Open;
        var stopPrice = entryPrice - (sign * stopMultiple * atr);
        var oneR = Math.Abs(entryPrice - stopPrice);
        var nowUtc = DateTime.UtcNow;

        var position = Position.Open(
            stockCode, direction, isMargin: direction == TradeDirection.Short,
            ExecutionTime(bars[entryIndex].TradeDate), entryPrice, Quantity, null, atr, stopPrice, null, nowUtc);

        var fills = new List<(int Quantity, decimal Price)>();
        var pending = PendingOrder.None;
        var pendingReason = ExitReason.Exit;
        var partialDone = false;
        decimal mfe = 0m, mae = 0m;

        for (var i = entryIndex; i < bars.Count; i++)
        {
            var bar = bars[i];

            // 1) 前日の終値判定に基づく成行（始値）の決済
            if (pending == PendingOrder.Full)
            {
                Close(position, fills, bar, bar.Open, (int)position.RemainingQuantity, nowUtc);
                return Build(i, pendingReason);
            }

            if (pending == PendingOrder.Partial)
            {
                var partialQuantity = Math.Clamp(
                    (int)Math.Round(position.RemainingQuantity * risk.PartialTakeProfitRatio, MidpointRounding.AwayFromZero),
                    1,
                    (int)position.RemainingQuantity);
                Close(position, fills, bar, bar.Open, partialQuantity, nowUtc);
                partialDone = true;
                if (position.RemainingQuantity == 0)
                {
                    return Build(i, ExitReason.Exit);
                }
            }

            pending = PendingOrder.None;

            // 2) 逆指値（建玉日を含む）。始値がラインを超えていればギャップとして始値で約定
            var favorable = sign > 0 ? bar.High : bar.Low;
            var adverse = sign > 0 ? bar.Low : bar.High;
            mfe = Math.Max(mfe, sign * (favorable - entryPrice) / oneR);
            mae = Math.Min(mae, sign * (adverse - entryPrice) / oneR);

            if (sign * (adverse - stopPrice) <= 0)
            {
                var fillPrice = sign * (bar.Open - stopPrice) <= 0 ? bar.Open : stopPrice;
                Close(position, fills, bar, fillPrice, (int)position.RemainingQuantity, nowUtc);
                return Build(i, ExitReason.StopLoss);
            }

            // 3) 大引け後の判定。最終バーなら翌日の始値が無いので終値で打ち切る
            if (i == bars.Count - 1)
            {
                Close(position, fills, bar, bar.Close, (int)position.RemainingQuantity, nowUtc);
                return Build(i, bar.TradeDate < lastDataDate ? ExitReason.DataEnd : ExitReason.PeriodEnd);
            }

            var windowStart = Math.Max(0, i + 1 - _parameters.AnalysisWindow.BarsToFetch);
            var window = bars.Skip(windowStart).Take(i + 1 - windowStart).ToList();
            var evaluation = _evaluator.Evaluate(position, window, nowUtc);

            switch (evaluation.Decision)
            {
                case HoldingDecision.StopLoss:
                    pending = PendingOrder.Full;
                    pendingReason = ExitReason.StopLoss;
                    break;
                case HoldingDecision.TimeStop:
                    pending = PendingOrder.Full;
                    pendingReason = ExitReason.TimeStop;
                    break;
                case HoldingDecision.Exit:
                    pending = PendingOrder.Full;
                    pendingReason = ExitReason.Exit;
                    break;
                case HoldingDecision.TakeProfit:
                    pending = PendingOrder.Partial;
                    break;
            }
        }

        return null; // 到達しない（最終バーで必ず打ち切る）

        SimulatedTrade Build(int exitIndex, ExitReason reason)
        {
            var totalQuantity = fills.Sum(f => f.Quantity);
            var averageExit = fills.Sum(f => f.Quantity * f.Price) / totalQuantity;
            var resultR = sign * (averageExit - entryPrice) / oneR;
            return new SimulatedTrade(
                stockCode, direction, bars[signalIndex].TradeDate, bars[entryIndex].TradeDate, entryPrice, stopPrice,
                bars[exitIndex].TradeDate, averageExit, resultR, mfe, mae, exitIndex - entryIndex,
                partialDone, reason, exitIndex);
        }
    }

    private static void Close(Position position, List<(int Quantity, decimal Price)> fills, DailyBar bar, decimal price, int quantity, DateTime nowUtc)
    {
        position.AddCloseExecution(ExecutionTime(bar.TradeDate), price, quantity, nowUtc);
        fills.Add((quantity, price));
    }

    /// <summary>JSTの正午（UTC 03:00）。<see cref="Position.OpenedDate"/> が取引日と一致するようにする。</summary>
    private static DateTime ExecutionTime(DateOnly tradeDate) =>
        new(tradeDate.Year, tradeDate.Month, tradeDate.Day, 3, 0, 0, DateTimeKind.Utc);

    private enum PendingOrder
    {
        None,
        Full,
        Partial,
    }
}
