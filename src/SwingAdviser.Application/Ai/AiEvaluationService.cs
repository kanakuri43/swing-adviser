using Microsoft.EntityFrameworkCore;
using SwingAdviser.Application.Analysis;
using SwingAdviser.Application.Common;
using SwingAdviser.Domain.Ai;
using SwingAdviser.Domain.Common;
using SwingAdviser.Infrastructure.Analysis;
using SwingAdviser.Infrastructure.Persistence;

namespace SwingAdviser.Application.Ai;

public sealed record AiEvaluationTarget(
    string StockCode,
    TradeDirection? Direction,
    string? StockName = null,
    decimal? LatestClose = null,
    DateOnly? LatestCloseDate = null,
    int? Score = null,
    ConfidenceLevel? Confidence = null);

public sealed record AiEvaluationProgress(int Completed, int Total);

public sealed record AiEvaluationRunResult(int SucceededCount, int FailedCount);

/// <summary>
/// AI総合評価の実行。状態はPending→Running→Succeeded|Failedの4状態のみ（terraの9状態は過剰）。
/// 1件ごとに独立したDbContextとtry/catchで処理し、1件の失敗が他候補の実行・テクニカル結果を無効化しない
/// （CLAUDE.md「AI総合評価」節）。並列数はexecutor（<see cref="IAiCliExecutor"/>）内部のセマフォが制御する。
/// </summary>
public sealed class AiEvaluationService(
    IAiCliExecutor executor,
    IDbContextFactory<SwingAdviserDbContext> contextFactory,
    TimeProvider timeProvider)
{
    public async Task<AiEvaluationRunResult> RunAsync(
        IReadOnlyList<AiEvaluationTarget> targets,
        IProgress<AiEvaluationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targets);
        if (targets.Count == 0)
        {
            return new AiEvaluationRunResult(0, 0);
        }

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var evaluations = targets.Select(t => AiEvaluation.Create(t.StockCode, t.Direction, nowUtc)).ToList();

        await using (var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            context.AiEvaluations.AddRange(evaluations);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        var total = evaluations.Count;
        var completed = 0;
        progress?.Report(new AiEvaluationProgress(0, total));

        var tasks = evaluations.Zip(targets, (evaluation, target) => (evaluation, target)).Select(async pair =>
        {
            var succeeded = await ExecuteOneAsync(pair.evaluation.Id, pair.target, cancellationToken)
                .ConfigureAwait(false);
            var done = Interlocked.Increment(ref completed);
            progress?.Report(new AiEvaluationProgress(done, total));
            return succeeded;
        });

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        return new AiEvaluationRunResult(results.Count(r => r), results.Count(r => !r));
    }

    /// <summary>
    /// 日次更新後の自動AI評価対象を選ぶ純粋関数。信頼度Highのみ、実行中（Pending/Running）のものを除き、
    /// 同一評価日以降にすでに成功済みのものは再実行しない（1日に複数回更新した場合の重複実行防止）。
    /// </summary>
    public static IReadOnlyList<AiEvaluationTarget> SelectAutoTargets(IReadOnlyList<CandidateOverview> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var targets = new List<AiEvaluationTarget>();
        foreach (var candidate in candidates)
        {
            if (candidate.Confidence != ConfidenceLevel.High)
            {
                continue;
            }

            if (candidate.AiStatus is AiEvaluationStatus.Pending or AiEvaluationStatus.Running)
            {
                continue;
            }

            if (candidate.AiStatus == AiEvaluationStatus.Succeeded
                && candidate.AiRequestedAtUtc is { } requestedAtUtc
                && DateOnly.FromDateTime(Jst.ToJst(requestedAtUtc)) >= candidate.EvaluationDate)
            {
                continue;
            }

            targets.Add(new AiEvaluationTarget(
                candidate.StockCode,
                candidate.Direction,
                candidate.StockName,
                candidate.Close,
                candidate.EvaluationDate,
                candidate.Score,
                candidate.Confidence));
        }

        return targets;
    }

    /// <summary>起動時に残っているPending/Runningを中断扱いでFailedにする。呼び出しの配線はPresentation層。</summary>
    public async Task<int> RecoverInterruptedAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var interrupted = await context.AiEvaluations
            .Where(e => e.Status == AiEvaluationStatus.Pending || e.Status == AiEvaluationStatus.Running)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        if (interrupted.Count == 0)
        {
            return 0;
        }

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        foreach (var evaluation in interrupted)
        {
            evaluation.MarkFailed("前回起動時に中断されました。", nowUtc);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return interrupted.Count;
    }

    private async Task<bool> ExecuteOneAsync(long evaluationId, AiEvaluationTarget target, CancellationToken cancellationToken)
    {
        try
        {
            await TransitionAsync(evaluationId, evaluation => evaluation.MarkRunning(timeProvider.GetUtcNow().UtcDateTime))
                .ConfigureAwait(false);

            var prompt = AiPromptBuilder.Build(new AiPromptBuilder.PromptContext(
                target.StockCode,
                target.StockName ?? target.StockCode,
                target.Direction,
                target.LatestClose,
                target.LatestCloseDate,
                target.Score,
                target.Confidence));
            var result = await executor.ExecuteAsync(prompt, cancellationToken).ConfigureAwait(false);

            var failureReason = ClassifyFailure(result);
            AiResponseParseResult? parsed = null;
            if (failureReason is null)
            {
                parsed = AiResponseParser.Parse(result.Output);
                if (!parsed.Success)
                {
                    failureReason = $"応答の解析に失敗しました: {parsed.FailureReason}";
                }
            }

            if (failureReason is not null)
            {
                await TransitionAsync(evaluationId, evaluation => evaluation.MarkFailed(failureReason, timeProvider.GetUtcNow().UtcDateTime))
                    .ConfigureAwait(false);
                return false;
            }

            await TransitionAsync(evaluationId, evaluation => evaluation.MarkSucceeded(
                parsed!.Verdict,
                parsed.Confidence,
                parsed.Summary,
                parsed.PositiveFactors,
                parsed.RiskFactors,
                parsed.InvalidationConditions,
                timeProvider.GetUtcNow().UtcDateTime)).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            await MarkFailedSafelyAsync(evaluationId, "キャンセルされました。").ConfigureAwait(false);
            return false;
        }
        catch (Exception exception)
        {
            await MarkFailedSafelyAsync(evaluationId, $"予期しないエラーが発生しました: {Truncate(exception.Message, 500)}").ConfigureAwait(false);
            return false;
        }
    }

    private static string? ClassifyFailure(AiCliResult result) => result.Completion switch
    {
        AiCliCompletion.FailedToStart => "Codex CLIの起動に失敗しました。",
        AiCliCompletion.TimedOut => "Codex CLIの実行がtimeoutしました。",
        AiCliCompletion.Cancelled => "キャンセルされました。",
        AiCliCompletion.Completed when result.ExitCode is not 0 =>
            $"Codex CLIが異常終了しました(ExitCode={result.ExitCode}): {Truncate(result.ErrorOutput, 500)}",
        _ => null,
    };

    private async Task TransitionAsync(long evaluationId, Action<AiEvaluation> transition)
    {
        await using var context = await contextFactory.CreateDbContextAsync(CancellationToken.None).ConfigureAwait(false);
        var evaluation = await context.AiEvaluations.FindAsync([evaluationId], CancellationToken.None).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"AiEvaluation Id={evaluationId} が見つかりません。");
        transition(evaluation);
        await context.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private async Task MarkFailedSafelyAsync(long evaluationId, string reason)
    {
        try
        {
            await using var context = await contextFactory.CreateDbContextAsync(CancellationToken.None).ConfigureAwait(false);
            var evaluation = await context.AiEvaluations.FindAsync([evaluationId], CancellationToken.None).ConfigureAwait(false);
            if (evaluation is null || evaluation.Status is AiEvaluationStatus.Succeeded or AiEvaluationStatus.Failed)
            {
                return;
            }

            evaluation.MarkFailed(reason, timeProvider.GetUtcNow().UtcDateTime);
            await context.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // 保存できなくても呼び出し元の結果には影響させない。RecoverInterruptedAsyncが次回起動時に拾う。
        }
    }

    private static string Truncate(string value, int maxLength) => value.Length <= maxLength ? value : value[..maxLength];
}
