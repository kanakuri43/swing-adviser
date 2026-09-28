namespace SwingAdviser.Tests.Application.TestSupport;

/// <summary>テスト内で時刻を固定・進行できる<see cref="TimeProvider"/>。</summary>
public sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public DateTimeOffset UtcNow { get; set; } = utcNow;

    public override DateTimeOffset GetUtcNow() => UtcNow;
}
