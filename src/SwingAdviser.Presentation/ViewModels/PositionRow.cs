using SwingAdviser.Application.Risk;
using SwingAdviser.Domain.Common;
using SwingAdviser.Domain.Risk;

namespace SwingAdviser.Presentation.ViewModels;

/// <summary>保有タブの1行。信用返済期限は「未確認」「警告」「期限超過」をXAMLのDataTriggerで色分けする想定。</summary>
public sealed class PositionRow(HoldingOverview overview)
{
    public HoldingOverview Overview { get; } = overview;

    public long PositionId => Overview.PositionId;

    public string StockCode => Overview.StockCode;

    public string StockName => Overview.StockName;

    public string Direction => Overview.Direction == TradeDirection.Long ? "Long" : "Short";

    public bool IsMargin => Overview.IsMargin;

    public decimal RemainingQuantity => Overview.RemainingQuantity;

    public decimal AverageEntryPrice => Overview.AverageEntryPrice;

    public decimal StopLossPrice => Overview.StopLossPrice;

    public decimal? LatestClose => Overview.LatestClose;

    public decimal? CurrentProfitAndLoss => Overview.CurrentProfitAndLoss;

    public string ProfitAndLossState => Overview.CurrentProfitAndLoss switch
    {
        > 0 => "Profit",
        < 0 => "Loss",
        _ => "Neutral",
    };

    public string DecisionText => Overview.Decision switch
    {
        null => "未評価",
        HoldingDecision.StopLoss => "損切",
        HoldingDecision.TimeStop => "時間ストップ",
        HoldingDecision.Exit => "Exit（反転）",
        HoldingDecision.TakeProfit => "一部利確候補",
        HoldingDecision.Hold => "Hold",
        _ => Overview.Decision.ToString() ?? "未評価",
    };

    public string? DecisionReason => Overview.DecisionReason;

    public decimal? AchievedRMultiple => Overview.AchievedRMultiple;

    public string MarginDueText => Overview.MarginDueStatus switch
    {
        MarginDueStatus.NotMargin => "現物",
        MarginDueStatus.Unconfirmed => "未確認",
        MarginDueStatus.Overdue => $"期限超過（{Overview.MarginDueDate:yyyy-MM-dd}）",
        MarginDueStatus.Warning => $"警告: 期限接近（{Overview.MarginDueDate:yyyy-MM-dd}）",
        MarginDueStatus.Normal => $"{Overview.MarginDueDate:yyyy-MM-dd}",
        _ => "—",
    };

    public string MarginDueStatusName => Overview.MarginDueStatus.ToString();
}
