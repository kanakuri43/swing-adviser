using System.Windows;
using MahApps.Metro.Controls;
using SwingAdviser.Application.Positions;
using SwingAdviser.Domain.Common;
using SwingAdviser.Presentation.ViewModels;

namespace SwingAdviser.Presentation;

public partial class OpenPositionWindow : MetroWindow
{
    private readonly OpenPositionViewModel _viewModel;

    public OpenPositionWindow(ExecutionEntryService executionEntryService, string? stockCode = null, TradeDirection? direction = null)
    {
        _viewModel = new OpenPositionViewModel(executionEntryService, stockCode, direction);
        DataContext = _viewModel;
        InitializeComponent();
    }

    private async void PreviewButtonClick(object sender, RoutedEventArgs e)
    {
        ConfirmButton.IsEnabled = await _viewModel.PreviewAsync();
    }

    private async void ConfirmButtonClick(object sender, RoutedEventArgs e)
    {
        if (await _viewModel.ConfirmAsync())
        {
            DialogResult = true;
        }
    }
}
