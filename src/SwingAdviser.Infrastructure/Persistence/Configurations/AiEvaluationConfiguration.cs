using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SwingAdviser.Domain.Ai;
using SwingAdviser.Infrastructure.Persistence.Converters;

namespace SwingAdviser.Infrastructure.Persistence.Configurations;

public sealed class AiEvaluationConfiguration : IEntityTypeConfiguration<AiEvaluation>
{
    public void Configure(EntityTypeBuilder<AiEvaluation> builder)
    {
        builder.ToTable("ai_evaluations");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("ai_evaluation_id");

        // nullable列挙型はConfigureConventionsの対象外にしているため、ここで個別に文字列変換する。
        builder.Property(a => a.Direction).HasConversion<string>();
        builder.Property(a => a.Confidence).HasConversion<string>();
        builder.Property(a => a.Verdict).HasConversion<string>();

        MapStringListField(builder, "_positiveFactors", "positive_factors");
        MapStringListField(builder, "_riskFactors", "risk_factors");
        MapStringListField(builder, "_invalidationConditions", "invalidation_conditions");

        builder.Ignore(a => a.PositiveFactors);
        builder.Ignore(a => a.RiskFactors);
        builder.Ignore(a => a.InvalidationConditions);
    }

    private static void MapStringListField(EntityTypeBuilder<AiEvaluation> builder, string fieldName, string columnName)
    {
        builder.Property<List<string>>(fieldName)
            .HasColumnName(columnName)
            .HasConversion(StringListJsonConverter.Converter, StringListJsonConverter.Comparer);
    }
}
