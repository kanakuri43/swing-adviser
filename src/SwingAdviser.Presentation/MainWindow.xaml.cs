using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;
using MahApps.Metro.Controls;
using SwingAdviser.Application.Positions;
using SwingAdviser.Presentation.ViewModels;

namespace SwingAdviser.Presentation;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : MetroWindow
{
    private readonly MainWindowViewModel _viewModel;
    private readonly ExecutionEntryService _executionEntryService;

    public MainWindow(MainWindowViewModel viewModel, ExecutionEntryService executionEntryService)
    {
        _viewModel = viewModel;
        _executionEntryService = executionEntryService;
        DataContext = _viewModel;
        InitializeComponent();
        Loaded += async (_, _) => await _viewModel.InitializeAsync();
    }

    private async void OpenPositionFromCandidateClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not CandidateRow candidate)
        {
            return;
        }

        var result = new OpenPositionWindow(_executionEntryService, candidate.StockCode, candidate.Overview.Direction) { Owner = this }.ShowDialog();
        if (result == true)
        {
            await _viewModel.ReloadDisplayDataAsync();
        }
    }

    private async void OpenPositionBlankClick(object sender, RoutedEventArgs e)
    {
        var result = new OpenPositionWindow(_executionEntryService) { Owner = this }.ShowDialog();
        if (result == true)
        {
            await _viewModel.ReloadDisplayDataAsync();
        }
    }

    private async void AddExecutionClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not PositionRow position)
        {
            return;
        }

        var result = new AddExecutionWindow(_executionEntryService, position) { Owner = this }.ShowDialog();
        if (result == true)
        {
            await _viewModel.ReloadDisplayDataAsync();
        }
    }

    /// <summary>候補行のダブルクリックでYahoo!ファイナンスのチャートページを既定ブラウザで開く。
    /// 行内のボタン（実行・約定を手入力）上でのダブルクリックは無視する。</summary>
    private void CandidateRowDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestorOrSelf<System.Windows.Controls.Button>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        if ((sender as FrameworkElement)?.DataContext is not CandidateRow candidate)
        {
            return;
        }

        BrowserLauncher.Open(BrowserLauncher.YahooFinanceChartUri(candidate.StockCode));
    }

    private void ReferenceUrlRequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        BrowserLauncher.Open(e.Uri);
        e.Handled = true;
    }

    private static T? FindAncestorOrSelf<T>(DependencyObject? element) where T : DependencyObject
    {
        while (element is not null)
        {
            if (element is T match)
            {
                return match;
            }

            element = VisualTreeHelper.GetParent(element);
        }

        return null;
    }
}
