using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SwingAdviser.Domain.Risk;

namespace SwingAdviser.Infrastructure.Persistence.Configurations;

public sealed class HoldingEvaluationConfiguration : IEntityTypeConfiguration<HoldingEvaluation>
{
    public void Configure(EntityTypeBuilder<HoldingEvaluation> builder)
    {
        builder.ToTable("holding_evaluations");
        builder.HasKey(h => h.Id);
        builder.Property(h => h.Id).HasColumnName("holding_evaluation_id");
        builder.HasIndex(h => new { h.PositionId, h.EvaluationDate });
    }
}
