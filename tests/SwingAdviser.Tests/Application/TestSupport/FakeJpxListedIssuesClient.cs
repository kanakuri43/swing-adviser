using SwingAdviser.Infrastructure.MarketData;

namespace SwingAdviser.Tests.Application.TestSupport;

public sealed class FakeJpxListedIssuesClient : IJpxListedIssuesClient
{
    private Func<IReadOnlyList<JpxListedInstrument>> _factory = () => [];

    public int CallCount { get; private set; }

    public void SetResponse(IReadOnlyList<JpxListedInstrument> instruments) => _factory = () => instruments;

    public void SetFailure(Exception exception) => _factory = () => throw exception;

    public Task<IReadOnlyList<JpxListedInstrument>> FetchAsync(CancellationToken cancellationToken = default)
    {
        CallCount++;
        return Task.FromResult(_factory());
    }
}
