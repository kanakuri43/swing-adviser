using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SwingAdviser.Domain.MarketData;

namespace SwingAdviser.Infrastructure.Persistence.Configurations;

public sealed class DailyBarConfiguration : IEntityTypeConfiguration<DailyBar>
{
    public void Configure(EntityTypeBuilder<DailyBar> builder)
    {
        builder.ToTable("daily_bars");
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).HasColumnName("daily_bar_id");
        builder.HasIndex(b => new { b.StockCode, b.TradeDate }).IsUnique();
    }
}
