using SwingAdviser.Application.Positions;
using SwingAdviser.Domain.Common;
using SwingAdviser.Domain.Positions;
using SwingAdviser.Tests.Application.TestSupport;

namespace SwingAdviser.Tests.Application.Positions;

public class ExecutionOverviewReaderTests
{
    private static readonly DateTime NowUtc = new(2026, 1, 5, 3, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task GetAllAsync_IncludesExecutionsForBothOpenAndClosedPositions()
    {
        using var contextFactory = new SqliteInMemoryContextFactory();
        await using (var context = contextFactory.CreateDbContext())
        {
            var openPosition = Position.Open("7203", TradeDirection.Long, false, NowUtc, 1000m, 100, null, 20m, 940m, null, NowUtc);

            var closedPosition = Position.Open("9984", TradeDirection.Long, false, NowUtc, 500m, 50, null, 10m, 470m, null, NowUtc);
            closedPosition.AddCloseExecution(NowUtc.AddDays(1), 520m, 50, NowUtc.AddDays(1));

            context.Positions.AddRange(openPosition, closedPosition);
            await context.SaveChangesAsync();
        }

        var reader = new ExecutionOverviewReader(contextFactory);
        var results = await reader.GetAllAsync();

        Assert.Equal(3, results.Count);
        Assert.Contains(results, r => r.StockCode == "7203" && r.IsPositionOpen);
        Assert.Contains(results, r => r.StockCode == "9984" && r.Side == ExecutionSide.Close && !r.IsPositionOpen);
    }

    [Fact]
    public async Task GetAllAsync_OrdersByExecutedAtDescending()
    {
        using var contextFactory = new SqliteInMemoryContextFactory();
        await using (var context = contextFactory.CreateDbContext())
        {
            var position = Position.Open("7203", TradeDirection.Long, false, NowUtc, 1000m, 100, null, 20m, 940m, null, NowUtc);
            position.AddOpenExecution(NowUtc.AddDays(1), 1010m, 50, null, NowUtc.AddDays(1));
            context.Positions.Add(position);
            await context.SaveChangesAsync();
        }

        var results = await new ExecutionOverviewReader(contextFactory).GetAllAsync();

        Assert.Equal(2, results.Count);
        Assert.True(results[0].ExecutedAtJst > results[1].ExecutedAtJst);
    }

    [Fact]
    public async Task GetAllAsync_CorrectionCountMatchesCorrectionLogEntries()
    {
        using var contextFactory = new SqliteInMemoryContextFactory();
        await using (var context = contextFactory.CreateDbContext())
        {
            var position = Position.Open("7203", TradeDirection.Long, false, NowUtc, 1000m, 100, null, 20m, 940m, null, NowUtc);
            var execution = position.Executions.Single();
            position.CorrectExecution(execution, 1000m, 100, NowUtc, "入力ミスの訂正", NowUtc.AddMinutes(1));
            context.Positions.Add(position);
            await context.SaveChangesAsync();
        }

        var result = Assert.Single(await new ExecutionOverviewReader(contextFactory).GetAllAsync());

        Assert.Equal(1, result.CorrectionCount);
    }
}
