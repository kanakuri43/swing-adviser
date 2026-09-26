namespace SwingAdviser.Infrastructure.Configuration;

public sealed class LiquidityFilterOptions
{
    public decimal MinimumAverageTurnoverJpy { get; init; }
    public int TurnoverAveragePeriodDays { get; init; }
}
