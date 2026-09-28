using Microsoft.EntityFrameworkCore;
using SwingAdviser.Domain.Stocks;
using SwingAdviser.Infrastructure.Configuration;
using SwingAdviser.Infrastructure.MarketData;
using SwingAdviser.Infrastructure.Persistence;

namespace SwingAdviser.Application.MarketData;

public sealed record StockMasterSyncResult(bool Refreshed, int ActiveCount, string? Warning);

/// <summary>
/// JPX上場銘柄一覧を銘柄マスタへ反映する。地合い判定用のTOPIX連動ETF(1306)はJPX一覧からは
/// 除外されるため（ETFは対象外）、ここで別途必ず存在させる。
/// </summary>
public sealed class StockMasterSynchronizer(
    IJpxListedIssuesClient jpxClient,
    IDbContextFactory<SwingAdviserDbContext> contextFactory,
    JpxOptions options,
    TimeProvider timeProvider)
{
    public const string MarketRegimeStockCode = "1306";
    private const string MarketRegimeStockName = "NEXT FUNDS TOPIX連動型上場投信";

    public async Task<StockMasterSyncResult> SyncAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;

        await EnsureMarketRegimeStockAsync(context, nowUtc, cancellationToken).ConfigureAwait(false);

        var activeCount = await context.Stocks
            .CountAsync(s => s.IsActive && s.StockCode != MarketRegimeStockCode, cancellationToken).ConfigureAwait(false);
        var lastRefreshedAtUtc = await context.Stocks
            .Where(s => s.StockCode != MarketRegimeStockCode)
            .MaxAsync(s => (DateTime?)s.UpdatedAtUtc, cancellationToken).ConfigureAwait(false);
        var needsRefresh = activeCount == 0
            || lastRefreshedAtUtc is null
            || (nowUtc - lastRefreshedAtUtc.Value).TotalDays >= options.RefreshIntervalDays;

        if (!needsRefresh)
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return new StockMasterSyncResult(false, activeCount, null);
        }

        IReadOnlyList<JpxListedInstrument> instruments;
        try
        {
            instruments = await jpxClient.FetchAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or TaskCanceledException)
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            if (activeCount == 0)
            {
                throw new InvalidOperationException("銘柄マスタが空で、JPX銘柄一覧の取得にも失敗しました。", exception);
            }

            return new StockMasterSyncResult(
                false, activeCount, $"JPX銘柄一覧の取得に失敗したため、既存の銘柄マスタを継続使用します: {exception.Message}");
        }

        var existingByCode = await context.Stocks.ToDictionaryAsync(s => s.StockCode, cancellationToken).ConfigureAwait(false);
        var fetchedCodes = new HashSet<string>(instruments.Select(i => i.Code), StringComparer.Ordinal);

        foreach (var instrument in instruments)
        {
            if (existingByCode.TryGetValue(instrument.Code, out var stock))
            {
                stock.Update(instrument.Name, instrument.Segment, isActive: true, nowUtc);
            }
            else
            {
                context.Stocks.Add(new Stock(instrument.Code, instrument.Name, instrument.Segment, isActive: true, nowUtc));
            }
        }

        foreach (var stock in existingByCode.Values)
        {
            if (stock.StockCode == MarketRegimeStockCode || fetchedCodes.Contains(stock.StockCode) || !stock.IsActive)
            {
                continue;
            }

            stock.Update(stock.Name, stock.MarketSegment, isActive: false, nowUtc);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        var newActiveCount = await context.Stocks
            .CountAsync(s => s.IsActive && s.StockCode != MarketRegimeStockCode, cancellationToken).ConfigureAwait(false);
        return new StockMasterSyncResult(true, newActiveCount, null);
    }

    private static async Task EnsureMarketRegimeStockAsync(SwingAdviserDbContext context, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var existing = await context.Stocks
            .FirstOrDefaultAsync(s => s.StockCode == MarketRegimeStockCode, cancellationToken).ConfigureAwait(false);

        if (existing is null)
        {
            context.Stocks.Add(new Stock(MarketRegimeStockCode, MarketRegimeStockName, MarketSegment.Etf, isActive: true, nowUtc));
        }
        else if (!existing.IsActive)
        {
            existing.Update(existing.Name, MarketSegment.Etf, isActive: true, nowUtc);
        }
    }
}
