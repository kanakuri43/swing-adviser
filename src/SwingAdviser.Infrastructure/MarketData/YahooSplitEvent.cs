namespace SwingAdviser.Infrastructure.MarketData;

/// <summary>
/// Yahoo Finance chart APIの`events.splits`から抽出した分割イベント。
/// Ratio = numerator/denominator（例: 1:2分割はnumerator=2,denominator=1→Ratio=2）で、
/// Domain.MarketData.DailyBar.ApplySplit(ratio)（価格÷ratio、出来高×ratio）とそのまま整合する。
/// </summary>
public sealed record YahooSplitEvent(DateOnly EffectiveDate, decimal Ratio);
