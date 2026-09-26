namespace SwingAdviser.Domain.Stocks;

/// <summary>
/// 証券コード（StockCode）を自然キーとして扱う。JPX取込・分析・保有のすべての外部キーがこれを参照する。
/// </summary>
public class Stock
{
    private Stock()
    {
    } // EF Core

    public Stock(string stockCode, string name, MarketSegment marketSegment, bool isActive, DateTime updatedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(stockCode))
        {
            throw new ArgumentException("証券コードは必須です。", nameof(stockCode));
        }

        StockCode = stockCode;
        Name = name;
        MarketSegment = marketSegment;
        IsActive = isActive;
        UpdatedAtUtc = updatedAtUtc;
    }

    public string StockCode { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public MarketSegment MarketSegment { get; private set; }

    public bool IsActive { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public void Update(string name, MarketSegment marketSegment, bool isActive, DateTime nowUtc)
    {
        Name = name;
        MarketSegment = marketSegment;
        IsActive = isActive;
        UpdatedAtUtc = nowUtc;
    }
}
