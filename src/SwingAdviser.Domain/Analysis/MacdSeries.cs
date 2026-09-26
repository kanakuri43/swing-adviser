namespace SwingAdviser.Domain.Analysis;

/// <summary>MACD線・シグナル線・ヒストグラムの3系列。各配列は入力と同じ長さで、未定義区間は0。</summary>
public sealed record MacdSeries(decimal[] Line, decimal[] Signal, decimal[] Histogram);
