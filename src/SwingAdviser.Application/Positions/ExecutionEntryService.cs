using Microsoft.EntityFrameworkCore;
using SwingAdviser.Application.Common;
using SwingAdviser.Application.MarketData;
using SwingAdviser.Domain.Analysis;
using SwingAdviser.Domain.Common;
using SwingAdviser.Domain.Positions;
using SwingAdviser.Domain.Strategy;
using SwingAdviser.Infrastructure.Persistence;

namespace SwingAdviser.Application.Positions;

public sealed record OpenPositionInput(
    string StockCode,
    TradeDirection Direction,
    bool IsMargin,
    DateTime ExecutedAtJst,
    decimal Price,
    int Quantity,
    DateOnly? MarginDueDate,
    string? Memo);

public sealed record OpenPositionPreview(
    OpenPositionInput Input,
    decimal Atr14,
    DateOnly Atr14AsOfDate,
    decimal StopLossPrice,
    decimal ReferenceClose,
    DateOnly ReferenceCloseDate,
    IReadOnlyList<string> Warnings);

public sealed record AddExecutionInput(
    long PositionId,
    ExecutionSide Side,
    DateTime ExecutedAtJst,
    decimal Price,
    int Quantity,
    DateOnly? MarginDueDate);

public sealed record AddExecutionPreview(
    AddExecutionInput Input,
    decimal RemainingQuantityAfter,
    bool WillFullyClose,
    IReadOnlyList<string> Warnings);

