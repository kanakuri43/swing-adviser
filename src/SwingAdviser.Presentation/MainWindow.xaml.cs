using System.Windows;
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
}
