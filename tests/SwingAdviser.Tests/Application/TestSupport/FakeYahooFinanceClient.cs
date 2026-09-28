using System.Collections.Concurrent;
using SwingAdviser.Infrastructure.MarketData;

namespace SwingAdviser.Tests.Application.TestSupport;

public sealed class FakeYahooFinanceClient : IYahooFinanceClient
{
    private readonly ConcurrentDictionary<string, Func<DateOnly, (IReadOnlyList<FetchedDailyBar> Bars, IReadOnlyList<YahooSplitEvent> Splits)>> _responses = new();

    public ConcurrentBag<(string Code, DateOnly Since)> Calls { get; } = [];

    public void SetResponse(string code, IReadOnlyList<FetchedDailyBar> bars, IReadOnlyList<YahooSplitEvent>? splits = null) =>
        _responses[code] = _ => (bars, splits ?? []);

    public void SetResponse(string code, Func<DateOnly, (IReadOnlyList<FetchedDailyBar> Bars, IReadOnlyList<YahooSplitEvent> Splits)> factory) =>
        _responses[code] = factory;

    public void SetFailure(string code, Exception exception) => _responses[code] = _ => throw exception;

    public Task<(IReadOnlyList<FetchedDailyBar> Bars, IReadOnlyList<YahooSplitEvent> Splits)> FetchAsync(
        string code, DateOnly since, CancellationToken cancellationToken = default)
    {
        Calls.Add((code, since));
        if (!_responses.TryGetValue(code, out var factory))
        {
            throw new InvalidOperationException($"FakeYahooFinanceClientに{code}の応答が設定されていません。");
        }

        return Task.FromResult(factory(since));
    }
}
