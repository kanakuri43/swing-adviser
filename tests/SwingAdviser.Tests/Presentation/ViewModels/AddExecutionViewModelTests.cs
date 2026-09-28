using SwingAdviser.Application.MarketData;
using SwingAdviser.Application.Positions;
using SwingAdviser.Domain.Positions;
using SwingAdviser.Infrastructure.Configuration;
using SwingAdviser.Presentation.ViewModels;
using SwingAdviser.Tests.Application.TestSupport;
using SwingAdviser.Tests.Domain;

namespace SwingAdviser.Tests.Presentation.ViewModels;

public class AddExecutionViewModelTests
{
    private static ExecutionEntryService BuildService(SqliteInMemoryContextFactory contextFactory)
    {
        var yahooOptions = new YahooFinanceOptions
        {
            BaseUrl = "https://example.com/",
            MaxRequestsPerSecond = 5,
            TimeoutSeconds = 10,
            InitialFetchCalendarDays = 400,
            DailyBarFinalizedTimeJst = "16:00",
        };
        var timeProvider = new FixedTimeProvider(new DateTimeOffset(2026, 1, 10, 8, 0, 0, TimeSpan.Zero));
        var barSynchronizer = new DailyBarSynchronizer(new FakeYahooFinanceClient(), contextFactory, yahooOptions, timeProvider);
        return new ExecutionEntryService(barSynchronizer, contextFactory, TestFixtures.DefaultStrategyParameters(), timeProvider);
    }

    [Fact]
    public async Task PreviewAsync_MissingExecutedAt_SetsValidationMessage()
    {
        using var contextFactory = new SqliteInMemoryContextFactory();
        var viewModel = new AddExecutionViewModel(BuildService(contextFactory), 1, "7203", "Long", false, 100m)
        {
            PriceText = "1000",
            QuantityText = "50",
        };

        var result = await viewModel.PreviewAsync();

        Assert.False(result);
        Assert.Contains("約定日時", viewModel.ValidationMessage);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("abc")]
    public async Task PreviewAsync_InvalidPrice_SetsValidationMessage(string priceText)
    {
        using var contextFactory = new SqliteInMemoryContextFactory();
        var viewModel = new AddExecutionViewModel(BuildService(contextFactory), 1, "7203", "Long", false, 100m)
        {
            ExecutedAtJst = new DateTime(2026, 1, 5, 9, 0, 0),
            PriceText = priceText,
            QuantityText = "50",
        };

        var result = await viewModel.PreviewAsync();

        Assert.False(result);
        Assert.Contains("価格", viewModel.ValidationMessage);
    }

    [Fact]
    public async Task ConfirmAsync_WithoutPreview_ReturnsFalse()
    {
        using var contextFactory = new SqliteInMemoryContextFactory();
        var viewModel = new AddExecutionViewModel(BuildService(contextFactory), 1, "7203", "Long", false, 100m);

        var result = await viewModel.ConfirmAsync();

        Assert.False(result);
        Assert.Contains("プレビュー", viewModel.ValidationMessage);
    }

    [Fact]
    public void IsOpenAddition_ReflectsSide()
    {
        using var contextFactory = new SqliteInMemoryContextFactory();
        var viewModel = new AddExecutionViewModel(BuildService(contextFactory), 1, "7203", "Long", false, 100m);

        Assert.False(viewModel.IsOpenAddition);

        viewModel.Side = ExecutionSide.Open;

        Assert.True(viewModel.IsOpenAddition);
    }
}
