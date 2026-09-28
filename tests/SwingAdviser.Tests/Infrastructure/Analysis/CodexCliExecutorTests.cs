using System.Diagnostics;
using SwingAdviser.Infrastructure.Analysis;
using SwingAdviser.Infrastructure.Configuration;

namespace SwingAdviser.Tests.Infrastructure.Analysis;

public class CodexCliExecutorTests
{
    private static readonly string CmdExePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");

    private static readonly string PowerShellExePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");

    [Fact]
    public void BuildStartInfo_SetsExecFlagsAndPromptAsLastArgument()
    {
        var startInfo = CodexCliExecutor.BuildStartInfo(
            executablePath: "codex.exe",
            prompt: "この銘柄を調べて",
            finalMessagePath: @"C:\temp\final.txt",
            homeDirectory: null,
            userProfileDirectory: null);

        Assert.Equal("codex.exe", startInfo.FileName);
        Assert.False(startInfo.UseShellExecute);
        Assert.True(startInfo.RedirectStandardOutput);
        Assert.True(startInfo.RedirectStandardError);
        Assert.True(startInfo.CreateNoWindow);
        Assert.Equal(
            ["exec", "--ignore-user-config", "--skip-git-repo-check", "--sandbox", "read-only", "--ephemeral", "-o", @"C:\temp\final.txt", "この銘柄を調べて"],
            startInfo.ArgumentList);
    }

    [Fact]
    public void BuildStartInfo_PromptContainingSpacesAndQuotes_RemainsOneArgument()
    {
        const string prompt = "銘柄コード \"7203\" を調べて スペース入り";

        var startInfo = CodexCliExecutor.BuildStartInfo("codex.exe", prompt, "final.txt", null, null);

        Assert.Equal(prompt, startInfo.ArgumentList[^1]);
    }

    [Fact]
    public void BuildStartInfo_UsesHomeDirectory_WhenProvided()
    {
        var startInfo = CodexCliExecutor.BuildStartInfo("codex.exe", "prompt", "final.txt", homeDirectory: @"C:\home", userProfileDirectory: @"C:\profile");

        Assert.Equal(@"C:\home", startInfo.Environment["HOME"]);
    }

    [Fact]
    public void BuildStartInfo_FallsBackToUserProfile_WhenHomeDirectoryMissing()
    {
        var startInfo = CodexCliExecutor.BuildStartInfo("codex.exe", "prompt", "final.txt", homeDirectory: null, userProfileDirectory: @"C:\profile");

        Assert.Equal(@"C:\profile", startInfo.Environment["HOME"]);
    }

