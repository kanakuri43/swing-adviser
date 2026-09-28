using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SwingAdviser.Infrastructure.Persistence;

namespace SwingAdviser.Tests.Application.TestSupport;

/// <summary>
/// 開いたままの:memory:接続を共有する<see cref="IDbContextFactory{SwingAdviserDbContext}"/>偽実装。
/// Applicationサービスは並列実行のため複数のDbContextを要求するので、単一コンテキストではなくこれを渡す。
/// </summary>
public sealed class SqliteInMemoryContextFactory : IDbContextFactory<SwingAdviserDbContext>, IDisposable
{
    private readonly SqliteConnection _connection;

    public SqliteInMemoryContextFactory()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        using var context = CreateDbContext();
        context.Database.EnsureCreated();
    }

    public SwingAdviserDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<SwingAdviserDbContext>()
            .UseSqlite(_connection)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new SwingAdviserDbContext(options);
    }

    public void Dispose() => _connection.Dispose();
}
