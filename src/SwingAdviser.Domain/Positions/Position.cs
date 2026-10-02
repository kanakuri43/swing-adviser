using SwingAdviser.Domain.Common;

namespace SwingAdviser.Domain.Positions;

/// <summary>
/// 銘柄・方向・状態・メモを持つ集約ルート。約定明細（新規/決済）を複数紐付ける2層構造
/// （MarginLot×約定割当×企業アクションの3層構造は作らない）。
/// </summary>
public class Position
{
    private static readonly TimeZoneInfo JstTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time");

    private readonly List<Execution> _executions = [];

    private Position()
    {
    } // EF Core

    private Position(string stockCode, TradeDirection direction, bool isMargin, decimal initialAtr, decimal stopLossPrice, string? memo, DateTime nowUtc)
    {
        StockCode = stockCode;
        Direction = direction;
        IsMargin = isMargin;
        InitialAtr = initialAtr;
        StopLossPrice = stopLossPrice;
        Memo = memo;
        Status = PositionStatus.Open;
        CreatedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    public long Id { get; private set; }

    public string StockCode { get; private set; } = string.Empty;

    public TradeDirection Direction { get; private set; }

    public bool IsMargin { get; private set; }

    public PositionStatus Status { get; private set; }

    /// <summary>建玉時に固定したATR14。以後の日次再計算で上書きしない（分割時のみ換算）。</summary>
    public decimal InitialAtr { get; private set; }

    /// <summary>建玉時に固定した損切ライン。以後の日次再計算で上書きしない（分割時のみ換算）。</summary>
    public decimal StopLossPrice { get; private set; }

    public string? Memo { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public IReadOnlyList<Execution> Executions => _executions;

    public decimal RemainingQuantity =>
        _executions.Where(e => e.Side == ExecutionSide.Open).Sum(e => e.AdjustedQuantity)
        - _executions.Where(e => e.Side == ExecutionSide.Close).Sum(e => e.AdjustedQuantity);

    public decimal AverageEntryPrice
    {
        get
        {
            var opens = _executions.Where(e => e.Side == ExecutionSide.Open).ToList();
            var totalQuantity = opens.Sum(e => e.AdjustedQuantity);
            return totalQuantity == 0 ? 0m : opens.Sum(e => e.AdjustedPrice * e.AdjustedQuantity) / totalQuantity;
        }
    }

    /// <summary>決済約定ぶんの実現損益（平均取得単価基準、手数料・信用コスト除く参考値）。</summary>
    public decimal RealizedProfitAndLoss
    {
        get
        {
            return _executions
                .Where(e => e.Side == ExecutionSide.Close)
                .Sum(RealizedProfitAndLossOf);
        }
    }

    /// <summary>決済約定1件ぶんの実現損益（平均取得単価基準）。新規約定は0。</summary>
    public decimal RealizedProfitAndLossOf(Execution execution)
    {
        if (execution.Side != ExecutionSide.Close)
        {
            return 0m;
        }

        var sign = Direction == TradeDirection.Long ? 1m : -1m;
        return sign * (execution.AdjustedPrice - AverageEntryPrice) * execution.AdjustedQuantity;
    }

    /// <summary>最初の新規約定のJST日付。時間ストップ（保有営業日数）の起点に使う。</summary>
    public DateOnly OpenedDate
    {
        get
        {
            var earliestOpenUtc = _executions.Where(e => e.Side == ExecutionSide.Open).Min(e => e.ExecutedAtUtc);
            return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(earliestOpenUtc, JstTimeZone));
        }
    }

    /// <summary>信用取引で返済期限が未入力の新規約定が残っている（決済済みなら常にfalse）。</summary>
    public bool IsMarginDueUnconfirmed =>
        Status == PositionStatus.Open
        && IsMargin
        && _executions.Any(e => e.Side == ExecutionSide.Open && e.MarginDueDate is null);

    public static Position Open(
        string stockCode,
        TradeDirection direction,
        bool isMargin,
        DateTime executedAtUtc,
        decimal price,
        int quantity,
        DateOnly? marginDueDate,
        decimal initialAtr,
        decimal stopLossPrice,
        string? memo,
        DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(stockCode))
        {
            throw new ArgumentException("証券コードは必須です。", nameof(stockCode));
        }

        if (direction == TradeDirection.Short && !isMargin)
        {
            throw new InvalidOperationException("Short は信用取引でのみ建てられます。");
        }

        var position = new Position(stockCode, direction, isMargin, initialAtr, stopLossPrice, memo, nowUtc);
        position._executions.Add(new Execution(ExecutionSide.Open, executedAtUtc, price, quantity, marginDueDate));
        return position;
    }

    public void AddOpenExecution(DateTime executedAtUtc, decimal price, int quantity, DateOnly? marginDueDate, DateTime nowUtc)
    {
        EnsureOpen();
        _executions.Add(new Execution(ExecutionSide.Open, executedAtUtc, price, quantity, marginDueDate));
        UpdatedAtUtc = nowUtc;
    }

