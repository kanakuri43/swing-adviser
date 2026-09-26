using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SwingAdviser.Infrastructure.Persistence;

/// <summary>`dotnet ef migrations` 実行時にDIコンテナを介さずDbContextを生成するための設計時ファクトリ。</summary>
public sealed class SwingAdviserDbContextFactory : IDesignTimeDbContextFactory<SwingAdviserDbContext>
{
    public SwingAdviserDbContext CreateDbContext(string[] args)
    {
        var databasePath = Path.Combine(AppContext.BaseDirectory, "swing-adviser-design.db");

        var optionsBuilder = new DbContextOptionsBuilder<SwingAdviserDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .UseSnakeCaseNamingConvention();

        return new SwingAdviserDbContext(optionsBuilder.Options);
    }
}
