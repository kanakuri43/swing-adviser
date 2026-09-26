using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SwingAdviser.Domain.Analysis;

namespace SwingAdviser.Infrastructure.Persistence.Configurations;

public sealed class CandidateEvaluationConfiguration : IEntityTypeConfiguration<CandidateEvaluation>
{
    public void Configure(EntityTypeBuilder<CandidateEvaluation> builder)
    {
        builder.ToTable("candidate_evaluations");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("candidate_evaluation_id");
        builder.HasIndex(c => new { c.StockCode, c.EvaluationDate, c.Direction });

        // 命名規約変換（数字の直後の大文字）が "ema100twenty_days_ago" になり読みにくいため明示する。
        builder.Property(c => c.Ema100TwentyDaysAgo).HasColumnName("ema100_twenty_days_ago");
    }
}