    public void AddCloseExecution(DateTime executedAtUtc, decimal price, int quantity, DateTime nowUtc)
    {
        EnsureOpen();

        var closeExecution = new Execution(ExecutionSide.Close, executedAtUtc, price, quantity, null);
        if (closeExecution.AdjustedQuantity > RemainingQuantity)
        {
            throw new InvalidOperationException("残株数を超える決済はできません。");
        }

        _executions.Add(closeExecution);
        UpdatedAtUtc = nowUtc;

        if (RemainingQuantity == 0)
        {
            Status = PositionStatus.Closed;
        }
    }

    /// <summary>ratio=2 は1:2分割、ratio=0.1 は10:1併合。元約定（Price/Quantity）は変更せず、SplitFactorだけ更新する。</summary>
    public void ApplySplit(decimal ratio, DateTime nowUtc)
    {
        EnsureOpen();

        if (ratio <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ratio));
        }

        // 全件が整数株になることを確認してから適用する（途中で例外が出て半端な状態になるのを防ぐ）。
        foreach (var execution in _executions)
        {
            var newAdjustedQuantity = execution.Quantity * execution.SplitFactor * ratio;
            if (newAdjustedQuantity % 1 != 0)
            {
                throw new InvalidOperationException("分割・併合後の株数に端数が生じました。証券会社の明細を確認し手入力で修正してください。");
            }
        }

        foreach (var execution in _executions)
        {
            execution.ApplySplit(ratio);
        }

        StopLossPrice /= ratio;
        InitialAtr /= ratio;
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>最初の新規約定か（損切ライン・ATRの算出根拠になった約定）。</summary>
    public bool IsFirstOpenExecution(Execution execution)
    {
        var first = _executions
            .Where(e => e.Side == ExecutionSide.Open)
            .OrderBy(e => e.ExecutedAtUtc)
            .ThenBy(e => e.Id)
            .FirstOrDefault();
        return ReferenceEquals(first, execution);
    }

    /// <summary>約定の株数を <paramref name="quantity"/> に訂正した場合の残株数。負なら訂正できない。</summary>
    public decimal RemainingQuantityAfterCorrection(Execution execution, int quantity)
    {
        if (!_executions.Contains(execution))
        {
            throw new InvalidOperationException("このポジションに属さない約定です。");
        }

        var newAdjustedQuantity = quantity * execution.SplitFactor;
        var delta = execution.Side == ExecutionSide.Open
            ? newAdjustedQuantity - execution.AdjustedQuantity
            : execution.AdjustedQuantity - newAdjustedQuantity;
        return RemainingQuantity + delta;
    }

    /// <summary>
    /// 入力ミスの訂正。理由は必須で、CorrectionLogに追記するのみ（revisionチェーンは作らない）。
    /// 訂正後の残株数に合わせて状態（保有中/決済済み）を再判定する。
    /// 最初の新規約定の価格・日時を訂正したときは、呼び出し側が再計算した <paramref name="newInitialAtr"/> と
    /// <paramref name="newStopLossPrice"/>（分割調整後の単位）で損切ラインを更新し、変更前後をログに残す。
    /// </summary>
    public void CorrectExecution(
        Execution execution, decimal price, int quantity, DateTime executedAtUtc, string reason, DateTime nowUtc,
        decimal? newInitialAtr = null, decimal? newStopLossPrice = null)
    {
        if (RemainingQuantityAfterCorrection(execution, quantity) < 0)
        {
            throw new InvalidOperationException("訂正後の株数がポジションの残株数を超えます。");
        }

        var stopLossChanged = newInitialAtr.HasValue && newStopLossPrice.HasValue && IsFirstOpenExecution(execution);
        var loggedReason = stopLossChanged
            ? $"{reason}（損切ライン {StopLossPrice:0.##} → {newStopLossPrice!.Value:0.##}）"
            : reason;

        execution.Correct(price, quantity, executedAtUtc, loggedReason, nowUtc);

        if (stopLossChanged)
        {
            InitialAtr = newInitialAtr!.Value;
            StopLossPrice = newStopLossPrice!.Value;
        }

        Status = RemainingQuantity == 0 ? PositionStatus.Closed : PositionStatus.Open;
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>未確認だった信用返済期限を後から入力する。新規約定にのみ設定できる。</summary>
    public void SetMarginDueDate(Execution execution, DateOnly marginDueDate, DateTime nowUtc)
    {
        if (!_executions.Contains(execution))
        {
            throw new InvalidOperationException("このポジションに属さない約定です。");
        }

        if (execution.Side != ExecutionSide.Open)
        {
            throw new InvalidOperationException("信用返済期限は新規約定にのみ設定できます。");
        }

        execution.SetMarginDueDate(marginDueDate);
        UpdatedAtUtc = nowUtc;
    }

    private void EnsureOpen()
    {
        if (Status == PositionStatus.Closed)
        {
            throw new InvalidOperationException("決済済みのポジションは変更できません。");
        }
    }
}
