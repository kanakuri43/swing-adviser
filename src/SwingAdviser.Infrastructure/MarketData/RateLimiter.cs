namespace SwingAdviser.Infrastructure.MarketData;

/// <summary>
/// 「次に開始してよい時刻」だけを保持する軽量なレート制御。同時実行の多重は許容し、開始タイミングのみ律速する。
/// </summary>
public sealed class RateLimiter
{
    private readonly TimeSpan _interval;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTime _nextStartUtc = DateTime.MinValue;

    public RateLimiter(int maxRequestsPerSecond)
    {
        if (maxRequestsPerSecond <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxRequestsPerSecond));
        }

        _interval = TimeSpan.FromSeconds(1.0 / maxRequestsPerSecond);
    }

    public async Task WaitForTurnAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var now = DateTime.UtcNow;
            var delay = _nextStartUtc - now;
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                now = DateTime.UtcNow;
            }

            _nextStartUtc = now + _interval;
        }
        finally
        {
            _gate.Release();
        }
    }
}
