using System.Windows.Input;

namespace SwingAdviser.Presentation.ViewModels;

/// <summary>
/// 長時間処理を安全に実行するための再利用可能なICommand実装。多重実行防止（IsRunning/CanExecute）・
/// キャンセル（CancellationTokenSource）・未処理例外の隔離（Faultedイベント）だけを保証する。
/// 業務的な例外処理・状態文言の組み立てはViewModel側（execute本体）の責務とする。
/// </summary>
public sealed class AsyncRelayCommand(Func<CancellationToken, Task> execute, Func<bool>? canExecute = null) : ObservableObject, ICommand
{
    private CancellationTokenSource? _cancellationTokenSource;
    private bool _isRunning;

    public event EventHandler? CanExecuteChanged;

    /// <summary>execute本体が投げた例外（OperationCanceledExceptionを除く）を通知する。UIスレッドの未処理例外にしないため。</summary>
    public event EventHandler<Exception>? Faulted;

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (Set(ref _isRunning, value))
            {
                CanExecuteChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public bool CanExecute(object? parameter) => !IsRunning && (canExecute?.Invoke() ?? true);

    public void Execute(object? parameter) => _ = ExecuteAndObserveFaultsAsync();

    public async Task ExecuteAsync()
    {
        if (IsRunning)
        {
            return;
        }

        IsRunning = true;
        _cancellationTokenSource = new CancellationTokenSource();
        try
        {
            await execute(_cancellationTokenSource.Token);
        }
        finally
        {
            _cancellationTokenSource.Dispose();
            _cancellationTokenSource = null;
            IsRunning = false;
        }
    }

    public void Cancel() => _cancellationTokenSource?.Cancel();

    private async Task ExecuteAndObserveFaultsAsync()
    {
        try
        {
            await ExecuteAsync();
        }
        catch (OperationCanceledException)
        {
            // 利用者によるキャンセルは正常系。Faultedへは流さない。
        }
        catch (Exception exception)
        {
            Faulted?.Invoke(this, exception);
        }
    }
}
