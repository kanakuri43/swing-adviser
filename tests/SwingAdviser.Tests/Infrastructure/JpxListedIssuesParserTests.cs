using System.Text;
using SwingAdviser.Domain.Stocks;
using SwingAdviser.Infrastructure.MarketData;

namespace SwingAdviser.Tests.Infrastructure;

public class JpxListedIssuesParserTests
{
    private static byte[] ToCsvBytes(string csv) => Encoding.UTF8.GetBytes(csv);

    [Fact]
    public void Parse_DomesticCommonStockInEligibleSegments_IsIncluded()
    {
        var csv = "コード,銘柄名,市場・商品区分\n"
            + "7203,トヨタ自動車,プライム（内国株式）\n"
            + "9984,ソフトバンクグループ,プライム（内国株式）\n"
            + "3350,メタプラネット,スタンダード（内国株式）\n"
            + "4485,JTOWER,グロース（内国株式）\n";

        var result = JpxListedIssuesParser.Parse(ToCsvBytes(csv), "data_j.csv");

        Assert.Equal(4, result.Count);
        Assert.Contains(result, i => i.Code == "7203" && i.Name == "トヨタ自動車" && i.Segment == MarketSegment.Prime);
        Assert.Contains(result, i => i.Code == "3350" && i.Segment == MarketSegment.Standard);
        Assert.Contains(result, i => i.Code == "4485" && i.Segment == MarketSegment.Growth);
    }

    [Fact]
    public void Parse_NonDomesticCommonStockOrIneligibleSegment_IsExcluded()
    {
        var csv = "コード,銘柄名,市場・商品区分\n"
            + "1306,TOPIX連動型上場投資信託,プライム（ETF・ETN）\n"
            + "8697,日本取引所グループ,プライム（内国株式）\n"
            + "9999,テスト外国株,プライム（外国株）\n";

        var result = JpxListedIssuesParser.Parse(ToCsvBytes(csv), "data_j.csv");

        var instrument = Assert.Single(result);
        Assert.Equal("8697", instrument.Code);
    }

    [Fact]
    public void Parse_HeaderWithFullWidthSpaceAndNakaguro_IsRecognized()
    {
        // ヘッダーの表記ゆれ（全角スペース混じりの列名、タブ区切り）を吸収できることを確認する。
        var csv = "コード　\t銘柄名\t市場・商品区分\n7203\tトヨタ自動車\tプライム（内国株式）\n";

        var result = JpxListedIssuesParser.Parse(ToCsvBytes(csv), "data_j.tsv");

        var instrument = Assert.Single(result);
        Assert.Equal("7203", instrument.Code);
        Assert.Equal(MarketSegment.Prime, instrument.Segment);
    }

    [Fact]
    public void Parse_MissingHeaderColumns_Throws()
    {
        var csv = "code,name\n7203,Toyota\n";

        Assert.Throws<InvalidOperationException>(() => JpxListedIssuesParser.Parse(ToCsvBytes(csv), "data_j.csv"));
    }
}
