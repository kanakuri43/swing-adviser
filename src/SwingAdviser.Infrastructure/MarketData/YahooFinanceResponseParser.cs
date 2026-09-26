using System.Text.Json;

namespace SwingAdviser.Infrastructure.MarketData;

/// <summary>
/// Yahoo Finance chart API（v8/finance/chart）のJSONレスポンスを解析する純粋関数。HTTP通信は行わない。
/// </summary>
public static class YahooFinanceResponseParser
{
    private static readonly TimeZoneInfo JstTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time");

    public static (IReadOnlyList<FetchedDailyBar> Bars, IReadOnlyList<YahooSplitEvent> Splits) Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var chart = document.RootElement.GetProperty("chart");

        if (!chart.TryGetProperty("result", out var resultArray)
            || resultArray.ValueKind != JsonValueKind.Array
            || resultArray.GetArrayLength() != 1)
        {
            throw new InvalidOperationException(
                "Yahoo Financeのレスポンスにresultが1件見つかりません（廃止銘柄または無効な証券コードの可能性があります）。");
        }

        var result = resultArray[0];
        var timestamps = result.GetProperty("timestamp");
        var quote = result.GetProperty("indicators").GetProperty("quote")[0];

        var opens = quote.GetProperty("open");
        var highs = quote.GetProperty("high");
        var lows = quote.GetProperty("low");
        var closes = quote.GetProperty("close");
        var volumes = quote.GetProperty("volume");

        var count = timestamps.GetArrayLength();
        var bars = new List<FetchedDailyBar>(count);

        for (var i = 0; i < count; i++)
        {
            // 上場前など、当日分のOHLCVがすべてnullの日は「まだ取引がない」として読み飛ばす。
            if (opens[i].ValueKind == JsonValueKind.Null
                || highs[i].ValueKind == JsonValueKind.Null
                || lows[i].ValueKind == JsonValueKind.Null
                || closes[i].ValueKind == JsonValueKind.Null
                || volumes[i].ValueKind == JsonValueKind.Null)
            {
                continue;
            }

            var tradeDate = ToJstDate(timestamps[i].GetInt64());
            var open = opens[i].GetDecimal();
            var high = highs[i].GetDecimal();
            var low = lows[i].GetDecimal();
            var close = closes[i].GetDecimal();
            var volume = volumes[i].GetInt64();

            if (open <= 0 || high <= 0 || low <= 0 || close <= 0 || volume < 0 || high < low)
            {
                throw new InvalidOperationException($"Yahoo Financeのレスポンスに不正な値があります（{tradeDate:yyyy-MM-dd}）。");
            }

            bars.Add(new FetchedDailyBar(tradeDate, open, high, low, close, volume));
        }

        if (bars.Count == 0)
        {
            throw new InvalidOperationException("Yahoo Financeのレスポンスに有効なバーが1件もありません。");
        }

        var splits = new List<YahooSplitEvent>();
        if (result.TryGetProperty("events", out var events) && events.TryGetProperty("splits", out var splitsElement))
        {
            foreach (var property in splitsElement.EnumerateObject())
            {
                var action = property.Value;
                var numerator = action.GetProperty("numerator").GetDecimal();
                var denominator = action.GetProperty("denominator").GetDecimal();

                if (denominator == 0)
                {
                    continue;
                }

                var effectiveDate = ToJstDate(action.GetProperty("date").GetInt64());
                splits.Add(new YahooSplitEvent(effectiveDate, numerator / denominator));
            }
        }

        return (bars, splits);
    }

    private static DateOnly ToJstDate(long unixSeconds)
    {
        var utc = DateTimeOffset.FromUnixTimeSeconds(unixSeconds).UtcDateTime;
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, JstTimeZone));
    }
}
