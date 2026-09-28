using SwingAdviser.Application.Ai;
using SwingAdviser.Domain.Ai;
using SwingAdviser.Domain.Common;
using SwingAdviser.Infrastructure.Analysis;
using SwingAdviser.Tests.Application.TestSupport;

namespace SwingAdviser.Tests.Application.Ai;

/// <summary>1件の失敗が他候補の実行を無効化しないこと（CLAUDE.md「AI総合評価」節）を中心に検証する。</summary>
public class AiEvaluationServiceTests
{
    private static readonly DateTimeOffset NowUtc = new(2026, 1, 5, 0, 0, 0, TimeSpan.Zero);

    private const string SuccessJson = """{ "verdict": "Bullish", "confidence": "High", "summary": "良好な見通し。" }""";

    [Fact]
    public async Task RunAsync_OneOfThreeFails_OthersStillSucceed()
    {
        using var contextFactory = new SqliteInMemoryContextFactory();
        var timeProvider = new FixedTimeProvider(NowUtc);
        var executor = new FakeAiCliExecutor((prompt, _) => Task.FromResult(
            prompt.Contains("銘柄コード: 2222")
                ? new AiCliResult(string.Empty, "何らかのエラー", 1, AiCliCompletion.Completed)
                : new AiCliResult(SuccessJson, string.Empty, 0, AiCliCompletion.Completed)));

        var service = new AiEvaluationService(executor, contextFactory, timeProvider);
        var targets = new[]
        {
            new AiEvaluationTarget("1111", TradeDirection.Long),
            new AiEvaluationTarget("2222", TradeDirection.Long),
            new AiEvaluationTarget("3333", TradeDirection.Short),
        };

        var result = await service.RunAsync(targets);

        Assert.Equal(2, result.SucceededCount);
        Assert.Equal(1, result.FailedCount);

        await using var context = contextFactory.CreateDbContext();
        var evaluations = context.AiEvaluations.ToList();
        Assert.Equal(3, evaluations.Count);
        Assert.All(evaluations.Where(e => e.StockCode != "2222"), e => Assert.Equal(AiEvaluationStatus.Succeeded, e.Status));

        var failed = evaluations.Single(e => e.StockCode == "2222");
        Assert.Equal(AiEvaluationStatus.Failed, failed.Status);
        Assert.Contains("異常終了", failed.ErrorMessage);
    }

    [Fact]
    public async Task RunAsync_TimedOut_MarksFailedWithReason()
    {
        using var contextFactory = new SqliteInMemoryContextFactory();
        var timeProvider = new FixedTimeProvider(NowUtc);
        var executor = new FakeAiCliExecutor((_, _) =>
            Task.FromResult(new AiCliResult(string.Empty, string.Empty, null, AiCliCompletion.TimedOut)));
        var service = new AiEvaluationService(executor, contextFactory, timeProvider);

        var result = await service.RunAsync([new AiEvaluationTarget("1111", null)]);

        Assert.Equal(0, result.SucceededCount);
        Assert.Equal(1, result.FailedCount);

        await using var context = contextFactory.CreateDbContext();
        var evaluation = context.AiEvaluations.Single();
        Assert.Equal(AiEvaluationStatus.Failed, evaluation.Status);
        Assert.Contains("timeout", evaluation.ErrorMessage);
    }

    [Fact]
    public async Task RunAsync_ExecutorReturnsCancelled_MarksFailed()
    {
        using var contextFactory = new SqliteInMemoryContextFactory();
        var timeProvider = new FixedTimeProvider(NowUtc);
        var executor = new FakeAiCliExecutor((_, _) =>
            Task.FromResult(new AiCliResult(string.Empty, string.Empty, null, AiCliCompletion.Cancelled)));
        var service = new AiEvaluationService(executor, contextFactory, timeProvider);

        await service.RunAsync([new AiEvaluationTarget("1111", null)]);

        await using var context = contextFactory.CreateDbContext();
        var evaluation = context.AiEvaluations.Single();
        Assert.Equal(AiEvaluationStatus.Failed, evaluation.Status);
        Assert.Contains("キャンセル", evaluation.ErrorMessage);
    }

    [Fact]
    public async Task RunAsync_UnparsableResponse_MarksFailed()
    {
        using var contextFactory = new SqliteInMemoryContextFactory();
        var timeProvider = new FixedTimeProvider(NowUtc);
        var executor = new FakeAiCliExecutor((_, _) =>
            Task.FromResult(new AiCliResult("これはJSONではありません", string.Empty, 0, AiCliCompletion.Completed)));
        var service = new AiEvaluationService(executor, contextFactory, timeProvider);

        await service.RunAsync([new AiEvaluationTarget("1111", null)]);

        await using var context = contextFactory.CreateDbContext();
        var evaluation = context.AiEvaluations.Single();
        Assert.Equal(AiEvaluationStatus.Failed, evaluation.Status);
        Assert.Contains("解析に失敗", evaluation.ErrorMessage);
    }

    [Fact]
    public async Task RecoverInterruptedAsync_MarksPendingAndRunningAsFailed_LeavesSucceededAlone()
    {
        using var contextFactory = new SqliteInMemoryContextFactory();
        var timeProvider = new FixedTimeProvider(NowUtc);

        await using (var context = contextFactory.CreateDbContext())
        {
            var pending = AiEvaluation.Create("1111", null, NowUtc.UtcDateTime);
            var running = AiEvaluation.Create("2222", null, NowUtc.UtcDateTime);
            running.MarkRunning(NowUtc.UtcDateTime);
            var succeeded = AiEvaluation.Create("3333", null, NowUtc.UtcDateTime);
            succeeded.MarkRunning(NowUtc.UtcDateTime);
            succeeded.MarkSucceeded(AiVerdict.Neutral, ConfidenceLevel.Medium, "ok", [], [], [], [], NowUtc.UtcDateTime);

            context.AiEvaluations.AddRange(pending, running, succeeded);
            await context.SaveChangesAsync();
        }

        var executor = new FakeAiCliExecutor((_, _) => throw new InvalidOperationException("呼ばれないはず。"));
        var service = new AiEvaluationService(executor, contextFactory, timeProvider);

        var recovered = await service.RecoverInterruptedAsync();

        Assert.Equal(2, recovered);
        await using var verifyContext = contextFactory.CreateDbContext();
        var evaluations = verifyContext.AiEvaluations.ToList();
        Assert.Equal(2, evaluations.Count(e => e.Status == AiEvaluationStatus.Failed));
        Assert.Equal(1, evaluations.Count(e => e.Status == AiEvaluationStatus.Succeeded));
    }
}
