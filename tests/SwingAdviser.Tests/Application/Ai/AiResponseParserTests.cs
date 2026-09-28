using SwingAdviser.Application.Ai;
using SwingAdviser.Domain.Ai;
using SwingAdviser.Domain.Common;

namespace SwingAdviser.Tests.Application.Ai;

public class AiResponseParserTests
{
    private const string ValidJson = """
        {
          "verdict": "Bullish",
          "confidence": "High",
          "summary": "上昇トレンドが継続する見通し。",
          "positiveFactors": ["出来高増加"],
          "riskFactors": ["決算控え"],
          "invalidationConditions": ["EMA20を明確に割り込む"],
          "referenceUrls": ["https://example.com/a", "not-a-url", "ftp://example.com/b"]
        }
        """;

    [Fact]
    public void Parse_ValidJson_ReturnsSuccessWithAllFields()
    {
        var result = AiResponseParser.Parse(ValidJson);

        Assert.True(result.Success);
        Assert.Equal(AiVerdict.Bullish, result.Verdict);
        Assert.Equal(ConfidenceLevel.High, result.Confidence);
        Assert.Equal("上昇トレンドが継続する見通し。", result.Summary);
        Assert.Equal(["出来高増加"], result.PositiveFactors);
        Assert.Equal(["決算控え"], result.RiskFactors);
        Assert.Equal(["EMA20を明確に割り込む"], result.InvalidationConditions);
    }

    [Fact]
    public void Parse_FiltersOutNonHttpUrls()
    {
        var result = AiResponseParser.Parse(ValidJson);

        Assert.Equal(["https://example.com/a"], result.ReferenceUrls);
    }

    [Fact]
    public void Parse_WrappedInCodeFenceAndProse_ExtractsJsonObject()
    {
        var wrapped = $"""
            以下が調査結果です。

            ```json
            {ValidJson}
            ```

            ご確認ください。
            """;

        var result = AiResponseParser.Parse(wrapped);

        Assert.True(result.Success);
        Assert.Equal(AiVerdict.Bullish, result.Verdict);
    }

    [Fact]
    public void Parse_LowercaseEnumValues_AreAcceptedCaseInsensitively()
    {
        const string json = """{ "verdict": "bearish", "confidence": "low", "summary": "弱気。" }""";

        var result = AiResponseParser.Parse(json);

        Assert.True(result.Success);
        Assert.Equal(AiVerdict.Bearish, result.Verdict);
        Assert.Equal(ConfidenceLevel.Low, result.Confidence);
    }

    [Fact]
    public void Parse_InvalidVerdict_ReturnsFailure()
    {
        const string json = """{ "verdict": "とても強気", "confidence": "High", "summary": "要約。" }""";

        var result = AiResponseParser.Parse(json);

        Assert.False(result.Success);
        Assert.NotNull(result.FailureReason);
    }

    [Fact]
    public void Parse_MissingSummary_ReturnsFailure()
    {
        const string json = """{ "verdict": "Neutral", "confidence": "Medium" }""";

        var result = AiResponseParser.Parse(json);

        Assert.False(result.Success);
    }

    [Fact]
    public void Parse_NoJsonObjectPresent_ReturnsFailure()
    {
        var result = AiResponseParser.Parse("調査を完了できませんでした。");

        Assert.False(result.Success);
    }

    [Fact]
    public void Parse_EmptyInput_ReturnsFailure()
    {
        var result = AiResponseParser.Parse(string.Empty);

        Assert.False(result.Success);
    }
}
