using SwingAdviser.Application.Positions;
using SwingAdviser.Domain.Common;
using SwingAdviser.Domain.Positions;

namespace SwingAdviser.Presentation.ViewModels;

/// <summary>履歴タブの1行。約定は監査原票の一覧表示なので読み取り専用（コマンドを持たない）。</summary>
public sealed class ExecutionRow(ExecutionOverview overview)
{
    public ExecutionOverview Overview { get; } = overview;

    public long PositionId => Overview.PositionId;

    public string StockCode => Overview.StockCode;

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
