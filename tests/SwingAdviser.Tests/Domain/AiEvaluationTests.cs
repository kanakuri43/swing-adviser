using SwingAdviser.Domain.Ai;
using SwingAdviser.Domain.Common;

namespace SwingAdviser.Tests.Domain;

public class AiEvaluationTests
{
    private static readonly DateTime NowUtc = new(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void PendingToRunningToSucceeded_Allowed()
    {
        var evaluation = AiEvaluation.Create("7203", TradeDirection.Long, NowUtc);

        evaluation.MarkRunning(NowUtc.AddMinutes(1));
        evaluation.MarkSucceeded(
            AiVerdict.Bullish,
            ConfidenceLevel.High,
            "summary",
            ["好材料"],
            ["リスク"],
            ["無効化条件"],
            NowUtc.AddMinutes(2));

        Assert.Equal(AiEvaluationStatus.Succeeded, evaluation.Status);
        Assert.Equal(AiVerdict.Bullish, evaluation.Verdict);
        Assert.Single(evaluation.PositiveFactors);
    }

    [Fact]
    public void PendingToFailed_Allowed()
    {
        var evaluation = AiEvaluation.Create("7203", null, NowUtc);

        evaluation.MarkFailed("timeout", NowUtc.AddMinutes(1));

        Assert.Equal(AiEvaluationStatus.Failed, evaluation.Status);
    }

    [Fact]
    public void PendingToSucceeded_Throws()
    {
        var evaluation = AiEvaluation.Create("7203", null, NowUtc);

        Assert.Throws<InvalidOperationException>(() =>
            evaluation.MarkSucceeded(AiVerdict.Neutral, ConfidenceLevel.Medium, "s", [], [], [], NowUtc));
    }

    [Theory]
    [InlineData(AiEvaluationStatus.Succeeded)]
    [InlineData(AiEvaluationStatus.Failed)]
    public void TerminalStates_RejectFurtherTransitions(AiEvaluationStatus terminalStatus)
    {
        var evaluation = AiEvaluation.Create("7203", null, NowUtc);
        evaluation.MarkRunning(NowUtc);

        if (terminalStatus == AiEvaluationStatus.Succeeded)
        {
            evaluation.MarkSucceeded(AiVerdict.Neutral, ConfidenceLevel.Medium, "s", [], [], [], NowUtc);
        }
        else
        {
            evaluation.MarkFailed("err", NowUtc);
        }

        Assert.Throws<InvalidOperationException>(() => evaluation.MarkRunning(NowUtc));
    }
}
