using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SwingAdviser.Domain.Positions;
using SwingAdviser.Infrastructure.Persistence.Converters;

namespace SwingAdviser.Infrastructure.Persistence.Configurations;

public sealed class ExecutionConfiguration : IEntityTypeConfiguration<Execution>
{
    public void Configure(EntityTypeBuilder<Execution> builder)
    {
        builder.ToTable("executions");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("execution_id");

        // 監査原票（Price/Quantity）から導出される計算専用プロパティは永続化しない。
        builder.Ignore(e => e.AdjustedPrice);
        builder.Ignore(e => e.AdjustedQuantity);

        // CorrectionLogはpublicにはgetオンリーなので、バッキングフィールドを直接JSON列として永続化する。
        builder.Ignore(e => e.CorrectionLog);
        builder.Property<List<string>>("_correctionLog")
            .HasColumnName("correction_log")
            .HasConversion(StringListJsonConverter.Converter, StringListJsonConverter.Comparer);
    }
}
