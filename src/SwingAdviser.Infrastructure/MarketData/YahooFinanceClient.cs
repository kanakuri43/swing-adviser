using SwingAdviser.Infrastructure.Configuration;

namespace SwingAdviser.Infrastructure.MarketData;

public interface IYahooFinanceClient
{
    /// <summary>
    /// <paramref name="since"/>以降の日足を取得する。フル取得か差分取得かの判断は呼び出し側（Application層）が行う。
    /// </summary>
    Task<(IReadOnlyList<FetchedDailyBar> Bars, IReadOnlyList<YahooSplitEvent> Splits)> FetchAsync(
        string code, DateOnly since, CancellationToken cancellationToken = default);
}

public sealed class YahooFinanceClient : IYahooFinanceClient, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly RateLimiter _rateLimiter;
    private readonly string _baseUrl;

    public YahooFinanceClient(YahooFinanceOptions options)
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds) };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("SwingAdviser/1.0 (decision-support; no-ordering)");
        _rateLimiter = new RateLimiter(options.MaxRequestsPerSecond);
        _baseUrl = options.BaseUrl;
    }

    public async Task<(IReadOnlyList<FetchedDailyBar> Bars, IReadOnlyList<YahooSplitEvent> Splits)> FetchAsync(
        string code, DateOnly since, CancellationToken cancellationToken = default)
    {
        await _rateLimiter.WaitForTurnAsync(cancellationToken).ConfigureAwait(false);

        var symbol = $"{code.ToUpperInvariant()}.T";
        var period1 = new DateTimeOffset(since.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToUnixTimeSeconds();
        var period2 = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var url = $"{_baseUrl}{symbol}?period1={period1}&period2={period2}&interval=1d&events=split";

        using var response = await _httpClient
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return YahooFinanceResponseParser.Parse(json);
    }

    public void Dispose() => _httpClient.Dispose();
}
