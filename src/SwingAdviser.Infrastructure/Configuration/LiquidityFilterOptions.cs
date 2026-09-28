namespace SwingAdviser.Infrastructure.Configuration;

public sealed class LiquidityFilterOptions
{
    public decimal MinimumAverageTurnoverJpy { get; init; }
    public int TurnoverAveragePeriodDays { get; init; }

    /// <summary>流動性フィルタ外の銘柄を再取得して再判定する間隔（日数）。</summary>
    public int RecheckIntervalDays { get; init; }
}
