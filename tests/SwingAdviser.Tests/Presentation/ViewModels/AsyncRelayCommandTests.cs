using SwingAdviser.Presentation.ViewModels;

namespace SwingAdviser.Tests.Presentation.ViewModels;

public class AsyncRelayCommandTests
{
    [Fact]
    public async Task CanExecute_WhileRunning_ReturnsFalseThenTrueAfterCompletion()
    {
        var gate = new TaskCompletionSource();
        var command = new AsyncRelayCommand(async _ => await gate.Task);

        Assert.True(command.CanExecute(null));
        var task = command.ExecuteAsync();

        Assert.False(command.CanExecute(null));

        gate.SetResult();
        await task;

        Assert.True(command.CanExecute(null));
    }

    [Fact]
    public async Task ExecuteAsync_CalledWhileAlreadyRunning_IsNoOpAndDoesNotInvokeBodyAgain()
    {
        var gate = new TaskCompletionSource();
        var executionCount = 0;
        var command = new AsyncRelayCommand(async _ =>
        {
            Interlocked.Increment(ref executionCount);
            await gate.Task;
        });

        var first = command.ExecuteAsync();
        var second = command.ExecuteAsync();

        await second;
        Assert.Equal(1, executionCount);

        gate.SetResult();
        await first;
    }

    [Fact]
    public async Task Cancel_CancelsTokenPassedToExecuteBody()
    {
        var tokenWasCancelled = false;
        var started = new TaskCompletionSource();
        var command = new AsyncRelayCommand(async ct =>
        {
            started.SetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, ct);
            }
            catch (OperationCanceledException)
            {
                tokenWasCancelled = ct.IsCancellationRequested;
                throw;
            }
        });

        var task = command.ExecuteAsync();
        await started.Task;
        command.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.True(tokenWasCancelled);
        Assert.True(command.CanExecute(null));
    }

    [Fact]
    public async Task Execute_BodyThrows_RaisesFaultedAndDoesNotThrowToCaller()
    {
        Exception? faulted = null;
        var command = new AsyncRelayCommand(_ => throw new InvalidOperationException("テスト用例外。"));
        command.Faulted += (_, exception) => faulted = exception;

        command.Execute(null);
        await WaitUntilAsync(() => !command.IsRunning);

        Assert.IsType<InvalidOperationException>(faulted);
    }

    [Fact]
    public async Task Execute_BodyThrowsOperationCanceled_DoesNotRaiseFaulted()
    {
        var faultedRaised = false;
        var command = new AsyncRelayCommand(_ => throw new OperationCanceledException());
        command.Faulted += (_, _) => faultedRaised = true;

        command.Execute(null);
        await WaitUntilAsync(() => !command.IsRunning);

        Assert.False(faultedRaised);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 2000)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            if (stopwatch.ElapsedMilliseconds > timeoutMs)
            {
                throw new TimeoutException("条件が満たされるまでにタイムアウトしました。");
            }

            await Task.Delay(10);
        }
    }
}
