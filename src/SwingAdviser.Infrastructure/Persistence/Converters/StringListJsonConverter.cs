using System.Text.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace SwingAdviser.Infrastructure.Persistence.Converters;

/// <summary>
/// 文字列リスト（Execution.CorrectionLog、AiEvaluationの好材料/リスク要因/無効化条件/参照URL）を
/// 1つのTEXT列にJSON配列として保存する（子テーブルを増やさないため）。
/// </summary>
public static class StringListJsonConverter
{
    public static ValueConverter<List<string>, string> Converter { get; } = new(
        v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
        v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>());

    public static ValueComparer<List<string>> Comparer { get; } = new(
        (a, b) => (a ?? new List<string>()).SequenceEqual(b ?? new List<string>()),
        v => v.Aggregate(0, (hash, s) => HashCode.Combine(hash, s.GetHashCode())),
        v => v.ToList());
}
