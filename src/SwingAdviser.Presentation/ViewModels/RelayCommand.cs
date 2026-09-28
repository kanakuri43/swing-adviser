using System.Windows.Input;

namespace SwingAdviser.Presentation.ViewModels;

/// <summary>同期的な操作（キャンセルボタン等）向けの最小限のICommand実装。</summary>
public sealed class RelayCommand(Action execute, Func<bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;

    public void Execute(object? parameter) => execute();

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
