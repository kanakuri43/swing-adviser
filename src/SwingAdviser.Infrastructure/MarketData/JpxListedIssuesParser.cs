using System.Text;
using ExcelDataReader;
using SwingAdviser.Domain.Stocks;

namespace SwingAdviser.Infrastructure.MarketData;

/// <summary>
/// JPX「上場銘柄一覧」（Excel/CSV/TSV）を解析する純粋関数。HTTP通信は行わない。
/// CLAUDE.mdの対象範囲（東証上場の国内普通株）に合わないETF/REIT/外国株・プライム/スタンダード/グロース以外は
/// ここで除外する（Stockエンティティに「対象外」概念を持たせないため）。
/// </summary>
public static class JpxListedIssuesParser
{
    private static readonly byte[] Ole2Magic = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    static JpxListedIssuesParser()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static IReadOnlyList<JpxListedInstrument> Parse(byte[] content, string fileName)
    {
        var rows = IsExcelWorkbook(content, fileName) ? ReadExcelRows(content) : ReadDelimitedTextRows(content);
        return Classify(rows);
    }

    private static bool IsExcelWorkbook(byte[] content, string fileName)
    {
        if (fileName.EndsWith(".xls", StringComparison.OrdinalIgnoreCase) || fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return content.Length >= Ole2Magic.Length && content.AsSpan(0, Ole2Magic.Length).SequenceEqual(Ole2Magic);
    }

    private static List<string[]> ReadExcelRows(byte[] content)
    {
        using var stream = new MemoryStream(content);
        using var reader = ExcelReaderFactory.CreateReader(stream);

        var rows = new List<string[]>();
        while (reader.Read())
        {
            var row = new string[reader.FieldCount];
            for (var i = 0; i < reader.FieldCount; i++)
            {
                row[i] = reader.GetValue(i)?.ToString() ?? string.Empty;
            }

            rows.Add(row);
        }

        return rows;
    }

    private static List<string[]> ReadDelimitedTextRows(byte[] content)
    {
        var text = Encoding.UTF8.GetString(content);
        var lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0)
        {
            return [];
        }

        var delimiter = lines[0].Count(c => c == '\t') > lines[0].Count(c => c == ',') ? '\t' : ',';
        return lines.Select(line => line.Split(delimiter)).ToList();
    }

    private static IReadOnlyList<JpxListedInstrument> Classify(List<string[]> rows)
    {
        if (rows.Count == 0)
        {
            throw new InvalidOperationException("JPX銘柄一覧が空です。");
        }

        var header = rows[0];
        var codeIndex = FindColumn(header, "コード", "code");
        var nameIndex = FindColumn(header, "銘柄名", "name");
        var segmentIndex = FindColumn(header, "市場・商品区分", "市場商品区分", "市場区分", "marketsegment");

        if (codeIndex < 0 || nameIndex < 0 || segmentIndex < 0)
        {
            throw new InvalidOperationException("JPX銘柄一覧のヘッダー列（コード/銘柄名/市場区分）が見つかりません。");
        }

        var maxIndex = Math.Max(codeIndex, Math.Max(nameIndex, segmentIndex));
        var instruments = new List<JpxListedInstrument>();

        for (var i = 1; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.Length <= maxIndex)
            {
                continue;
            }

            var code = row[codeIndex].Trim();
            var name = row[nameIndex].Trim();
            var segmentValue = row[segmentIndex].Trim();

            if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(segmentValue) || !segmentValue.Contains("内国株"))
            {
                continue; // ETF/ETN/REIT/外国株等は対象外
            }

            MarketSegment? segment = segmentValue.Contains("プライム") ? MarketSegment.Prime
                : segmentValue.Contains("スタンダード") ? MarketSegment.Standard
                : segmentValue.Contains("グロース") ? MarketSegment.Growth
                : null;

            if (segment is null)
            {
                continue; // プライム/スタンダード/グロース以外は対象外
            }

            instruments.Add(new JpxListedInstrument(code, name, segment.Value));
        }

        return instruments;
    }

    private static int FindColumn(string[] header, params string[] candidates)
    {
        for (var i = 0; i < header.Length; i++)
        {
            var normalized = Normalize(header[i]);
            if (candidates.Any(c => normalized == Normalize(c)))
            {
                return i;
            }
        }

        return -1;
    }

    private static string Normalize(string value) =>
        value.Replace(" ", string.Empty).Replace("　", string.Empty).Replace("・", string.Empty).Trim().ToLowerInvariant();
}
