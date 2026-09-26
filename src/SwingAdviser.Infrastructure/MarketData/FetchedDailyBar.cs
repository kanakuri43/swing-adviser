namespace SwingAdviser.Infrastructure.MarketData;

/// <summary>Yahoo Financeから取得した検証済みの生バー（Domain.MarketData.DailyBarへ変換する前の値）。</summary>
public sealed record FetchedDailyBar(DateOnly TradeDate, decimal Open, decimal High, decimal Low, decimal Close, long Volume);
