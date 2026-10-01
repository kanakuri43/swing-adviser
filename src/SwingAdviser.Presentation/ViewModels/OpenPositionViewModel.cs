using SwingAdviser.Application.Positions;
using SwingAdviser.Domain.Common;

namespace SwingAdviser.Presentation.ViewModels;

/// <summary>
/// 新規建て入力ダイアログのViewModel。プレビュー（未保存）→確認→確定（保存）の2段構成にし、
/// 候補一覧からボタン1回で確定まで行うUIにはしない（CLAUDE.md「Non-negotiable rules」）。
/// </summary>
public sealed class OpenPositionViewModel : ObservableObject
{
    private readonly ExecutionEntryService _executionEntryService;

    private string _stockCode;
    private string _directionText;
    private bool _isMargin;
    private DateTime? _executedAtJst;
    private string _priceText = string.Empty;
    private string _quantityText = string.Empty;
    private DateTime? _marginDueDate;
    private string? _memo;
    private string _validationMessage = string.Empty;
    private OpenPositionPreview? _preview;

    public OpenPositionViewModel(ExecutionEntryService executionEntryService, string? stockCode = null, TradeDirection? direction = null)
    {
        _executionEntryService = executionEntryService;
        _stockCode = stockCode ?? string.Empty;
        _directionText = direction == TradeDirection.Short ? "Short" : "Long";
        _isMargin = _directionText == "Short";
    }

    public string StockCode { get => _stockCode; set => Set(ref _stockCode, value); }

    public string DirectionText
    {
        get => _directionText;
        set
        {
            if (Set(ref _directionText, value) && value == "Short")
            {
                IsMargin = true;
            }
        }
    }

    public bool IsMargin { get => _isMargin; set => Set(ref _isMargin, value); }

    public DateTime? ExecutedAtJst { get => _executedAtJst; set => Set(ref _executedAtJst, value); }

    public string PriceText { get => _priceText; set => Set(ref _priceText, value); }

    public string QuantityText { get => _quantityText; set => Set(ref _quantityText, value); }

    public DateTime? MarginDueDate { get => _marginDueDate; set => Set(ref _marginDueDate, value); }

    public string? Memo { get => _memo; set => Set(ref _memo, value); }

    public string ValidationMessage { get => _validationMessage; set => Set(ref _validationMessage, value); }

    public OpenPositionPreview? Preview { get => _preview; private set { if (Set(ref _preview, value)) { OnPropertyChanged(nameof(PreviewSummary)); } } }

    public string PreviewSummary => Preview is null
        ? string.Empty
        : $"ATR14: {Preview.Atr14:F2}（基準日 {Preview.Atr14AsOfDate:yyyy-MM-dd}）\n" +
          $"損切ライン: {Preview.StopLossPrice:F2}\n" +
          $"参考終値: {Preview.ReferenceClose:F2}（{Preview.ReferenceCloseDate:yyyy-MM-dd}）" +
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
            Preview = await _executionEntryService.PreviewOpenPositionAsync(input);
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
            await _executionEntryService.ConfirmOpenPositionAsync(Preview);
            return true;
        }
        catch (Exception exception)
        {
            ValidationMessage = exception.Message;
            return false;
        }
    }

    private bool TryBuildInput(out OpenPositionInput input)
    {
        input = null!;

        if (string.IsNullOrWhiteSpace(StockCode))
        {
            ValidationMessage = "証券コードを入力してください。";
            return false;
        }

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

        var direction = DirectionText == "Short" ? TradeDirection.Short : TradeDirection.Long;
        var marginDueDate = IsMargin && MarginDueDate.HasValue ? DateOnly.FromDateTime(MarginDueDate.Value) : (DateOnly?)null;

        input = new OpenPositionInput(
            StockCode.Trim(), direction, IsMargin, ExecutedAtJst.Value, price, quantity, marginDueDate,
            string.IsNullOrWhiteSpace(Memo) ? null : Memo);
        return true;
    }
}
