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

    /// <summary>保存済みバーが無い銘柄を初回取得する際に遡る暦日数（250営業日＋祝日の余裕）。</summary>
    public int InitialFetchCalendarDays { get; init; }

    /// <summary>この時刻（JST、"HH:mm"）より前は当日分のバーを未確定として保存しない。</summary>
    public string DailyBarFinalizedTimeJst { get; init; } = string.Empty;
}

public sealed class JpxOptions
{
    public string ListedIssuesUrl { get; init; } = string.Empty;

    /// <summary>銘柄マスタを再取得する間隔（日数）。</summary>
    public int RefreshIntervalDays { get; init; }
}
