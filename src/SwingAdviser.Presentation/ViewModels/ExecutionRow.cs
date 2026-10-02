using SwingAdviser.Application.Positions;
using SwingAdviser.Domain.Common;
using SwingAdviser.Domain.Positions;

namespace SwingAdviser.Presentation.ViewModels;

/// <summary>履歴タブの1行。表示専用（訂正はコードビハインドから訂正ダイアログを開く）。</summary>
public sealed class ExecutionRow(ExecutionOverview overview)
{
    public ExecutionOverview Overview { get; } = overview;

    public long PositionId => Overview.PositionId;

    public string StockCode => Overview.StockCode;

    public string StockName => Overview.StockName;

    public decimal? RealizedProfitAndLoss => Overview.RealizedProfitAndLoss;

    public string ProfitAndLossState => Overview.RealizedProfitAndLoss switch
    {
        > 0 => "Profit",
        < 0 => "Loss",
        _ => "Neutral",
    };

    public string Direction => Overview.Direction == TradeDirection.Long ? "Long" : "Short";

    public string Side => Overview.Side == ExecutionSide.Open ? "新規" : "決済";

    public DateTime ExecutedAtJst => Overview.ExecutedAtJst;

    public decimal Price => Overview.Price;

    public int Quantity => Overview.Quantity;

    public string MarginDueDateText => Overview.MarginDueDate?.ToString("yyyy-MM-dd")
        ?? (Overview.Side == ExecutionSide.Open ? "現物または未確認" : "—");

    public string CorrectionText => Overview.CorrectionCount > 0 ? $"訂正{Overview.CorrectionCount}回" : "訂正なし";

    public bool IsPositionOpen => Overview.IsPositionOpen;
}
