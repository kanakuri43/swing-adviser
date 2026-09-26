using System.Text.Json;
using SwingAdviser.Domain.Analysis;
using SwingAdviser.Domain.Common;
using SwingAdviser.Domain.MarketData;
using SwingAdviser.Domain.Positions;
using SwingAdviser.Domain.Strategy;

namespace SwingAdviser.Domain.Risk;

/// <summary>
/// 保有ポジションの日次再評価。優先順位（損切 &gt; 時間ストップ &gt; Exit &gt; 利確 &gt; Hold）はif-elseの直列評価で表現する。
/// 損切ラインの固定・分割時換算は<see cref="Position"/>側の責務（建玉時に固定し、以後は分割時のみ換算）。
/// </summary>
public sealed class HoldingRiskEvaluator
{
    private readonly StrategyParameters _parameters;
    private readonly string _strategyParametersJson;

    public HoldingRiskEvaluator(StrategyParameters parameters)
    {
        _parameters = parameters;
        _strategyParametersJson = JsonSerializer.Serialize(parameters);
    }

    public HoldingEvaluation Evaluate(Position position, IReadOnlyList<DailyBar> bars, DateTime nowUtc)
    {
        if (position.Status != PositionStatus.Open)
        {
            throw new InvalidOperationException("決済済みのポジションは再評価できません。");
        }

        if (bars.Count < _parameters.AnalysisWindow.MinimumRequiredBars)
        {
            throw new ArgumentException(
                $"分析には最低{_parameters.AnalysisWindow.MinimumRequiredBars}本のバーが必要です（現在{bars.Count}本）。", nameof(bars));
        }

        var openedIndex = -1;
        for (var i = 0; i < bars.Count; i++)
        {
            if (bars[i].TradeDate == position.OpenedDate)
            {
                openedIndex = i;
                break;
            }
        }

        if (openedIndex < 0)
        {
            throw new ArgumentException("barsにポジションの建玉日が含まれていません。", nameof(bars));
        }

        var indicators = _parameters.Indicators;
        var risk = _parameters.Risk;

        var closes = bars.Select(b => b.Close).ToArray();
        var macd = TechnicalIndicators.Macd(closes, indicators.MacdFastPeriod, indicators.MacdSlowPeriod, indicators.MacdSignalPeriod);
        var ema20 = TechnicalIndicators.Ema(closes, indicators.EmaShortPeriod);
        var atr = TechnicalIndicators.AtrWilder(bars, indicators.AtrPeriod);

        var todayIndex = bars.Count - 1;
        var evaluationDate = bars[todayIndex].TradeDate;
        var close = closes[todayIndex];
        var sign = position.Direction == TradeDirection.Long ? 1m : -1m;

        var r = Math.Abs(position.AverageEntryPrice - position.StopLossPrice);
        var achievedR = r == 0 ? 0m : sign * (close - position.AverageEntryPrice) / r;
        var holdingBusinessDays = todayIndex - openedIndex;
        var alreadyPartiallyClosed = position.Executions.Any(e => e.Side == ExecutionSide.Close);

        var macdDeadCross = sign * (macd.Line[todayIndex] - macd.Signal[todayIndex]) <= 0;
        var brokeEma20 = sign * (close - ema20[todayIndex]) <= 0;

        HoldingDecision decision;
        string reason;

        if (sign * (close - position.StopLossPrice) <= 0)
        {
            decision = HoldingDecision.StopLoss;
            reason = $"終値{close}が損切ライン{position.StopLossPrice}に到達しました。";
        }
        else if (holdingBusinessDays >= risk.TimeStopBusinessDays)
        {
            decision = HoldingDecision.TimeStop;
            reason = $"建玉から{holdingBusinessDays}営業日が経過しました（基準{risk.TimeStopBusinessDays}営業日）。";
        }
        else if (achievedR >= risk.PartialTakeProfitRMultiple && (macdDeadCross || brokeEma20))
        {
            decision = HoldingDecision.Exit;
            reason = macdDeadCross
                ? $"到達R倍率{achievedR:F2}Rで、MACD({macd.Line[todayIndex]:F2})がシグナル({macd.Signal[todayIndex]:F2})に対して逆行しました。"
                : $"到達R倍率{achievedR:F2}Rで、終値{close}がEMA20({ema20[todayIndex]:F2})に対して逆行しました。";
        }
        else if (achievedR >= risk.PartialTakeProfitRMultiple && !alreadyPartiallyClosed)
        {
            decision = HoldingDecision.TakeProfit;
            reason = $"到達R倍率{achievedR:F2}Rが利確ライン{risk.PartialTakeProfitRMultiple}Rに到達しました（残数量の{risk.PartialTakeProfitRatio:P0}を一部利確候補として表示）。";
        }
        else
        {
            decision = HoldingDecision.Hold;
            reason = "損切・時間ストップ・Exit・利確のいずれにも該当しません。";
        }

        return new HoldingEvaluation
        {
            EvaluationDate = evaluationDate,
            PositionId = position.Id,
            Decision = decision,
            Reason = reason,
            Close = close,
            Atr14 = atr[todayIndex],
            MacdLine = macd.Line[todayIndex],
            MacdSignal = macd.Signal[todayIndex],
            Ema20 = ema20[todayIndex],
            StopLossPrice = position.StopLossPrice,
            AchievedRMultiple = achievedR,
            HoldingBusinessDays = holdingBusinessDays,
            StrategyParametersJson = _strategyParametersJson,
            CreatedAtUtc = nowUtc,
        };
    }
}
