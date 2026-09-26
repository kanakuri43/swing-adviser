namespace SwingAdviser.Domain.Risk;

/// <summary>
/// 保有ポジションの日次再評価を判定日ごとに1行追記する。使用した指標値・戦略パラメータをそのまま保存し、
/// 別テーブルのmanifest/hashによる凍結はしない。追記専用（作成後は変更しない）。
/// </summary>
public class HoldingEvaluation
{
    public long Id { get; private set; }

    public required DateOnly EvaluationDate { get; init; }

    public required long PositionId { get; init; }

    public required HoldingDecision Decision { get; init; }

    public required string Reason { get; init; }

    public required decimal Close { get; init; }

    public required decimal Atr14 { get; init; }

    public required decimal MacdLine { get; init; }

    public required decimal MacdSignal { get; init; }

    public required decimal Ema20 { get; init; }

    public required decimal StopLossPrice { get; init; }

    public required decimal AchievedRMultiple { get; init; }

    public required int HoldingBusinessDays { get; init; }

    public required string StrategyParametersJson { get; init; }

    public required DateTime CreatedAtUtc { get; init; }
}
