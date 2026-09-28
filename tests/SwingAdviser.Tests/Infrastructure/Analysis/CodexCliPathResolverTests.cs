using SwingAdviser.Infrastructure.Analysis;

namespace SwingAdviser.Tests.Infrastructure.Analysis;

public class CodexCliPathResolverTests
{
    private const string EnvironmentVariableName = "SWING_ADVISER_CODEX_PATH";

    [Fact]
    public void Resolve_EnvironmentVariableSet_ReturnsItDirectlyWithoutSearching()
    {
        var previous = Environment.GetEnvironmentVariable(EnvironmentVariableName);
        try
        {
            Environment.SetEnvironmentVariable(EnvironmentVariableName, @"D:\custom\codex.exe");

            var resolved = CodexCliPathResolver.Resolve();

            Assert.Equal(@"D:\custom\codex.exe", resolved);
        }
        finally
        {
            Environment.SetEnvironmentVariable(EnvironmentVariableName, previous);
        }
    }

    [Fact]
    public void FindExecutableInDirectory_DirectChild_ReturnsIt()
    {
        var root = Directory.CreateTempSubdirectory("swing-adviser-codex-");
        try
        {
            var executable = Path.Combine(root.FullName, "codex.exe");
            File.WriteAllText(executable, "stub");

            Assert.Equal(executable, CodexCliPathResolver.FindExecutableInDirectory(root.FullName));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void FindExecutableInDirectory_NestedUnderVersionedSubdirectory_ReturnsNewest()
    {
        var root = Directory.CreateTempSubdirectory("swing-adviser-codex-");
        try
        {
            var older = Directory.CreateDirectory(Path.Combine(root.FullName, "0.1.0"));
            var newer = Directory.CreateDirectory(Path.Combine(root.FullName, "0.2.0"));
            var olderExecutable = Path.Combine(older.FullName, "codex.exe");
            var newerExecutable = Path.Combine(newer.FullName, "codex.exe");
            File.WriteAllText(olderExecutable, "stub");
            File.WriteAllText(newerExecutable, "stub");
            File.SetLastWriteTimeUtc(olderExecutable, DateTime.UtcNow.AddMinutes(-10));
            File.SetLastWriteTimeUtc(newerExecutable, DateTime.UtcNow);

            Assert.Equal(newerExecutable, CodexCliPathResolver.FindExecutableInDirectory(root.FullName));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void FindExecutableInDirectory_DirectoryDoesNotExist_ReturnsNull()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"swing-adviser-missing-{Guid.NewGuid():N}");

        Assert.Null(CodexCliPathResolver.FindExecutableInDirectory(missing));
    }

    [Fact]
    public void FindExecutableInDirectory_DirectoryWithoutCodexExe_ReturnsNull()
    {
        var root = Directory.CreateTempSubdirectory("swing-adviser-codex-empty-");
        try
        {
            Assert.Null(CodexCliPathResolver.FindExecutableInDirectory(root.FullName));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }
}
