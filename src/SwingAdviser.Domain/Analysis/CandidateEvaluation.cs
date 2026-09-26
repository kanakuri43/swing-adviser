using SwingAdviser.Domain.Common;

namespace SwingAdviser.Domain.Analysis;

/// <summary>
/// 候補化ゲートを通過した銘柄の判定を判定日ごとに1行追記する。使用した指標値・戦略パラメータをそのまま保存し、
/// 別テーブルのmanifest/hashによる凍結はしない。追記専用（作成後は変更しない）。
/// </summary>
public class CandidateEvaluation
{
    public long Id { get; private set; }

    public required DateOnly EvaluationDate { get; init; }

    public required string StockCode { get; init; }

    public required TradeDirection Direction { get; init; }

    public required int Score { get; init; }

    public required ConfidenceLevel Confidence { get; init; }

    public required decimal Close { get; init; }

    public required decimal MacdLine { get; init; }

    public required decimal MacdSignal { get; init; }

    public required decimal MacdHistogram { get; init; }

    public required decimal PreviousMacdHistogram { get; init; }

    public required decimal Ema20 { get; init; }

    public required decimal Ema100 { get; init; }

    public required decimal Ema100TwentyDaysAgo { get; init; }

    public required decimal Atr14 { get; init; }

    public required decimal VolumeRatio { get; init; }

    public required int MacdCrossAgeDays { get; init; }

    public required bool MarketRegimeAligned { get; init; }

    public required int MacdFreshnessScore { get; init; }

    public required int MacdPositionScore { get; init; }

    public required int MacdMomentumScore { get; init; }

    public required int TrendStrengthScore { get; init; }

    public required int VolumeScore { get; init; }

    public required int MarketRegimeScore { get; init; }

    public required string StrategyParametersJson { get; init; }

    public required DateTime CreatedAtUtc { get; init; }
}
