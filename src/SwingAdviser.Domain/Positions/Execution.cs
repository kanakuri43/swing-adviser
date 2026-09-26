namespace SwingAdviser.Domain.Positions;

/// <summary>
/// 1件の新規/決済約定。監査原票なので Price/Quantity は分割・併合で書き換えない
/// （<see cref="ApplySplit"/> は SplitFactor だけを更新する）。訂正は <see cref="Correct"/> の UPDATE＋理由追記のみで、
/// revision チェーンは作らない。
/// </summary>
public class Execution
{
    private readonly List<string> _correctionLog = [];

    private Execution()
    {
    } // EF Core

    internal Execution(ExecutionSide side, DateTime executedAtUtc, decimal price, int quantity, DateOnly? marginDueDate)
    {
        if (price <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(price));
        }

        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity));
        }

        Side = side;
        ExecutedAtUtc = executedAtUtc;
        Price = price;
        Quantity = quantity;
        SplitFactor = 1m;
        MarginDueDate = marginDueDate;
    }

    public long Id { get; private set; }

    public ExecutionSide Side { get; private set; }

    public DateTime ExecutedAtUtc { get; private set; }

    /// <summary>約定価格（監査原票、分割調整しない）。</summary>
    public decimal Price { get; private set; }

    /// <summary>約定株数（監査原票、分割調整しない）。</summary>
    public int Quantity { get; private set; }

    /// <summary>この約定より後に発生した分割・併合の累積比率。既定1。</summary>
    public decimal SplitFactor { get; private set; } = 1m;

    public DateOnly? MarginDueDate { get; private set; }

    public IReadOnlyList<string> CorrectionLog => _correctionLog;

    public decimal AdjustedPrice => Price / SplitFactor;

    public decimal AdjustedQuantity => Quantity * SplitFactor;

    internal void ApplySplit(decimal ratio) => SplitFactor *= ratio;

    internal void Correct(decimal price, int quantity, DateTime executedAtUtc, string reason, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("訂正には理由が必要です。", nameof(reason));
        }

        if (price <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(price));
        }

        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity));
        }

        Price = price;
        Quantity = quantity;
        ExecutedAtUtc = executedAtUtc;
        _correctionLog.Add($"{nowUtc:O} {reason}");
    }
}
