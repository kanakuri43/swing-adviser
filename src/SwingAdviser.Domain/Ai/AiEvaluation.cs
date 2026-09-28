using SwingAdviser.Domain.Common;

namespace SwingAdviser.Domain.Ai;

/// <summary>
/// Codex CLI総合評価の状態遷移。Pending→Running→Succeeded|Failedの4状態のみ（terraの9状態は過剰）。
/// Succeeded/Failedは終端で、再実行は新しい行を作る。
/// </summary>
public class AiEvaluation
{
    private readonly List<string> _positiveFactors = [];
    private readonly List<string> _riskFactors = [];
    private readonly List<string> _invalidationConditions = [];

    private AiEvaluation()
    {
    } // EF Core

    private AiEvaluation(string stockCode, TradeDirection? direction, DateTime requestedAtUtc)
    {
        StockCode = stockCode;
        Direction = direction;
        Status = AiEvaluationStatus.Pending;
        RequestedAtUtc = requestedAtUtc;
    }

    public long Id { get; private set; }

    public string StockCode { get; private set; } = string.Empty;

    public TradeDirection? Direction { get; private set; }

    public AiEvaluationStatus Status { get; private set; }

    public DateTime RequestedAtUtc { get; private set; }

    public DateTime? StartedAtUtc { get; private set; }

    public DateTime? CompletedAtUtc { get; private set; }

    public AiVerdict? Verdict { get; private set; }

    public ConfidenceLevel? Confidence { get; private set; }

    public string? Summary { get; private set; }

    public string? ErrorMessage { get; private set; }

    public IReadOnlyList<string> PositiveFactors => _positiveFactors;

    public IReadOnlyList<string> RiskFactors => _riskFactors;

    public IReadOnlyList<string> InvalidationConditions => _invalidationConditions;

    public static AiEvaluation Create(string stockCode, TradeDirection? direction, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(stockCode))
        {
            throw new ArgumentException("証券コードは必須です。", nameof(stockCode));
        }

        return new AiEvaluation(stockCode, direction, nowUtc);
    }

    public void MarkRunning(DateTime nowUtc)
    {
        if (Status != AiEvaluationStatus.Pending)
        {
            throw new InvalidOperationException($"Pending からのみ Running にできます（現在: {Status}）。");
        }

        Status = AiEvaluationStatus.Running;
        StartedAtUtc = nowUtc;
    }

    public void MarkSucceeded(
        AiVerdict verdict,
        ConfidenceLevel confidence,
        string summary,
        IEnumerable<string> positiveFactors,
        IEnumerable<string> riskFactors,
        IEnumerable<string> invalidationConditions,
        DateTime nowUtc)
    {
        if (Status != AiEvaluationStatus.Running)
        {
            throw new InvalidOperationException($"Running からのみ Succeeded にできます（現在: {Status}）。");
        }

        Status = AiEvaluationStatus.Succeeded;
        Verdict = verdict;
        Confidence = confidence;
        Summary = summary;
        _positiveFactors.AddRange(positiveFactors);
        _riskFactors.AddRange(riskFactors);
        _invalidationConditions.AddRange(invalidationConditions);
        CompletedAtUtc = nowUtc;
    }

    public void MarkFailed(string errorMessage, DateTime nowUtc)
    {
        if (Status is not (AiEvaluationStatus.Pending or AiEvaluationStatus.Running))
        {
            throw new InvalidOperationException($"Pending または Running からのみ Failed にできます（現在: {Status}）。");
        }

        Status = AiEvaluationStatus.Failed;
        ErrorMessage = errorMessage;
        CompletedAtUtc = nowUtc;
    }
}
