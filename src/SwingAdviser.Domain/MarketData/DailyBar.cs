namespace SwingAdviser.Domain.MarketData;

/// <summary>
/// 銘柄+取引日で一意。分割・併合は raw/調整後を二重保持せず、過去行を素直に UPDATE する（<see cref="ApplySplit"/>）。
/// </summary>
public class DailyBar
{
    private DailyBar()
    {
    } // EF Core

    public DailyBar(string stockCode, DateOnly tradeDate, decimal open, decimal high, decimal low, decimal close, long volume)
    {
        if (string.IsNullOrWhiteSpace(stockCode))
        {
            throw new ArgumentException("証券コードは必須です。", nameof(stockCode));
        }

        if (high < low)
        {
            throw new ArgumentException("高値は安値以上である必要があります。");
        }

        if (volume < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(volume));
        }

        StockCode = stockCode;
        TradeDate = tradeDate;
        Open = open;
        High = high;
        Low = low;
        Close = close;
        Volume = volume;
    }

    public long Id { get; private set; }

    public string StockCode { get; private set; } = string.Empty;

    public DateOnly TradeDate { get; private set; }

    public decimal Open { get; private set; }

    public decimal High { get; private set; }

    public decimal Low { get; private set; }

    public decimal Close { get; private set; }

    public long Volume { get; private set; }

    /// <summary>ratio=2 は1:2分割（株数2倍）、ratio=0.1 は10:1併合（株数1/10）を表す。</summary>
    public void ApplySplit(decimal ratio)
    {
        if (ratio <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ratio));
        }

        Open /= ratio;
        High /= ratio;
        Low /= ratio;
        Close /= ratio;
        Volume = (long)Math.Round(Volume * ratio, MidpointRounding.AwayFromZero);
    }
}
