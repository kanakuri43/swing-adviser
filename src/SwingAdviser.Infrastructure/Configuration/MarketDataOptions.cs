namespace SwingAdviser.Infrastructure.Configuration;

public sealed class MarketDataOptions
{
    public YahooFinanceOptions YahooFinance { get; init; } = new();
    public JpxOptions Jpx { get; init; } = new();
}

public sealed class YahooFinanceOptions
{
    public string BaseUrl { get; init; } = string.Empty;
    public int MaxRequestsPerSecond { get; init; }
    public int TimeoutSeconds { get; init; }
}

public sealed class JpxOptions
{
    public string ListedIssuesUrl { get; init; } = string.Empty;
}
