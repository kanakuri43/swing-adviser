using System.Windows;
using MahApps.Metro.Controls;
using SwingAdviser.Application.Positions;
using SwingAdviser.Presentation.ViewModels;

namespace SwingAdviser.Presentation;

public partial class AddExecutionWindow : MetroWindow
{
    private readonly AddExecutionViewModel _viewModel;

    public AddExecutionWindow(
        ExecutionEntryService executionEntryService, PositionRow position)
    {
        _viewModel = new AddExecutionViewModel(
            executionEntryService, position.PositionId, position.StockCode, position.Direction, position.IsMargin, position.RemainingQuantity);
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
