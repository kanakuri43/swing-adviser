using System.IO.Compression;
using Microsoft.Data.Sqlite;
using SwingAdviser.Infrastructure.Configuration;
using SwingAdviser.Infrastructure.Persistence;

namespace SwingAdviser.Tests.Infrastructure.Persistence;

public class DatabaseBackupServiceTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory();

    public void Dispose() => _root.Delete(recursive: true);

    private string CreateDatabase(string value)
    {
        var path = Path.Combine(_root.FullName, "swing-adviser.db");
        File.Delete(path);
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"CREATE TABLE t (v TEXT); INSERT INTO t VALUES ('{value}');";
        command.ExecuteNonQuery();
        return path;
    }

    private BackupOptions Options(int keepCount = 30) => new()
    {
        DestinationDirectory = Path.Combine(_root.FullName, "backup"),
        KeepCount = keepCount,
    };

    private static string ReadValueFromZip(string zipPath, string extractDirectory)
    {
        ZipFile.ExtractToDirectory(zipPath, extractDirectory);
        using var connection = new SqliteConnection(
            $"Data Source={Path.Combine(extractDirectory, "swing-adviser.db")};Mode=ReadOnly;Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT v FROM t";
        return (string)command.ExecuteScalar()!;
    }

    [Fact]
    public void Backup_CreatesDatedZipContainingValidDatabase()
    {
        var databasePath = CreateDatabase("first");

        var zipPath = DatabaseBackupService.Backup(databasePath, Options(), new DateOnly(2026, 9, 29));

        Assert.Equal(Path.Combine(_root.FullName, "backup", "swing-adviser_20260929.zip"), zipPath);
        Assert.Equal("first", ReadValueFromZip(zipPath!, Path.Combine(_root.FullName, "extract")));
        Assert.Single(Directory.GetFiles(Path.Combine(_root.FullName, "backup")));
    }

    [Fact]
    public void Backup_OverwritesSameDayBackup()
    {
        var day = new DateOnly(2026, 9, 29);
        DatabaseBackupService.Backup(CreateDatabase("first"), Options(), day);

        var zipPath = DatabaseBackupService.Backup(CreateDatabase("second"), Options(), day);

        Assert.Equal("second", ReadValueFromZip(zipPath!, Path.Combine(_root.FullName, "extract")));
        Assert.Single(Directory.GetFiles(Path.Combine(_root.FullName, "backup")));
    }

    [Fact]
    public void Backup_KeepsOnlyNewestKeepCountFiles()
    {
        var databasePath = CreateDatabase("x");
        for (var day = 1; day <= 5; day++)
        {
            DatabaseBackupService.Backup(databasePath, Options(keepCount: 3), new DateOnly(2026, 9, day));
        }

        var names = Directory.GetFiles(Path.Combine(_root.FullName, "backup"))
            .Select(f => Path.GetFileName(f)!)
            .Order()
            .ToArray();

        Assert.Equal(
            ["swing-adviser_20260903.zip", "swing-adviser_20260904.zip", "swing-adviser_20260905.zip"],
            names);
    }

    [Fact]
    public void Backup_ReturnsNullAndCreatesNothing_WhenDestinationIsEmpty()
    {
        var databasePath = CreateDatabase("x");

        var result = DatabaseBackupService.Backup(
            databasePath, new BackupOptions { DestinationDirectory = "" }, new DateOnly(2026, 9, 29));

        Assert.Null(result);
        Assert.False(Directory.Exists(Path.Combine(_root.FullName, "backup")));
    }
}
