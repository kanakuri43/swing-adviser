using SwingAdviser.Domain.Analysis;
using SwingAdviser.Domain.Common;
using SwingAdviser.Domain.MarketData;
using SwingAdviser.Domain.Strategy;
using SwingAdviser.Infrastructure.Configuration;
using SwingAdviser.Infrastructure.MarketData;

namespace SwingAdviser.Backtest;

/// <summary>地合い指数（1306）の日足と、日付から位置を引く索引。取引日カレンダーも兼ねる。</summary>
public sealed class MarketCalendar
{
    private readonly Dictionary<DateOnly, int> _indexByDate;

    public MarketCalendar(List<DailyBar> regimeBars)
    {
        RegimeBars = regimeBars;
        _indexByDate = new Dictionary<DateOnly, int>(regimeBars.Count);
        for (var i = 0; i < regimeBars.Count; i++)
        {
            _indexByDate[regimeBars[i].TradeDate] = i;
        }
    }

    public List<DailyBar> RegimeBars { get; }

    public DateOnly LastDate => RegimeBars[^1].TradeDate;

    public bool TryGetIndex(DateOnly date, out int index) => _indexByDate.TryGetValue(date, out index);
}

/// <summary>
/// 1銘柄について、評価日ごとに本番と同じ <see cref="CandidateScanner"/> を回し、候補が出たら <see cref="TradeSimulator"/> で決済まで追う。
/// 評価日 t には t 以前のバーだけ（本番と同じ本数）を渡す。
/// </summary>
public sealed class StockSimulator
{
    private readonly StrategyParameters _parameters;
    private readonly LiquidityFilterOptions _liquidity;
    private readonly CandidateScanner _scanner;
    private readonly TradeSimulator _tradeSimulator;

    public StockSimulator(StrategyParameters parameters, LiquidityFilterOptions liquidity)
    {
        _parameters = parameters;
        _liquidity = liquidity;
        _scanner = new CandidateScanner(parameters);
        _tradeSimulator = new TradeSimulator(parameters);
    }

    /// <param name="from">シグナル日の下限（含む）。null なら指標が計算できる最初の日から。</param>
    /// <param name="to">シグナル日の上限（含む）。null なら最終日まで。</param>
    public IReadOnlyList<BacktestTrade> Run(
        string stockCode, List<DailyBar> bars, MarketCalendar calendar, DateOnly? from, DateOnly? to)
    {
        var window = _parameters.AnalysisWindow;
        var trades = new List<BacktestTrade>();
        var nextAllowedIndex = new Dictionary<TradeDirection, int>
        {
            [TradeDirection.Long] = 0,
            [TradeDirection.Short] = 0,
        };

        // 最後のバーはシグナルが出ても翌日の始値が無いので対象外
        for (var i = window.MinimumRequiredBars - 1; i < bars.Count - 1; i++)
        {
            var date = bars[i].TradeDate;
            if ((from is not null && date < from) || (to is not null && date > to))
            {
                continue;
            }

            // 両方向とも建玉中なら、新規シグナルは建てられないのでスキャンを省く
            if (nextAllowedIndex.Values.All(next => i < next))
            {
                continue;
            }

            if (!calendar.TryGetIndex(date, out var regimeIndex))
            {
                continue;
            }

            var barsWindow = Slice(bars, i, window.BarsToFetch);
            var regimeWindow = Slice(calendar.RegimeBars, regimeIndex, window.BarsToFetch);
            if (barsWindow.Count < window.MinimumRequiredBars
                || regimeWindow.Count < window.MinimumRequiredBars
                || !LiquidityFilter.IsEligible(barsWindow, _liquidity))
            {
                continue;
            }

            IReadOnlyList<CandidateEvaluation> candidates;
            try
            {
                candidates = _scanner.Evaluate(stockCode, barsWindow, regimeWindow, DateTime.UtcNow);
            }
            catch (DivideByZeroException)
            {
                continue; // ATR=0（値動きが無い銘柄）。本番でも計算できない日として扱う
            }

            foreach (var candidate in candidates)
            {
                if (i < nextAllowedIndex[candidate.Direction])
                {
                    continue;
                }

                var trade = _tradeSimulator.Simulate(
                    stockCode, candidate.Direction, bars, i, candidate.Atr14, calendar.LastDate);
                if (trade is null)
                {
                    continue;
                }

                trades.Add(new BacktestTrade(trade, candidate));
                nextAllowedIndex[candidate.Direction] = trade.ExitIndex;
            }
        }

        return trades;
    }

    /// <summary>endIndex を末尾とする最大 count 本。</summary>
    private static List<DailyBar> Slice(List<DailyBar> bars, int endIndex, int count)
    {
        var start = Math.Max(0, endIndex + 1 - count);
        return bars.GetRange(start, endIndex + 1 - start);
    }
}
