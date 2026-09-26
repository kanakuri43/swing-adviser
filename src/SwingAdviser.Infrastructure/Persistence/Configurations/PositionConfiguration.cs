using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SwingAdviser.Domain.Positions;

namespace SwingAdviser.Infrastructure.Persistence.Configurations;

public sealed class PositionConfiguration : IEntityTypeConfiguration<Position>
{
    public void Configure(EntityTypeBuilder<Position> builder)
    {
        builder.ToTable("positions");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("position_id");

        // 計算専用プロパティ（バッキングフィールドを持たない）は永続化しない。
        builder.Ignore(p => p.RemainingQuantity);
        builder.Ignore(p => p.AverageEntryPrice);
        builder.Ignore(p => p.OpenedDate);
        builder.Ignore(p => p.IsMarginDueUnconfirmed);

        // Executionsは _executions フィールドを直接読み書きする子エンティティ集合（監査原票のPrice/Quantityは変更しない設計）。
        builder.HasMany(p => p.Executions)
            .WithOne()
            .HasForeignKey("PositionId")
            .IsRequired();
        builder.Navigation(p => p.Executions).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
