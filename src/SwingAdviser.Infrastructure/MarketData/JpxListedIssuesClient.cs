using SwingAdviser.Infrastructure.Configuration;

namespace SwingAdviser.Infrastructure.MarketData;

public interface IJpxListedIssuesClient
{
    Task<IReadOnlyList<JpxListedInstrument>> FetchAsync(CancellationToken cancellationToken = default);
}

public sealed class JpxListedIssuesClient : IJpxListedIssuesClient, IDisposable
{
    private readonly HttpClient _httpClient = new();
    private readonly string _url;

    public JpxListedIssuesClient(JpxOptions options)
    {
        _url = options.ListedIssuesUrl;
    }

    public async Task<IReadOnlyList<JpxListedInstrument>> FetchAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient
            .GetAsync(_url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        var fileName = _url[(_url.LastIndexOf('/') + 1)..];
        return JpxListedIssuesParser.Parse(content, fileName);
    }

    public void Dispose() => _httpClient.Dispose();
}