/// <summary>
/// 約定の手入力ユースケース。プレビュー（未保存）→利用者確認→確定（保存）の2段構成にする。
/// 候補一覧からボタン1回で約定確定まで行うUIは作らない（CLAUDE.md「Non-negotiable rules」）。
/// 現在値・終値・サイン日時からの自動確定はしない。
/// </summary>
public sealed class ExecutionEntryService(
    DailyBarSynchronizer dailyBarSynchronizer,
    IDbContextFactory<SwingAdviserDbContext> contextFactory,
    StrategyParameters strategyParameters,
    TimeProvider timeProvider)
{
    public async Task<OpenPositionPreview> PreviewOpenPositionAsync(OpenPositionInput input, CancellationToken cancellationToken = default)
    {
        ValidateOpenInput(input);
        EnsureNotFuture(input.ExecutedAtJst);

        // 流動性フィルタ外の銘柄でも新規建て時にATRを正しく出せるよう、その場で日足を同期する。
        await dailyBarSynchronizer.SyncAsync(input.StockCode, cancellationToken).ConfigureAwait(false);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        // 約定日「より前」のバーだけでATRを計算する（約定当日の高値・安値は約定時点では未確定のため）。
        var priorTradeDate = DateOnly.FromDateTime(input.ExecutedAtJst).AddDays(-1);
        var bars = await BarRepository.LoadBarsAsOfAsync(
            context, input.StockCode, priorTradeDate, strategyParameters.AnalysisWindow.BarsToFetch, cancellationToken).ConfigureAwait(false);

        var atrPeriod = strategyParameters.Indicators.AtrPeriod;
        if (bars.Count < atrPeriod + 1)
        {
            throw new InvalidOperationException(
                $"{input.StockCode}: ATR計算に必要な日足（約定日より前に{atrPeriod + 1}本）が不足しています。");
        }

        var atrSeries = TechnicalIndicators.AtrWilder(bars, atrPeriod);
        var atr = atrSeries[^1];
        var referenceBar = bars[^1];

        var stopLossMultiple = input.Direction == TradeDirection.Long
            ? strategyParameters.Risk.LongStopLossAtrMultiple
            : strategyParameters.Risk.ShortStopLossAtrMultiple;
        var sign = input.Direction == TradeDirection.Long ? 1m : -1m;
        var stopLossPrice = input.Price - (sign * stopLossMultiple * atr);

        var warnings = new List<string>();
        if (input.IsMargin && input.MarginDueDate is null)
        {
            warnings.Add("信用返済期限が未入力です。確定後は「未確認」として扱われます。");
        }

        return new OpenPositionPreview(input, atr, referenceBar.TradeDate, stopLossPrice, referenceBar.Close, referenceBar.TradeDate, warnings);
    }

    public async Task<long> ConfirmOpenPositionAsync(OpenPositionPreview preview, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preview);
        var input = preview.Input;
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;

        var position = Position.Open(
            input.StockCode,
            input.Direction,
            input.IsMargin,
            Jst.ToUtc(input.ExecutedAtJst),
            input.Price,
            input.Quantity,
            input.MarginDueDate,
            preview.Atr14,
            preview.StopLossPrice,
            input.Memo,
            nowUtc);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        context.Positions.Add(position);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return position.Id;
    }

    public async Task<AddExecutionPreview> PreviewAddExecutionAsync(AddExecutionInput input, CancellationToken cancellationToken = default)
    {
        if (input.Price <= 0)
        {
            throw new ArgumentException("価格は正の値である必要があります。", nameof(input));
        }

        if (input.Quantity <= 0)
        {
            throw new ArgumentException("株数は正の値である必要があります。", nameof(input));
        }

        EnsureNotFuture(input.ExecutedAtJst);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var position = await context.Positions.Include(p => p.Executions)
            .FirstOrDefaultAsync(p => p.Id == input.PositionId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"ポジションId={input.PositionId}が見つかりません。");

        if (position.Status != PositionStatus.Open)
        {
            throw new InvalidOperationException("決済済みのポジションには約定を追加できません。");
        }

        var warnings = new List<string>();
        decimal remainingAfter;

        if (input.Side == ExecutionSide.Open)
        {
            remainingAfter = position.RemainingQuantity + input.Quantity;
            if (position.IsMargin && input.MarginDueDate is null)
            {
                warnings.Add("信用返済期限が未入力です。確定後は「未確認」として扱われます。");
            }
        }
        else
        {
            if (input.Quantity > position.RemainingQuantity)
            {
                throw new InvalidOperationException($"残株数({position.RemainingQuantity})を超える決済はできません。");
            }

            remainingAfter = position.RemainingQuantity - input.Quantity;
        }

        return new AddExecutionPreview(input, remainingAfter, remainingAfter == 0, warnings);
    }

    public async Task ConfirmAddExecutionAsync(AddExecutionPreview preview, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preview);
        var input = preview.Input;
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var position = await context.Positions.Include(p => p.Executions)
            .FirstOrDefaultAsync(p => p.Id == input.PositionId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"ポジションId={input.PositionId}が見つかりません。");

        var executedAtUtc = Jst.ToUtc(input.ExecutedAtJst);
        if (input.Side == ExecutionSide.Open)
        {
            position.AddOpenExecution(executedAtUtc, input.Price, input.Quantity, input.MarginDueDate, nowUtc);
        }
        else
        {
            position.AddCloseExecution(executedAtUtc, input.Price, input.Quantity, nowUtc);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task CorrectExecutionAsync(
        long positionId, long executionId, decimal price, int quantity, DateTime executedAtJst, string reason,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var position = await LoadPositionAsync(context, positionId, cancellationToken).ConfigureAwait(false);
        var execution = FindExecution(position, executionId);

        position.CorrectExecution(execution, price, quantity, Jst.ToUtc(executedAtJst), reason, timeProvider.GetUtcNow().UtcDateTime);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SetMarginDueDateAsync(
        long positionId, long executionId, DateOnly marginDueDate, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var position = await LoadPositionAsync(context, positionId, cancellationToken).ConfigureAwait(false);
        var execution = FindExecution(position, executionId);

        position.SetMarginDueDate(execution, marginDueDate, timeProvider.GetUtcNow().UtcDateTime);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<Position> LoadPositionAsync(SwingAdviserDbContext context, long positionId, CancellationToken cancellationToken) =>
        await context.Positions.Include(p => p.Executions)
            .FirstOrDefaultAsync(p => p.Id == positionId, cancellationToken).ConfigureAwait(false)
        ?? throw new InvalidOperationException($"ポジションId={positionId}が見つかりません。");

    private static Execution FindExecution(Position position, long executionId) =>
        position.Executions.FirstOrDefault(e => e.Id == executionId)
        ?? throw new InvalidOperationException($"約定Id={executionId}が見つかりません。");

    private void EnsureNotFuture(DateTime executedAtJst)
    {
        if (Jst.ToUtc(executedAtJst) > timeProvider.GetUtcNow().UtcDateTime)
        {
            throw new ArgumentException("約定日時は現在より未来にできません。", nameof(executedAtJst));
        }
    }

    private static void ValidateOpenInput(OpenPositionInput input)
    {
        if (string.IsNullOrWhiteSpace(input.StockCode))
        {
            throw new ArgumentException("証券コードは必須です。", nameof(input));
        }

        if (input.Price <= 0)
        {
            throw new ArgumentException("価格は正の値である必要があります。", nameof(input));
        }

        if (input.Quantity <= 0)
        {
            throw new ArgumentException("株数は正の値である必要があります。", nameof(input));
        }

        if (input.Direction == TradeDirection.Short && !input.IsMargin)
        {
            throw new InvalidOperationException("Shortは信用取引でのみ建てられます。");
        }
    }
}
