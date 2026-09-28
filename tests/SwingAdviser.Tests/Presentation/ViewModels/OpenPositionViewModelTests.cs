using SwingAdviser.Application.MarketData;
using SwingAdviser.Application.Positions;
using SwingAdviser.Infrastructure.Configuration;
using SwingAdviser.Presentation.ViewModels;
using SwingAdviser.Tests.Application.TestSupport;
using SwingAdviser.Tests.Domain;

namespace SwingAdviser.Tests.Presentation.ViewModels;

public class OpenPositionViewModelTests
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
    public async Task PreviewAsync_MissingStockCode_SetsValidationMessageAndReturnsFalse()
    {
        using var contextFactory = new SqliteInMemoryContextFactory();
        var viewModel = new OpenPositionViewModel(BuildService(contextFactory))
        {
            ExecutedAtJst = new DateTime(2026, 1, 5, 9, 0, 0),
            PriceText = "1000",
            QuantityText = "100",
        };

        var result = await viewModel.PreviewAsync();

        Assert.False(result);
        Assert.Contains("証券コード", viewModel.ValidationMessage);
    }

    [Fact]
    public async Task PreviewAsync_MissingExecutedAt_SetsValidationMessage()
    {
        using var contextFactory = new SqliteInMemoryContextFactory();
        var viewModel = new OpenPositionViewModel(BuildService(contextFactory))
        {
            StockCode = "7203",
            PriceText = "1000",
            QuantityText = "100",
        };

        var result = await viewModel.PreviewAsync();

        Assert.False(result);
        Assert.Contains("約定日時", viewModel.ValidationMessage);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-100")]
    [InlineData("abc")]
    public async Task PreviewAsync_InvalidPrice_SetsValidationMessage(string priceText)
    {
        using var contextFactory = new SqliteInMemoryContextFactory();
        var viewModel = new OpenPositionViewModel(BuildService(contextFactory))
        {
            StockCode = "7203",
            ExecutedAtJst = new DateTime(2026, 1, 5, 9, 0, 0),
            PriceText = priceText,
            QuantityText = "100",
        };

        var result = await viewModel.PreviewAsync();

        Assert.False(result);
        Assert.Contains("価格", viewModel.ValidationMessage);
    }

    [Fact]
    public async Task PreviewAsync_InvalidQuantity_SetsValidationMessage()
    {
        using var contextFactory = new SqliteInMemoryContextFactory();
        var viewModel = new OpenPositionViewModel(BuildService(contextFactory))
        {
            StockCode = "7203",
            ExecutedAtJst = new DateTime(2026, 1, 5, 9, 0, 0),
            PriceText = "1000",
            QuantityText = "0",
        };

        var result = await viewModel.PreviewAsync();

        Assert.False(result);
        Assert.Contains("株数", viewModel.ValidationMessage);
    }

    [Fact]
    public async Task ConfirmAsync_WithoutPreview_SetsValidationMessageAndReturnsFalse()
    {
        using var contextFactory = new SqliteInMemoryContextFactory();
        var viewModel = new OpenPositionViewModel(BuildService(contextFactory));

        var result = await viewModel.ConfirmAsync();

        Assert.False(result);
        Assert.Contains("プレビュー", viewModel.ValidationMessage);
    }

    [Fact]
    public void DirectionText_SetToShort_ForcesIsMarginTrue()
    {
        using var contextFactory = new SqliteInMemoryContextFactory();
        var viewModel = new OpenPositionViewModel(BuildService(contextFactory)) { IsMargin = false };

        viewModel.DirectionText = "Short";

        Assert.True(viewModel.IsMargin);
    }
}
