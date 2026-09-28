using SwingAdviser.Application.Analysis;
using SwingAdviser.Domain.Ai;
using SwingAdviser.Domain.Common;

namespace SwingAdviser.Presentation.ViewModels;

/// <summary>
/// 候補タブの1行。AI評価トリガーは自分自身の<see cref="AsyncRelayCommand"/>として持つ
/// （多重実行防止はコマンド自身のIsRunningで完結する。行の再構築は親ViewModelの全体リロードに任せる）。
/// </summary>
public sealed class CandidateRow
{
    public CandidateRow(CandidateOverview overview, Func<CandidateRow, CancellationToken, Task> runAiEvaluation)
    {
        Overview = overview;
        RunAiEvaluationCommand = new AsyncRelayCommand(ct => runAiEvaluation(this, ct));
    }

    public CandidateOverview Overview { get; }

    public string StockCode => Overview.StockCode;

    public string StockName => Overview.StockName;

    public string Direction => Overview.Direction == TradeDirection.Long ? "Long" : "Short";

    public int Score => Overview.Score;

    public string Confidence => Overview.Confidence.ToString();

    public decimal Close => Overview.Close;

    public decimal Atr14 => Overview.Atr14;

    public decimal VolumeRatio => Overview.VolumeRatio;

    public bool MarketRegimeAligned => Overview.MarketRegimeAligned;

    public decimal ReferenceStopLossPrice => Overview.ReferenceStopLossPrice;

    public decimal MacdLine => Overview.MacdLine;

    public decimal MacdSignal => Overview.MacdSignal;

    public decimal MacdHistogram => Overview.MacdHistogram;

    public decimal Ema20 => Overview.Ema20;

    public decimal Ema100 => Overview.Ema100;

    public string AiStatusText => Overview.AiStatus switch
    {
        null => "未実行",
        AiEvaluationStatus.Pending => "待機中",
        AiEvaluationStatus.Running => "実行中",
        AiEvaluationStatus.Succeeded => "成功",
        AiEvaluationStatus.Failed => "失敗",
        _ => Overview.AiStatus.ToString() ?? "未実行",
    };

    public string AiVerdictText => Overview.AiVerdict?.ToString() ?? "—";

    public string? AiSummary => Overview.AiSummary;

    public IReadOnlyList<string> AiPositiveFactors => Overview.AiPositiveFactors;

    public IReadOnlyList<string> AiRiskFactors => Overview.AiRiskFactors;

    public IReadOnlyList<string> AiInvalidationConditions => Overview.AiInvalidationConditions;

    public IReadOnlyList<string> AiReferenceUrls => Overview.AiReferenceUrls;

    public bool HasAiResult => Overview.AiStatus == AiEvaluationStatus.Succeeded;

    public AsyncRelayCommand RunAiEvaluationCommand { get; }
}
