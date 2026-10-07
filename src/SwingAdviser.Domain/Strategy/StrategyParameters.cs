namespace SwingAdviser.Domain.Strategy;

public sealed class StrategyParameters
{
    public IndicatorParameters Indicators { get; init; } = new();
    public GateParameters Gates { get; init; } = new();
    public ScoringParameters Scoring { get; init; } = new();
    public RiskParameters Risk { get; init; } = new();
    public AnalysisWindowParameters AnalysisWindow { get; init; } = new();
}

public sealed class IndicatorParameters
{
    public int MacdFastPeriod { get; init; }
    public int MacdSlowPeriod { get; init; }
    public int MacdSignalPeriod { get; init; }
    public int EmaShortPeriod { get; init; }
    public int EmaMediumPeriod { get; init; }
    public int AtrPeriod { get; init; }
    public int VolumeAveragePeriod { get; init; }
}

public sealed class GateParameters
{
    public int MacdCrossMaxAgeDays { get; init; }
    public int TrendSlopeLookbackDays { get; init; }
    public decimal OverextendedAtrMultiple { get; init; }
    public string MarketRegimeSymbol { get; init; } = string.Empty;

    /// <summary>
    /// 未クロスでも候補化する「早期シグナル」に必要な、ヒストグラムの連続拡大日数（当日を含む）。
    /// </summary>
    public int EarlySignalMinRisingDays { get; init; }

    /// <summary>
    /// 早期シグナルとして許容する、MACD線とシグナル線の乖離幅の上限（ATR14正規化）。
    /// これを超える乖離はクロスまで遠すぎるとみなし早期シグナル扱いしない。
    /// </summary>
    public decimal EarlySignalMaxGapAtrMultiple { get; init; }

    /// <summary>
    /// Short の新規候補を出すか。バックテスト（2022-08〜2026-10）で全期間・前半後半ともマイナスだったため既定は false。
    /// 保有中の Short ポジションの再評価には影響しない。
    /// </summary>
    public bool ShortCandidatesEnabled { get; init; }
}

public sealed class ScoringParameters
{
    public int MacdFreshnessPoints { get; init; }
    public int MacdPositionPoints { get; init; }
    public int MacdMomentumPoints { get; init; }
    public int TrendStrengthPoints { get; init; }
    public int VolumePoints { get; init; }
    public int MarketRegimePoints { get; init; }
    public decimal TrendStrengthFullScoreAtrMultiple { get; init; }
    public decimal MomentumFullScoreAtrMultiple { get; init; }
    public decimal VolumeRatioZeroScore { get; init; }
    public decimal VolumeRatioFullScore { get; init; }
    public int HighConfidenceThreshold { get; init; }
    public int MediumConfidenceThreshold { get; init; }

    /// <summary>
    /// 早期シグナル（未クロス）のMACD鮮度スコアに掛ける係数。確定シグナルより確信度が低いことを表すため
    /// MacdFreshnessPointsに対して頭打ちする（クロス日基準の経過日数という概念が早期シグナルには無いため）。
    /// </summary>
    public decimal EarlySignalFreshnessScoreCapRatio { get; init; }
}

public sealed class RiskParameters
{
    public decimal LongStopLossAtrMultiple { get; init; }
    public decimal ShortStopLossAtrMultiple { get; init; }
    public decimal PartialTakeProfitRMultiple { get; init; }
    public decimal PartialTakeProfitRatio { get; init; }
    public int TimeStopBusinessDays { get; init; }
    public int MarginDueWarningBusinessDays { get; init; }
}

public sealed class AnalysisWindowParameters
{
    public int BarsToFetch { get; init; }
    public int MinimumRequiredBars { get; init; }
}
