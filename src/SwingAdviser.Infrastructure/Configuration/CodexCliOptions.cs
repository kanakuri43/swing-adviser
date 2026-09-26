namespace SwingAdviser.Infrastructure.Configuration;

public sealed class CodexCliOptions
{
    public string ExecutablePath { get; init; } = string.Empty;
    public int TimeoutSeconds { get; init; }
    public int MaxParallelism { get; init; }
}
