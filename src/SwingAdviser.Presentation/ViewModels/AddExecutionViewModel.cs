using SwingAdviser.Application.Positions;
using SwingAdviser.Domain.Positions;

namespace SwingAdviser.Presentation.ViewModels;

/// <summary>
/// 保有ポジションへの追加約定（新規追加/決済）入力ダイアログのViewModel。
/// プレビュー（未保存）→確認→確定（保存）の2段構成にする。
/// </summary>
public sealed class AddExecutionViewModel : ObservableObject
{
    private readonly ExecutionEntryService _executionEntryService;

    private ExecutionSide _side = ExecutionSide.Close;
    private DateTime? _executedAtJst;
    private string _priceText = string.Empty;
    private string _quantityText = string.Empty;
    private DateTime? _marginDueDate;
    private string _validationMessage = string.Empty;
    private AddExecutionPreview? _preview;

    public AddExecutionViewModel(
        ExecutionEntryService executionEntryService, long positionId, string stockCode, string direction, bool isMargin, decimal remainingQuantity)
    {
        _executionEntryService = executionEntryService;
        PositionId = positionId;
        StockCode = stockCode;
        Direction = direction;
        IsMargin = isMargin;
        RemainingQuantity = remainingQuantity;
    }

    public long PositionId { get; }

    public string StockCode { get; }

    public string Direction { get; }

    public bool IsMargin { get; }

    public decimal RemainingQuantity { get; }

    public ExecutionSide Side { get => _side; set => Set(ref _side, value); }

    public bool IsOpenAddition => Side == ExecutionSide.Open;

    public DateTime? ExecutedAtJst { get => _executedAtJst; set => Set(ref _executedAtJst, value); }

    public string PriceText { get => _priceText; set => Set(ref _priceText, value); }

    public string QuantityText { get => _quantityText; set => Set(ref _quantityText, value); }

    public DateTime? MarginDueDate { get => _marginDueDate; set => Set(ref _marginDueDate, value); }

    public string ValidationMessage { get => _validationMessage; set => Set(ref _validationMessage, value); }

    public AddExecutionPreview? Preview { get => _preview; private set { if (Set(ref _preview, value)) { OnPropertyChanged(nameof(PreviewSummary)); } } }

    public string PreviewSummary => Preview is null
        ? string.Empty
        : $"決済後残数量: {Preview.RemainingQuantityAfter:F0}株{(Preview.WillFullyClose ? "（全決済）" : string.Empty)}" +
          (Preview.Warnings.Count > 0 ? "\n警告: " + string.Join(" / ", Preview.Warnings) : string.Empty);

    public async Task<bool> PreviewAsync()
    {
        ValidationMessage = string.Empty;
        Preview = null;

        if (!TryBuildInput(out var input))
        {
            return false;
        }

        try
        {
            Preview = await _executionEntryService.PreviewAddExecutionAsync(input);
            return true;
        }
        catch (Exception exception)
        {
            ValidationMessage = exception.Message;
            return false;
        }
    }

    public async Task<bool> ConfirmAsync()
    {
        if (Preview is null)
        {
            ValidationMessage = "先にプレビューを実行してください。";
            return false;
        }

        try
        {
            await _executionEntryService.ConfirmAddExecutionAsync(Preview);
            return true;
        }
        catch (Exception exception)
        {
            ValidationMessage = exception.Message;
            return false;
        }
    }

    private bool TryBuildInput(out AddExecutionInput input)
    {
        input = null!;

        if (!ExecutedAtJst.HasValue)
        {
            ValidationMessage = "約定日時を選択してください。";
            return false;
        }

        if (!decimal.TryParse(PriceText, out var price) || price <= 0)
        {
            ValidationMessage = "価格は正の数値で入力してください。";
            return false;
        }

        if (!int.TryParse(QuantityText, out var quantity) || quantity <= 0)
        {
            ValidationMessage = "株数は正の整数で入力してください。";
            return false;
        }

        var marginDueDate = Side == ExecutionSide.Open && IsMargin && MarginDueDate.HasValue
            ? DateOnly.FromDateTime(MarginDueDate.Value)
            : (DateOnly?)null;

        input = new AddExecutionInput(PositionId, Side, ExecutedAtJst.Value, price, quantity, marginDueDate);
        return true;
    }
}
