using System.Globalization;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace SwingAdviser.Infrastructure.Persistence.Converters;

/// <summary>価格・数量・比率を SQLite REAL でなく TEXT（10進文字列）として保存する（CLAUDE.md「データベース」節）。</summary>
public sealed class DecimalToStringConverter : ValueConverter<decimal, string>
{
    public DecimalToStringConverter()
        : base(
            v => v.ToString(CultureInfo.InvariantCulture),
            v => decimal.Parse(v, CultureInfo.InvariantCulture))
    {
    }
}
