using SwingAdviser.Infrastructure.Persistence;

namespace SwingAdviser.Tests;

public class DatabasePathResolverTests
{
    [Fact]
    public void ResolveWritableDatabasePath_ReturnsPathNextToBaseDirectory_AndLeavesNoProbeFile()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var path = DatabasePathResolver.ResolveWritableDatabasePath(directory.FullName);

            Assert.Equal(Path.Combine(directory.FullName, "swing-adviser.db"), path);
            Assert.Empty(Directory.GetFiles(directory.FullName));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void ResolveWritableDatabasePath_ThrowsInvalidOperationException_WhenDirectoryDoesNotExist()
    {
        var nonExistentDirectory = Path.Combine(Path.GetTempPath(), $"swing-adviser-missing-{Guid.NewGuid():N}");

        Assert.Throws<InvalidOperationException>(
            () => DatabasePathResolver.ResolveWritableDatabasePath(nonExistentDirectory));
    }
}
