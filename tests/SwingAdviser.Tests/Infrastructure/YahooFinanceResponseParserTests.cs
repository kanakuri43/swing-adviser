using SwingAdviser.Infrastructure.MarketData;

namespace SwingAdviser.Tests.Infrastructure;

public class YahooFinanceResponseParserTests
{
    private static long UnixSeconds(int year, int month, int day, int hour = 0) =>
        new DateTimeOffset(year, month, day, hour, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();

    private static string BuildChartJson(string timestamps, string opens, string highs, string lows, string closes, string volumes, string? eventsJson = null)
    {
        var eventsSection = eventsJson is null ? string.Empty : $",\"events\":{eventsJson}";
        return $$"""
        {
          "chart": {
            "result": [
              {
                "timestamp": [{{timestamps}}],
                "indicators": {
                  "quote": [
                    { "open": [{{opens}}], "high": [{{highs}}], "low": [{{lows}}], "close": [{{closes}}], "volume": [{{volumes}}] }
                  ]
                }
                {{eventsSection}}
              }
            ],
            "error": null
          }
        }
        """;
    }

    [Fact]
    public void Parse_ValidResponse_ReturnsBarsInOrder()
    {
        // 2024-01-04 09:00 JST（=2024-01-04 00:00 UTC）と2024-01-05を想定。
        var t1 = UnixSeconds(2024, 1, 4);
        var t2 = UnixSeconds(2024, 1, 5);
        var json = BuildChartJson(
            $"{t1},{t2}",
            "1000.0,1010.0",
            "1050.0,1060.0",
            "990.0,1000.0",
            "1020.0,1030.0",
            "200000,210000");

        var (bars, splits) = YahooFinanceResponseParser.Parse(json);

        Assert.Equal(2, bars.Count);
        Assert.Equal(new DateOnly(2024, 1, 4), bars[0].TradeDate);
        Assert.Equal(1000.0m, bars[0].Open);
        Assert.Equal(1050.0m, bars[0].High);
        Assert.Equal(990.0m, bars[0].Low);
        Assert.Equal(1020.0m, bars[0].Close);
        Assert.Equal(200000L, bars[0].Volume);
        Assert.Equal(new DateOnly(2024, 1, 5), bars[1].TradeDate);
        Assert.Empty(splits);
    }

    [Fact]
    public void Parse_EventsSplits_ExtractsRatioFromNumeratorDenominator()
    {
        var t1 = UnixSeconds(2024, 1, 4);
        var splitDate = UnixSeconds(2024, 1, 5);
        var json = BuildChartJson(
            $"{t1}",
            "1000.0",
            "1050.0",
            "990.0",
            "1020.0",
            "200000",
            $$"""{ "splits": { "{{splitDate}}": { "date": {{splitDate}}, "numerator": 2, "denominator": 1, "splitRatio": "2:1" } } }""");

        var (_, splits) = YahooFinanceResponseParser.Parse(json);

        var split = Assert.Single(splits);
        Assert.Equal(new DateOnly(2024, 1, 5), split.EffectiveDate);
        Assert.Equal(2m, split.Ratio); // numerator/denominator = 2/1
    }

    [Fact]
    public void Parse_ReverseSplitRatioLessThanOne()
    {
        var t1 = UnixSeconds(2024, 1, 4);
        var splitDate = UnixSeconds(2024, 1, 5);
        var json = BuildChartJson(
            $"{t1}",
            "1000.0",
            "1050.0",
            "990.0",
            "1020.0",
            "200000",
            $$"""{ "splits": { "{{splitDate}}": { "date": {{splitDate}}, "numerator": 1, "denominator": 10, "splitRatio": "1:10" } } }""");

        var (_, splits) = YahooFinanceResponseParser.Parse(json);

        var split = Assert.Single(splits);
        Assert.Equal(0.1m, split.Ratio); // 1:10併合 → numerator/denominator = 0.1
    }

    [Fact]
    public void Parse_NullBar_IsSkippedAsNotYetListed()
    {
        var t1 = UnixSeconds(2024, 1, 4);
        var t2 = UnixSeconds(2024, 1, 5);
        var json = BuildChartJson(
            $"{t1},{t2}",
            "null,1010.0",
            "null,1060.0",
            "null,1000.0",
            "null,1030.0",
            "null,210000");

        var (bars, _) = YahooFinanceResponseParser.Parse(json);

        var bar = Assert.Single(bars);
        Assert.Equal(new DateOnly(2024, 1, 5), bar.TradeDate);
    }

    [Fact]
    public void Parse_HighLessThanLow_Throws()
    {
        var t1 = UnixSeconds(2024, 1, 4);
        var json = BuildChartJson($"{t1}", "1000.0", "990.0", "1000.0", "995.0", "200000");

        Assert.Throws<InvalidOperationException>(() => YahooFinanceResponseParser.Parse(json));
    }

    [Fact]
    public void Parse_EmptyResultArray_Throws()
    {
        const string json = """
        { "chart": { "result": [], "error": null } }
        """;

        Assert.Throws<InvalidOperationException>(() => YahooFinanceResponseParser.Parse(json));
    }

    [Fact]
    public void Parse_AllBarsNull_Throws()
    {
        var t1 = UnixSeconds(2024, 1, 4);
        var json = BuildChartJson($"{t1}", "null", "null", "null", "null", "null");

        Assert.Throws<InvalidOperationException>(() => YahooFinanceResponseParser.Parse(json));
    }
}
