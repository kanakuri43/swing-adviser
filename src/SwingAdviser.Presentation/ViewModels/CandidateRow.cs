using SwingAdviser.Application.Analysis;
using SwingAdviser.Application.Common;
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
        RunAiEvaluationCommand = new AsyncRelayCommand(ct => runAiEvaluation(this, ct), canExecute: () => !IsAiEvaluationRunning);
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

    /// <summary>候補にAI評価が実行中（待機中含む）かどうか。DB上の状態を唯一の情報源とし、
    /// 手動実行・日次更新後の自動実行のどちらであっても他行の再読込に影響されず正しく反映される。</summary>
    public bool IsAiEvaluationRunning => Overview.AiStatus is AiEvaluationStatus.Pending or AiEvaluationStatus.Running;

    /// <summary>DataTriggerでの色分け用（enum名そのまま）。</summary>
    public string AiVerdictName => Overview.AiVerdict?.ToString() ?? "None";

    public string AiVerdictText => Overview.AiVerdict switch
    {
        AiVerdict.Bullish => "Bullish（強気）",
        AiVerdict.Neutral => "Neutral（中立）",
        AiVerdict.Bearish => "Bearish（弱気）",
        _ => "—",
    };

    public string AiConfidenceText => Overview.AiConfidence?.ToString() ?? "—";

    public string? AiSummary => Overview.AiSummary;

    public string? AiErrorMessage => Overview.AiErrorMessage;

    public bool HasAiFailure => Overview.AiStatus == AiEvaluationStatus.Failed;

    public string AiEvaluatedAtJstText => Overview.AiRequestedAtUtc is { } requestedAtUtc
        ? Jst.ToJst(requestedAtUtc).ToString("yyyy-MM-dd HH:mm")
        : "—";

    /// <summary>詳細パネルの状態行。未実行／実行中／失敗理由のいずれかを1行で表す（成功時はnull）。</summary>
    public string? AiStateMessage => Overview.AiStatus switch
    {
        null => "AI評価はまだ実行されていません。",
        AiEvaluationStatus.Pending => "AI評価は待機中です…",
        AiEvaluationStatus.Running => "AI評価を実行中です…",
        AiEvaluationStatus.Failed => $"AI評価に失敗しました: {Overview.AiErrorMessage}",
        _ => null,
    };

    public bool HasAiStateMessage => AiStateMessage is not null;

    public IReadOnlyList<string> AiPositiveFactors => Overview.AiPositiveFactors;

    public IReadOnlyList<string> AiRiskFactors => Overview.AiRiskFactors;

    public IReadOnlyList<string> AiInvalidationConditions => Overview.AiInvalidationConditions;

    public bool HasAiResult => Overview.AiStatus == AiEvaluationStatus.Succeeded;

    public AsyncRelayCommand RunAiEvaluationCommand { get; }
}
