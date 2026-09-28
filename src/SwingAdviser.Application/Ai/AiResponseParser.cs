using System.Text.Json;
using System.Text.Json.Serialization;
using SwingAdviser.Domain.Ai;
using SwingAdviser.Domain.Common;

namespace SwingAdviser.Application.Ai;

public sealed record AiResponseParseResult(
    bool Success,
    AiVerdict Verdict,
    ConfidenceLevel Confidence,
    string Summary,
    IReadOnlyList<string> PositiveFactors,
    IReadOnlyList<string> RiskFactors,
    IReadOnlyList<string> InvalidationConditions,
    string? FailureReason)
{
    public static AiResponseParseResult Failure(string reason) =>
        new(false, default, default, string.Empty, [], [], [], reason);

    public static AiResponseParseResult Ok(
        AiVerdict verdict,
        ConfidenceLevel confidence,
        string summary,
        IReadOnlyList<string> positiveFactors,
        IReadOnlyList<string> riskFactors,
        IReadOnlyList<string> invalidationConditions) =>
        new(true, verdict, confidence, summary, positiveFactors, riskFactors, invalidationConditions, null);
}

/// <summary>
/// Codex CLIの応答から ai結果のJSONを抜き出して解釈する。コードフェンスや前置きの文章が混ざっていても、
/// 最初の{から最後の}までを対象にすることで許容する。成功/失敗の2択で返す（多段のfail-closed分類は作らない）。
/// </summary>
public static class AiResponseParser
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static AiResponseParseResult Parse(string rawOutput)
    {
        if (string.IsNullOrWhiteSpace(rawOutput))
        {
            return AiResponseParseResult.Failure("応答が空でした。");
        }

        var start = rawOutput.IndexOf('{');
        var end = rawOutput.LastIndexOf('}');
        if (start < 0 || end < start)
        {
            return AiResponseParseResult.Failure("応答にJSONオブジェクトが見つかりませんでした。");
        }

        var json = rawOutput[start..(end + 1)];

        RawResponse? raw;
        try
        {
            raw = JsonSerializer.Deserialize<RawResponse>(json, Options);
        }
        catch (JsonException exception)
        {
            return AiResponseParseResult.Failure($"JSONの解析に失敗しました: {exception.Message}");
        }

        if (raw is null)
        {
            return AiResponseParseResult.Failure("応答のJSONがnullでした。");
        }

        if (string.IsNullOrWhiteSpace(raw.Summary))
        {
            return AiResponseParseResult.Failure("summaryが空です。");
        }

        if (!TryParseEnum<AiVerdict>(raw.Verdict, out var verdict))
        {
            return AiResponseParseResult.Failure($"verdictの値が不正です: {raw.Verdict}");
        }

        if (!TryParseEnum<ConfidenceLevel>(raw.Confidence, out var confidence))
        {
            return AiResponseParseResult.Failure($"confidenceの値が不正です: {raw.Confidence}");
        }

        return AiResponseParseResult.Ok(
            verdict,
            confidence,
            raw.Summary.Trim(),
            raw.PositiveFactors ?? [],
            raw.RiskFactors ?? [],
            raw.InvalidationConditions ?? []);
    }

    private static bool TryParseEnum<TEnum>(string? value, out TEnum result) where TEnum : struct, Enum
    {
        if (!string.IsNullOrWhiteSpace(value) && Enum.TryParse(value, ignoreCase: true, out result))
        {
            return true;
        }

        result = default;
        return false;
    }

    private sealed class RawResponse
    {
        public string? Verdict { get; set; }

        public string? Confidence { get; set; }

        public string? Summary { get; set; }

        [JsonPropertyName("positiveFactors")]
        public List<string>? PositiveFactors { get; set; }

        [JsonPropertyName("riskFactors")]
        public List<string>? RiskFactors { get; set; }

        [JsonPropertyName("invalidationConditions")]
        public List<string>? InvalidationConditions { get; set; }
    }
}
