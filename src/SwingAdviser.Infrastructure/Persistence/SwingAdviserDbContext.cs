using Microsoft.EntityFrameworkCore;
using SwingAdviser.Domain.Ai;
using SwingAdviser.Domain.Analysis;
using SwingAdviser.Domain.Common;
using SwingAdviser.Domain.MarketData;
using SwingAdviser.Domain.Positions;
using SwingAdviser.Domain.Risk;
using SwingAdviser.Domain.Stocks;
using SwingAdviser.Infrastructure.Persistence.Converters;

namespace SwingAdviser.Infrastructure.Persistence;

public sealed class SwingAdviserDbContext(DbContextOptions<SwingAdviserDbContext> options) : DbContext(options)
{
    public DbSet<Stock> Stocks => Set<Stock>();

    public DbSet<DailyBar> DailyBars => Set<DailyBar>();

    public DbSet<CandidateEvaluation> CandidateEvaluations => Set<CandidateEvaluation>();

    public DbSet<HoldingEvaluation> HoldingEvaluations => Set<HoldingEvaluation>();

    public DbSet<Position> Positions => Set<Position>();

    public DbSet<AiEvaluation> AiEvaluations => Set<AiEvaluation>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // 価格・数量・比率はSQLite REALでなくTEXT（10進文字列）として保存する（CLAUDE.md「データベース」節）。
        configurationBuilder.Properties<decimal>().HaveConversion<DecimalToStringConverter>();

        // 列挙型はSQLiteブラウザで開いても判定根拠が読めるよう文字列で保存する。
        // nullable列挙型（AiEvaluation.Direction/Confidence/Verdict）はここでは対象外にし、個別設定で明示する。
        configurationBuilder.Properties<TradeDirection>().HaveConversion<string>();
        configurationBuilder.Properties<MarketSegment>().HaveConversion<string>();
        configurationBuilder.Properties<ConfidenceLevel>().HaveConversion<string>();
        configurationBuilder.Properties<PositionStatus>().HaveConversion<string>();
        configurationBuilder.Properties<ExecutionSide>().HaveConversion<string>();
        configurationBuilder.Properties<HoldingDecision>().HaveConversion<string>();
        configurationBuilder.Properties<AiEvaluationStatus>().HaveConversion<string>();
        configurationBuilder.Properties<AiVerdict>().HaveConversion<string>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SwingAdviserDbContext).Assembly);
    }
}
