using SwingAdviser.Domain.Common;
using SwingAdviser.Domain.Positions;

namespace SwingAdviser.Tests.Domain;

public class PositionTests
{
    private static readonly DateTime Opened = new(2026, 1, 5, 0, 30, 0, DateTimeKind.Utc);

    private static Position CreateLongPosition(int quantity = 100, decimal price = 1000m, decimal initialAtr = 30m, decimal stopLossPrice = 910m)
    {
        return Position.Open(
            stockCode: "7203",
            direction: TradeDirection.Long,
            isMargin: false,
            executedAtUtc: Opened,
            price: price,
            quantity: quantity,
            marginDueDate: null,
            initialAtr: initialAtr,
            stopLossPrice: stopLossPrice,
            memo: null,
            nowUtc: Opened);
    }

    [Fact]
    public void AddCloseExecution_PartialClose_ReducesRemainingQuantity()
    {
        var position = CreateLongPosition(quantity: 100);

        position.AddCloseExecution(Opened.AddDays(1), 1050m, 40, Opened.AddDays(1));

        Assert.Equal(60m, position.RemainingQuantity);
        Assert.Equal(PositionStatus.Open, position.Status);
    }

    [Fact]
    public void AddCloseExecution_ExactRemainingQuantity_MarksPositionClosed()
    {
        var position = CreateLongPosition(quantity: 100);

        position.AddCloseExecution(Opened.AddDays(1), 1050m, 100, Opened.AddDays(1));

        Assert.Equal(0m, position.RemainingQuantity);
        Assert.Equal(PositionStatus.Closed, position.Status);
    }

    [Fact]
    public void AddCloseExecution_ExceedingRemainingQuantity_Throws()
    {
        var position = CreateLongPosition(quantity: 100);

        Assert.Throws<InvalidOperationException>(() =>
            position.AddCloseExecution(Opened.AddDays(1), 1050m, 101, Opened.AddDays(1)));
    }

    [Fact]
    public void AddOpenExecution_OnClosedPosition_Throws()
    {
        var position = CreateLongPosition(quantity: 100);
        position.AddCloseExecution(Opened.AddDays(1), 1050m, 100, Opened.AddDays(1));

        Assert.Throws<InvalidOperationException>(() =>
            position.AddOpenExecution(Opened.AddDays(2), 1000m, 10, null, Opened.AddDays(2)));
    }

    [Fact]
    public void Open_ShortWithoutMargin_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => Position.Open(
            stockCode: "7203",
            direction: TradeDirection.Short,
            isMargin: false,
            executedAtUtc: Opened,
            price: 1000m,
            quantity: 100,
            marginDueDate: null,
            initialAtr: 30m,
            stopLossPrice: 1090m,
            memo: null,
            nowUtc: Opened));
    }

    [Fact]
    public void ApplySplit_TwoForOne_DoublesQuantityHalvesPriceAndRiskLines_KeepsRawExecutionUnchanged()
    {
        var position = CreateLongPosition(quantity: 100, price: 1000m, initialAtr: 30m, stopLossPrice: 910m);

        position.ApplySplit(2m, Opened.AddDays(1));

        Assert.Equal(200m, position.RemainingQuantity);
        Assert.Equal(500m, position.AverageEntryPrice);
        Assert.Equal(455m, position.StopLossPrice);
        Assert.Equal(15m, position.InitialAtr);

        var rawExecution = Assert.Single(position.Executions);
        Assert.Equal(1000m, rawExecution.Price);
        Assert.Equal(100, rawExecution.Quantity);
    }

    [Fact]
    public void ApplySplit_ThenNewExecution_IsNotDoubleAdjusted()
    {
        var position = CreateLongPosition(quantity: 100, price: 1000m);
        position.ApplySplit(2m, Opened.AddDays(1));

        position.AddOpenExecution(Opened.AddDays(2), 500m, 50, null, Opened.AddDays(2));

        Assert.Equal(250m, position.RemainingQuantity);
    }

    [Fact]
    public void ApplySplit_ReverseSplitCausingFractionalShares_Throws()
    {
        var position = CreateLongPosition(quantity: 105);

        Assert.Throws<InvalidOperationException>(() => position.ApplySplit(0.1m, Opened.AddDays(1)));
    }

    [Fact]
    public void CorrectExecution_EmptyReason_Throws()
    {
        var position = CreateLongPosition(quantity: 100);
        var execution = Assert.Single(position.Executions);

        Assert.Throws<ArgumentException>(() =>
            position.CorrectExecution(execution, 1010m, 100, Opened, string.Empty, Opened.AddMinutes(1)));
    }

    [Fact]
    public void CorrectExecution_AppendsLogEntries_KeepsSingleExecutionRow()
    {
        var position = CreateLongPosition(quantity: 100);
        var execution = Assert.Single(position.Executions);

        position.CorrectExecution(execution, 1010m, 100, Opened, "価格入力ミス", Opened.AddMinutes(1));
        position.CorrectExecution(execution, 1010m, 100, Opened, "再修正", Opened.AddMinutes(2));

        Assert.Equal(2, execution.CorrectionLog.Count);
        Assert.Single(position.Executions);
    }

    [Fact]
    public void CorrectExecution_ReducingOpenQuantityBelowAlreadyClosedQuantity_Throws()
    {
        var position = CreateLongPosition(quantity: 100);
        position.AddCloseExecution(Opened.AddDays(1), 1050m, 60, Opened.AddDays(1));
        var openExecution = Assert.Single(position.Executions, e => e.Side == ExecutionSide.Open);

        Assert.Throws<InvalidOperationException>(() =>
            position.CorrectExecution(openExecution, 1000m, 50, Opened, "訂正", Opened.AddDays(2)));
    }
}
