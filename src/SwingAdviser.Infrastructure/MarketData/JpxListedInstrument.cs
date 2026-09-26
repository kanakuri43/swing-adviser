using SwingAdviser.Domain.Stocks;

namespace SwingAdviser.Infrastructure.MarketData;

/// <summary>JPX銘柄一覧から抽出した、東証国内普通株（プライム/スタンダード/グロース）のみの1銘柄。</summary>
public sealed record JpxListedInstrument(string Code, string Name, MarketSegment Segment);
