namespace SwingAdviser.Domain.Risk;

/// <summary>
/// 優先順位順（CLAUDE.md「リスク管理」）: 損切 > 時間ストップ > Exit（テクニカル反転）> 利確 > Hold。
/// 同一日に複数成立した場合は宣言順で最優先のものを採用する。
/// </summary>
public enum HoldingDecision
{
    StopLoss,
    TimeStop,
    Exit,
    TakeProfit,
    Hold,
}
