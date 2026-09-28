using SwingAdviser.Infrastructure.Analysis;

namespace SwingAdviser.Tests.Application.TestSupport;

public sealed class FakeAiCliExecutor(Func<string, CancellationToken, Task<AiCliResult>> handler) : IAiCliExecutor
{
    public List<string> Prompts { get; } = [];

    public Task<AiCliResult> ExecuteAsync(string prompt, CancellationToken cancellationToken = default)
    {
        lock (Prompts)
        {
            Prompts.Add(prompt);
        }

        return handler(prompt, cancellationToken);
    }
}
