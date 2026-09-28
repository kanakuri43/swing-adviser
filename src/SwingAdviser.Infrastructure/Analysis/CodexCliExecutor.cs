using System.ComponentModel;
using System.Diagnostics;
using SwingAdviser.Infrastructure.Configuration;

namespace SwingAdviser.Infrastructure.Analysis;

public enum AiCliCompletion
{
    Completed,
    TimedOut,
    Cancelled,
    FailedToStart,
}

public sealed record AiCliResult(string Output, string ErrorOutput, int? ExitCode, AiCliCompletion Completion);

public interface IAiCliExecutor
{
    Task<AiCliResult> ExecuteAsync(string prompt, CancellationToken cancellationToken = default);
}

/// <summary>
/// Codex CLI (`codex exec`) を外部プロセスとして実行する。起動失敗・timeout・非ゼロ終了は例外にせず
/// <see cref="AiCliResult"/> として返す。1件の失敗が呼び出し側の複数候補ループを止めないための設計。
/// </summary>
public sealed class CodexCliExecutor : IAiCliExecutor
{
    private readonly string _executablePath;
    private readonly TimeSpan _timeout;
    private readonly SemaphoreSlim _concurrencyGate;
    private readonly string? _reasoningEffort;

    public CodexCliExecutor(CodexCliOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.TimeoutSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "CodexCli.TimeoutSeconds は正の値である必要があります。");
        }

        if (options.MaxParallelism <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "CodexCli.MaxParallelism は正の値である必要があります。");
        }

        _executablePath = string.IsNullOrWhiteSpace(options.ExecutablePath)
            ? CodexCliPathResolver.Resolve()
            : options.ExecutablePath;
        _timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        _concurrencyGate = new SemaphoreSlim(options.MaxParallelism);
        _reasoningEffort = string.IsNullOrWhiteSpace(options.ReasoningEffort) ? null : options.ReasoningEffort;
    }

    public async Task<AiCliResult> ExecuteAsync(string prompt, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        await _concurrencyGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var finalMessagePath = Path.Combine(Path.GetTempPath(), $"swing-adviser-codex-{Guid.NewGuid():N}.txt");
            var startInfo = BuildStartInfo(
                _executablePath,
                prompt,
                finalMessagePath,
                Environment.GetEnvironmentVariable("HOME"),
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                _reasoningEffort);
            return await RunAsync(startInfo, _timeout, finalMessagePath, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _concurrencyGate.Release();
        }
    }

    /// <summary>プロセス引数・環境変数の組み立てのみを行う純粋関数（テスト用に公開）。</summary>
    public static ProcessStartInfo BuildStartInfo(
        string executablePath,
        string prompt,
        string finalMessagePath,
        string? homeDirectory,
        string? userProfileDirectory,
        string? reasoningEffort = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        // デスクトップホストはHOMEを継承しないことがある。Codex CLIはHOMEで認証設定(~/.codex)を探すため、
        // このプロセスにだけ明示的に補う（terraで実際に必要だった回避策）。
        var resolvedHome = !string.IsNullOrWhiteSpace(homeDirectory) ? homeDirectory : userProfileDirectory;
        if (!string.IsNullOrWhiteSpace(resolvedHome))
        {
            startInfo.Environment["HOME"] = resolvedHome;
            var codexHome = Path.Combine(resolvedHome, ".codex");
            if (Directory.Exists(codexHome))
            {
                startInfo.Environment["CODEX_HOME"] = codexHome;
            }
        }

        startInfo.ArgumentList.Add("exec");
        startInfo.ArgumentList.Add("--ignore-user-config");
        startInfo.ArgumentList.Add("--skip-git-repo-check");
        startInfo.ArgumentList.Add("--sandbox");
        startInfo.ArgumentList.Add("read-only");
        startInfo.ArgumentList.Add("--ephemeral");
        if (!string.IsNullOrWhiteSpace(reasoningEffort))
        {
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add($"model_reasoning_effort={reasoningEffort}");
        }

        startInfo.ArgumentList.Add("-o");
        startInfo.ArgumentList.Add(finalMessagePath);
        startInfo.ArgumentList.Add(prompt);
        return startInfo;
    }

    /// <summary>プロセスの起動・timeout・キャンセルの制御のみを行う（テスト用に公開）。</summary>
    public static async Task<AiCliResult> RunAsync(
        ProcessStartInfo startInfo,
        TimeSpan timeout,
        string? finalMessagePath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(startInfo);

        Process? process = null;
        try
        {
            process = new Process { StartInfo = startInfo };
            if (!process.Start())
            {
                return new AiCliResult(string.Empty, "The CLI process did not start.", null, AiCliCompletion.FailedToStart);
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);

            using var timeoutCts = new CancellationTokenSource(timeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            try
            {
                await process.WaitForExitAsync(linkedCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }

                await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
                var completion = timeoutCts.IsCancellationRequested ? AiCliCompletion.TimedOut : AiCliCompletion.Cancelled;
                return new AiCliResult(
                    ReadFinalMessageOrFallback(finalMessagePath, await stdoutTask.ConfigureAwait(false)),
                    await stderrTask.ConfigureAwait(false),
                    process.HasExited ? process.ExitCode : null,
                    completion);
            }

            await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
            return new AiCliResult(
                ReadFinalMessageOrFallback(finalMessagePath, await stdoutTask.ConfigureAwait(false)),
                await stderrTask.ConfigureAwait(false),
                process.ExitCode,
                AiCliCompletion.Completed);
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            return new AiCliResult(string.Empty, exception.Message, null, AiCliCompletion.FailedToStart);
        }
        finally
        {
            process?.Dispose();
            if (!string.IsNullOrWhiteSpace(finalMessagePath))
            {
                try
                {
                    if (File.Exists(finalMessagePath))
                    {
                        File.Delete(finalMessagePath);
                    }
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
    }

    private static string ReadFinalMessageOrFallback(string? path, string fallback)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return fallback;
        }

        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : fallback;
        }
        catch (IOException)
        {
            return fallback;
        }
        catch (UnauthorizedAccessException)
        {
            return fallback;
        }
    }
}
