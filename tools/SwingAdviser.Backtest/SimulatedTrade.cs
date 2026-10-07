using SwingAdviser.Domain.Analysis;
using SwingAdviser.Domain.Common;

namespace SwingAdviser.Backtest;

public enum ExitReason
{
    StopLoss,
    TimeStop,
    Exit,
    DataEnd,
    PeriodEnd,
}

/// <summary>1トレードの結果。R倍率は建値と損切ラインの差（1R）を単位とし、部分決済を含む全体の平均。</summary>
public sealed record SimulatedTrade(
    string StockCode,
    TradeDirection Direction,
    DateOnly SignalDate,
    DateOnly EntryDate,
    decimal EntryPrice,
    decimal StopLossPrice,
    DateOnly ExitDate,
    decimal AverageExitPrice,
    decimal ResultR,
    decimal MfeR,
    decimal MaeR,
    int HoldingBusinessDays,
    bool PartialTakeProfitDone,
    ExitReason ExitReason,
    int ExitIndex);

/// <summary>トレード結果と、その元になった候補時点の指標・スコア。</summary>
public sealed record BacktestTrade(SimulatedTrade Trade, CandidateEvaluation Candidate)
{
    public decimal OverextensionAtrMultiple =>
        (Trade.Direction == TradeDirection.Long ? 1m : -1m) * (Candidate.Close - Candidate.Ema20) / Candidate.Atr14;
}
