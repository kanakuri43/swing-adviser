using SwingAdviser.Application.Positions;
using SwingAdviser.Domain.Common;
using SwingAdviser.Domain.Positions;

namespace SwingAdviser.Presentation.ViewModels;

/// <summary>
/// 入力済み約定の訂正ダイアログのViewModel。誤入力の修正用で、理由は必須。
/// プレビュー（未保存）→確認→確定（保存）の2段構成にする。
/// </summary>
public sealed class CorrectExecutionViewModel : ObservableObject
{
    private readonly ExecutionEntryService _executionEntryService;
    private readonly ExecutionOverview _original;

    private DateTime? _executedAtJst;
    private string _priceText;
    private string _quantityText;
    private DateTime? _marginDueDate;
    private string _reason = string.Empty;
    private string _validationMessage = string.Empty;
    private CorrectExecutionPreview? _preview;

    public CorrectExecutionViewModel(ExecutionEntryService executionEntryService, ExecutionOverview original)
    {
        _executionEntryService = executionEntryService;
        _original = original;
        _executedAtJst = original.ExecutedAtJst;
        _priceText = original.Price.ToString();
        _quantityText = original.Quantity.ToString();
        _marginDueDate = original.MarginDueDate?.ToDateTime(TimeOnly.MinValue);
    }

    public string TargetText =>
        $"{_original.StockCode} {_original.StockName}（{(_original.Direction == TradeDirection.Long ? "Long" : "Short")}・" +
        $"{(_original.Side == ExecutionSide.Open ? "新規" : "決済")}約定）";

    public bool IsMarginDueEditable => _original.IsMargin && _original.Side == ExecutionSide.Open;

    public DateTime? ExecutedAtJst { get => _executedAtJst; set => Set(ref _executedAtJst, value); }

    public string PriceText { get => _priceText; set => Set(ref _priceText, value); }

    public string QuantityText { get => _quantityText; set => Set(ref _quantityText, value); }

    public DateTime? MarginDueDate { get => _marginDueDate; set => Set(ref _marginDueDate, value); }

    public string Reason { get => _reason; set => Set(ref _reason, value); }

    public string ValidationMessage { get => _validationMessage; set => Set(ref _validationMessage, value); }

    public CorrectExecutionPreview? Preview { get => _preview; private set { if (Set(ref _preview, value)) { OnPropertyChanged(nameof(PreviewSummary)); } } }

    public string PreviewSummary
    {
        get
        {
            if (Preview is null)
            {
                return string.Empty;
            }

            var lines = new List<string> { $"訂正後の残数量: {Preview.RemainingQuantityAfter:F0}株" };
            lines.Add(Preview.NewStopLossPrice.HasValue
                ? $"損切ライン: {Preview.CurrentStopLossPrice:N1} → {Preview.NewStopLossPrice.Value:N1}（新しい約定価格・日付でATRから再計算）"
                : $"損切ライン: {Preview.CurrentStopLossPrice:N1}（変更なし）");
            if (Preview.Warnings.Count > 0)
            {
                lines.Add("警告: " + string.Join(" / ", Preview.Warnings));
            }

            return string.Join("\n", lines);
        }
    }

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
            Preview = await _executionEntryService.PreviewCorrectExecutionAsync(input);
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
            await _executionEntryService.ConfirmCorrectExecutionAsync(Preview);
            return true;
        }
        catch (Exception exception)
        {
            ValidationMessage = exception.Message;
            return false;
        }
    }

    private bool TryBuildInput(out CorrectExecutionInput input)
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

        if (string.IsNullOrWhiteSpace(Reason))
        {
            ValidationMessage = "訂正理由を入力してください。";
            return false;
        }

        var marginDueDate = IsMarginDueEditable && MarginDueDate.HasValue
            ? DateOnly.FromDateTime(MarginDueDate.Value)
            : (DateOnly?)null;

        input = new CorrectExecutionInput(
            _original.PositionId, _original.ExecutionId, ExecutedAtJst.Value, price, quantity, marginDueDate, Reason.Trim());
        return true;
    }
}
