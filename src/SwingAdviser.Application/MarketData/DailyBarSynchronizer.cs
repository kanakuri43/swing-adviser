using Microsoft.EntityFrameworkCore;
using SwingAdviser.Application.Common;
using SwingAdviser.Domain.MarketData;
using SwingAdviser.Domain.Positions;
using SwingAdviser.Infrastructure.Configuration;
using SwingAdviser.Infrastructure.MarketData;
using SwingAdviser.Infrastructure.Persistence;

namespace SwingAdviser.Application.MarketData;

public sealed record DailyBarSyncResult(string StockCode, int UpsertedBarCount, IReadOnlyList<string> Warnings);

/// <summary>
/// 1銘柄の日足をYahoo Financeから同期する（1回の呼び出し＝Yahooへの1リクエスト）。
/// 分割・併合はここで検出し、保存済みバーと保有中ポジションへ乗除算のみで換算する
/// （単位ハッシュ検証はしない。CLAUDE.md「意図的に作らないもの」）。
/// </summary>
public sealed class DailyBarSynchronizer(
    IYahooFinanceClient yahooClient,
    IDbContextFactory<SwingAdviserDbContext> contextFactory,
    YahooFinanceOptions options,
    TimeProvider timeProvider)
{
    public async Task<DailyBarSyncResult> SyncAsync(string stockCode, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var lastStoredDate = await context.DailyBars
            .Where(b => b.StockCode == stockCode)
            .OrderByDescending(b => b.TradeDate)
            .Select(b => (DateOnly?)b.TradeDate)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        var todayJst = Jst.TodayJst(timeProvider);
        var since = lastStoredDate ?? todayJst.AddDays(-options.InitialFetchCalendarDays);

        var (fetchedBars, splits) = await yahooClient.FetchAsync(stockCode, since, cancellationToken).ConfigureAwait(false);

        var finalizedTime = TimeOnly.Parse(options.DailyBarFinalizedTimeJst);
        var nowJst = Jst.NowJst(timeProvider);
        var todayNotYetFinalized = TimeOnly.FromDateTime(nowJst) < finalizedTime;

        var eligibleBars = fetchedBars.Where(bar => bar.TradeDate != todayJst || !todayNotYetFinalized).ToList();

        var warnings = new List<string>();
        var appliedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        foreach (var split in splits.OrderBy(s => s.EffectiveDate))
        {
            await ApplySplitAsync(context, stockCode, split, appliedAtUtc, warnings, cancellationToken).ConfigureAwait(false);
        }

        if (eligibleBars.Count > 0)
        {
            var minDate = eligibleBars.Min(b => b.TradeDate);
            var maxDate = eligibleBars.Max(b => b.TradeDate);
            var existingBars = await context.DailyBars
                .Where(b => b.StockCode == stockCode && b.TradeDate >= minDate && b.TradeDate <= maxDate)
                .ToDictionaryAsync(b => b.TradeDate, cancellationToken).ConfigureAwait(false);

            foreach (var bar in eligibleBars)
            {
                if (existingBars.TryGetValue(bar.TradeDate, out var stored))
                {
                    stored.Update(bar.Open, bar.High, bar.Low, bar.Close, bar.Volume);
                }
                else
                {
                    context.DailyBars.Add(new DailyBar(stockCode, bar.TradeDate, bar.Open, bar.High, bar.Low, bar.Close, bar.Volume));
                }
            }
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new DailyBarSyncResult(stockCode, eligibleBars.Count, warnings);
    }

    private static async Task ApplySplitAsync(
        SwingAdviserDbContext context, string stockCode, YahooSplitEvent split, DateTime appliedAtUtc, List<string> warnings, CancellationToken cancellationToken)
    {
        var affectedBars = await context.DailyBars
            .Where(b => b.StockCode == stockCode && b.TradeDate < split.EffectiveDate)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var bar in affectedBars)
        {
            bar.ApplySplit(split.Ratio);
        }

        var openPositions = await context.Positions
            .Include(p => p.Executions)
            .Where(p => p.StockCode == stockCode && p.Status == PositionStatus.Open)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        foreach (var position in openPositions)
        {
            var allExecutionsBeforeEffectiveDate = position.Executions.All(
                execution => ExecutedJstDate(execution) < split.EffectiveDate);

            if (allExecutionsBeforeEffectiveDate)
            {
                position.ApplySplit(split.Ratio, appliedAtUtc);
            }
            else
            {
                warnings.Add(
                    $"{stockCode}: 分割・併合の効力発生日({split.EffectiveDate:yyyy-MM-dd})以後の約定を含むポジション(Id={position.Id})は自動換算されませんでした。証券会社の明細を確認し手入力で修正してください。");
            }
        }
    }

    private static DateOnly ExecutedJstDate(Execution execution) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(execution.ExecutedAtUtc, Jst.TimeZone));
}