    [Fact]
    public void BuildStartInfo_SetsCodexHome_OnlyWhenDotCodexDirectoryExists()
    {
        var home = Directory.CreateTempSubdirectory("swing-adviser-home-");
        try
        {
            var withoutDotCodex = CodexCliExecutor.BuildStartInfo("codex.exe", "prompt", "final.txt", home.FullName, null);
            Assert.False(withoutDotCodex.Environment.ContainsKey("CODEX_HOME"));

            var dotCodex = Directory.CreateDirectory(Path.Combine(home.FullName, ".codex"));
            var withDotCodex = CodexCliExecutor.BuildStartInfo("codex.exe", "prompt", "final.txt", home.FullName, null);
            Assert.Equal(dotCodex.FullName, withDotCodex.Environment["CODEX_HOME"]);
        }
        finally
        {
            home.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_NonexistentExecutable_ReturnsFailedToStartWithoutThrowing()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(Path.GetTempPath(), $"no-such-executable-{Guid.NewGuid():N}.exe"),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        var result = await CodexCliExecutor.RunAsync(startInfo, TimeSpan.FromSeconds(5), null, CancellationToken.None);

        Assert.Equal(AiCliCompletion.FailedToStart, result.Completion);
        Assert.Null(result.ExitCode);
    }

    [Fact]
    public async Task RunAsync_ProcessExitsNonZero_ReturnsCompletedWithExitCode()
    {
        var startInfo = BuildCmdStartInfo("/c", "exit", "3");

        var result = await CodexCliExecutor.RunAsync(startInfo, TimeSpan.FromSeconds(10), null, CancellationToken.None);

        Assert.Equal(AiCliCompletion.Completed, result.Completion);
        Assert.Equal(3, result.ExitCode);
    }

    [Fact]
    public async Task RunAsync_NoFinalMessageFile_FallsBackToStdout()
    {
        var startInfo = BuildCmdStartInfo("/c", "echo", "hello-from-stdout");

        var result = await CodexCliExecutor.RunAsync(startInfo, TimeSpan.FromSeconds(10), finalMessagePath: null, CancellationToken.None);

        Assert.Equal(AiCliCompletion.Completed, result.Completion);
        Assert.Contains("hello-from-stdout", result.Output);
    }

    [Fact]
    public async Task RunAsync_FinalMessageFileExists_PrefersFileContentOverStdout()
    {
        var finalMessagePath = Path.Combine(Path.GetTempPath(), $"swing-adviser-test-final-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(finalMessagePath, "final-message-content");
        try
        {
            var startInfo = BuildCmdStartInfo("/c", "echo", "stdout-noise");

            var result = await CodexCliExecutor.RunAsync(startInfo, TimeSpan.FromSeconds(10), finalMessagePath, CancellationToken.None);

            Assert.Equal("final-message-content", result.Output);
            Assert.False(File.Exists(finalMessagePath), "RunAsyncは一時ファイルを削除するはず。");
        }
        finally
        {
            if (File.Exists(finalMessagePath))
            {
                File.Delete(finalMessagePath);
            }
        }
    }

    [Fact]
    public async Task RunAsync_ProcessExceedsTimeout_KillsProcessAndReturnsTimedOutPromptly()
    {
        var startInfo = BuildPowerShellSleepStartInfo(seconds: 5);
        var stopwatch = Stopwatch.StartNew();

        var result = await CodexCliExecutor.RunAsync(startInfo, TimeSpan.FromSeconds(1), null, CancellationToken.None);

        stopwatch.Stop();
        Assert.Equal(AiCliCompletion.TimedOut, result.Completion);
        Assert.True(stopwatch.ElapsedMilliseconds < 4000, $"timeoutを尊重せず{stopwatch.ElapsedMilliseconds}ms待った。");
    }

    [Fact]
    public async Task RunAsync_ExternalCancellation_ReturnsCancelledPromptly()
    {
        var startInfo = BuildPowerShellSleepStartInfo(seconds: 5);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var stopwatch = Stopwatch.StartNew();

        var result = await CodexCliExecutor.RunAsync(startInfo, TimeSpan.FromSeconds(60), null, cts.Token);

        stopwatch.Stop();
        Assert.Equal(AiCliCompletion.Cancelled, result.Completion);
        Assert.True(stopwatch.ElapsedMilliseconds < 4000, $"外部キャンセルを尊重せず{stopwatch.ElapsedMilliseconds}ms待った。");
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    public void Constructor_NonPositiveTimeoutOrParallelism_Throws(int timeoutSeconds, int maxParallelism)
    {
        var options = new CodexCliOptions { ExecutablePath = "codex.exe", TimeoutSeconds = timeoutSeconds, MaxParallelism = maxParallelism };

        Assert.Throws<ArgumentOutOfRangeException>(() => new CodexCliExecutor(options));
    }

    [Fact]
    public async Task ExecuteAsync_BlankPrompt_Throws()
    {
        var executor = new CodexCliExecutor(new CodexCliOptions { ExecutablePath = "codex.exe", TimeoutSeconds = 10, MaxParallelism = 1 });

        await Assert.ThrowsAsync<ArgumentException>(() => executor.ExecuteAsync("   "));
    }

    private static ProcessStartInfo BuildCmdStartInfo(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = CmdExePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static ProcessStartInfo BuildPowerShellSleepStartInfo(int seconds)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = PowerShellExePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add($"Start-Sleep -Seconds {seconds}");
        return startInfo;
    }
}
