using Microsoft.Extensions.Configuration;
using SwingAdviser.Domain.Strategy;
using SwingAdviser.Infrastructure.Configuration;

namespace SwingAdviser.Tests;

public class ConfigurationBindingTests
{
    private static IConfiguration LoadConfiguration()
    {
        return new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .Build();
    }

    [Fact]
    public void StrategyParameters_BindsNonDefaultValues()
    {
        var configuration = LoadConfiguration();

        var parameters = configuration.GetSection("StrategyParameters").Get<StrategyParameters>();

        Assert.NotNull(parameters);
        Assert.Equal(16, parameters!.Indicators.MacdFastPeriod);
        Assert.Equal(34, parameters.Indicators.MacdSlowPeriod);
        Assert.Equal(9, parameters.Indicators.MacdSignalPeriod);
        Assert.Equal(20, parameters.Indicators.EmaShortPeriod);
        Assert.Equal(100, parameters.Indicators.EmaMediumPeriod);
        Assert.Equal(14, parameters.Indicators.AtrPeriod);
        Assert.Equal(3, parameters.Gates.MacdCrossMaxAgeDays);
        Assert.Equal("1306", parameters.Gates.MarketRegimeSymbol);
        Assert.Equal(0.3m, parameters.Scoring.MomentumFullScoreAtrMultiple);
        Assert.Equal(70, parameters.Scoring.HighConfidenceThreshold);
        Assert.Equal(3.0m, parameters.Risk.LongStopLossAtrMultiple);
        Assert.Equal(250, parameters.AnalysisWindow.BarsToFetch);
    }

    [Fact]
    public void MarketDataOptions_BindsNonDefaultValues()
    {
        var configuration = LoadConfiguration();

        var options = configuration.GetSection("MarketData").Get<MarketDataOptions>();

        Assert.NotNull(options);
        Assert.Equal(5, options!.YahooFinance.MaxRequestsPerSecond);
        Assert.StartsWith("https://query1.finance.yahoo.com", options.YahooFinance.BaseUrl);
        Assert.Equal(400, options.YahooFinance.InitialFetchCalendarDays);
        Assert.Equal("16:00", options.YahooFinance.DailyBarFinalizedTimeJst);
        Assert.StartsWith("https://www.jpx.co.jp", options.Jpx.ListedIssuesUrl);
        Assert.Equal(7, options.Jpx.RefreshIntervalDays);
    }

    [Fact]
    public void LiquidityFilterOptions_BindsNonDefaultValues()
    {
        var configuration = LoadConfiguration();

        var options = configuration.GetSection("LiquidityFilter").Get<LiquidityFilterOptions>();

        Assert.NotNull(options);
        Assert.Equal(100_000_000m, options!.MinimumAverageTurnoverJpy);
        Assert.Equal(20, options.TurnoverAveragePeriodDays);
        Assert.Equal(7, options.RecheckIntervalDays);
    }

    [Fact]
    public void CodexCliOptions_BindsNonDefaultValues()
    {
        var configuration = LoadConfiguration();

        var options = configuration.GetSection("CodexCli").Get<CodexCliOptions>();

        Assert.NotNull(options);
        Assert.Equal(600, options!.TimeoutSeconds);
        Assert.Equal(4, options.MaxParallelism);
        Assert.Equal("medium", options.ReasoningEffort);
    }
}
