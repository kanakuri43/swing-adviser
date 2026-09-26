namespace SwingAdviser.Domain.Stocks;

public enum MarketSegment
{
    Prime,
    Standard,
    Growth,

    /// <summary>地合い判定用の TOPIX 連動ETF（1306.T）だけに使う。</summary>
    Etf,
}
