using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using SwingAdviser.Backtest;
using SwingAdviser.Domain.Strategy;
using SwingAdviser.Infrastructure.Configuration;
using SwingAdviser.Infrastructure.MarketData;

// 使い方:
//   fetch [--cache <dir>] [--years 5] [--refresh] [--settings <json>]
//   run   [--cache <dir>] [--out <dir>] [--from yyyy-MM-dd] [--to yyyy-MM-dd] [--split yyyy-MM-dd] [--settings <json>]
// 既定の設定はアプリの appsettings.json（ビルド時にコピー）。--settings で別の設定ファイルに差し替えて比較できる。

var options = ParseArguments(args);
if (options.Command is not ("fetch" or "run"))
{
    Console.Error.WriteLine("使い方: SwingAdviser.Backtest <fetch|run> [--cache dir] [--out dir] [--years n] [--refresh] [--from date] [--to date] [--split date] [--settings json]");
    return 1;
}

var settingsPath = options.Get("settings") ?? Path.Combine(AppContext.BaseDirectory, "appsettings.json");
var configuration = new ConfigurationBuilder().AddJsonFile(Path.GetFullPath(settingsPath), optional: false).Build();
var strategy = configuration.GetSection("StrategyParameters").Get<StrategyParameters>()
    ?? throw new InvalidOperationException("StrategyParameters セクションがありません。");
var marketData = configuration.GetSection("MarketData").Get<MarketDataOptions>()
    ?? throw new InvalidOperationException("MarketData セクションがありません。");
var liquidity = configuration.GetSection("LiquidityFilter").Get<LiquidityFilterOptions>()
    ?? throw new InvalidOperationException("LiquidityFilter セクションがありません。");

var cacheDirectory = Path.GetFullPath(options.Get("cache") ?? "backtest-cache");
var regimeCode = strategy.Gates.MarketRegimeSymbol;

return options.Command == "fetch"
    ? await FetchAsync()
    : Run();

async Task<int> FetchAsync()
{
    Directory.CreateDirectory(cacheDirectory);
    var years = int.Parse(options.Get("years") ?? "5", CultureInfo.InvariantCulture);
    var refresh = options.Has("refresh");
    var since = DateOnly.FromDateTime(DateTime.Today).AddYears(-years);

    using var yahoo = new YahooFinanceClient(marketData.YahooFinance);
    using var jpx = new JpxListedIssuesClient(marketData.Jpx);

    var instruments = await jpx.FetchAsync();
    var codes = instruments.Select(i => i.Code).Where(c => c != regimeCode).Prepend(regimeCode).Distinct().ToList();
    Console.WriteLine($"取得対象 {codes.Count} 銘柄（地合い指数 {regimeCode} を含む）、{since:yyyy-MM-dd} 以降。");

    var done = 0;
    var failed = 0;
    await Parallel.ForEachAsync(codes, new ParallelOptions { MaxDegreeOfParallelism = 4 }, async (code, ct) =>
    {
        var path = BarCache.PathOf(cacheDirectory, code);
        if (!refresh && File.Exists(path))
        {
            Interlocked.Increment(ref done);
            return;
        }

        try
        {
            var (bars, splits) = await FetchWithRetryAsync(yahoo, code, since, ct);
            BarCache.Write(path, BarCleaner.Clean(bars, splits));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (code == regimeCode)
            {
                throw new InvalidOperationException($"地合い指数 {regimeCode} の取得に失敗したため中断します: {ex.Message}", ex);
            }

            Interlocked.Increment(ref failed);
            Console.WriteLine($"  失敗 {code}: {ex.Message}");
        }

        var count = Interlocked.Increment(ref done);
        if (count % 200 == 0)
        {
            Console.WriteLine($"  {count}/{codes.Count}");
        }
    });

    Console.WriteLine($"完了: {codes.Count - failed} 銘柄を取得/キャッシュ済み、失敗 {failed} 件。");
    return 0;
}

static async Task<(IReadOnlyList<FetchedDailyBar> Bars, IReadOnlyList<YahooSplitEvent> Splits)> FetchWithRetryAsync(
    YahooFinanceClient yahoo, string code, DateOnly since, CancellationToken ct)
{
    try
    {
        return await yahoo.FetchAsync(code, since, ct);
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
    {
        await Task.Delay(TimeSpan.FromSeconds(2), ct);
        return await yahoo.FetchAsync(code, since, ct);
    }
}

int Run()
{
    var regimePath = BarCache.PathOf(cacheDirectory, regimeCode);
    if (!File.Exists(regimePath))
    {
        Console.Error.WriteLine($"地合い指数のキャッシュ {regimePath} がありません。先に fetch を実行してください。");
        return 1;
    }

    var calendar = new MarketCalendar(BarCache.Read(regimePath));
    var from = ParseDate(options.Get("from"));
    var to = ParseDate(options.Get("to"));

    var effectiveFrom = calendar.RegimeBars[strategy.AnalysisWindow.MinimumRequiredBars - 1].TradeDate;
    if (from is not null && from > effectiveFrom)
    {
        effectiveFrom = from.Value;
    }

    var effectiveTo = to ?? calendar.LastDate;
    var split = ParseDate(options.Get("split")) ?? effectiveFrom.AddDays((effectiveTo.DayNumber - effectiveFrom.DayNumber) / 2);

    var files = Directory.GetFiles(cacheDirectory, "*.csv")
        .Where(f => Path.GetFileNameWithoutExtension(f) != regimeCode)
        .OrderBy(f => f, StringComparer.Ordinal)
        .ToList();
    Console.WriteLine($"{files.Count} 銘柄、シグナル日 {effectiveFrom:yyyy-MM-dd}〜{effectiveTo:yyyy-MM-dd}、前半/後半の境 {split:yyyy-MM-dd}。設定: {Path.GetFullPath(settingsPath)}");

    var trades = new ConcurrentBag<BacktestTrade>();
    var processed = 0;
    Parallel.ForEach(
        files,
        new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
        () => new StockSimulator(strategy, liquidity),
        (file, _, simulator) =>
        {
            try
            {
                var code = Path.GetFileNameWithoutExtension(file);
                foreach (var trade in simulator.Run(code, BarCache.Read(file), calendar, from, to))
                {
                    trades.Add(trade);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  スキップ {Path.GetFileName(file)}: {ex.Message}");
            }

            var count = Interlocked.Increment(ref processed);
            if (count % 200 == 0)
            {
                Console.WriteLine($"  {count}/{files.Count}");
            }

            return simulator;
        },
        _ => { });

    var ordered = trades
        .OrderBy(t => t.Trade.SignalDate)
        .ThenBy(t => t.Trade.StockCode, StringComparer.Ordinal)
        .ThenBy(t => t.Trade.Direction)
        .ToList();

    var outDirectory = Path.GetFullPath(options.Get("out") ?? "backtest-output");
    Directory.CreateDirectory(outDirectory);
    WriteTrades(Path.Combine(outDirectory, "trades.csv"), ordered);
    File.WriteAllText(Path.Combine(outDirectory, "strategy.json"), JsonSerializer.Serialize(strategy, new JsonSerializerOptions { WriteIndented = true }));

    var summary = Summary.Breakdown(ordered, split);
    WriteSummary(Path.Combine(outDirectory, "summary.csv"), summary);

    Console.WriteLine();
    foreach (var row in summary)
    {
        Console.WriteLine(Summary.FormatRow(row));
    }

    Console.WriteLine();
    Console.WriteLine($"出力: {outDirectory}（trades.csv / summary.csv / strategy.json）。母集団は現在の上場銘柄のため、上場廃止銘柄が無い偏りがある（Longが良く見えやすい）。");
    return 0;
}

void WriteTrades(string path, IReadOnlyList<BacktestTrade> trades)
{
    var sb = new StringBuilder();
    sb.AppendLine("stock_code,direction,signal_date,entry_date,entry_price,stop_loss_price,exit_date,avg_exit_price,result_r,mfe_r,mae_r,holding_days,partial_tp,exit_reason," +
        "score,confidence,is_early,cross_age_days,market_aligned,volume_ratio,overextension_atr,atr14,macd_freshness,macd_position,macd_momentum,trend_strength,volume,market_regime");
    foreach (var bt in trades)
    {
        var t = bt.Trade;
        var c = bt.Candidate;
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"{t.StockCode},{t.Direction},{t.SignalDate:yyyy-MM-dd},{t.EntryDate:yyyy-MM-dd},{t.EntryPrice},{t.StopLossPrice},{t.ExitDate:yyyy-MM-dd},{t.AverageExitPrice:F4},{t.ResultR:F4},{t.MfeR:F4},{t.MaeR:F4},{t.HoldingBusinessDays},{(t.PartialTakeProfitDone ? 1 : 0)},{t.ExitReason}," +
            $"{c.Score},{c.Confidence},{(c.IsEarlySignal ? 1 : 0)},{c.MacdCrossAgeDays},{(c.MarketRegimeAligned ? 1 : 0)},{c.VolumeRatio:F3},{bt.OverextensionAtrMultiple:F3},{c.Atr14:F4},{c.MacdFreshnessScore},{c.MacdPositionScore},{c.MacdMomentumScore},{c.TrendStrengthScore},{c.VolumeScore},{c.MarketRegimeScore}"));
    }

    File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
}

void WriteSummary(string path, IReadOnlyList<GroupStats> rows)
{
    var sb = new StringBuilder();
    sb.AppendLine("group,label,count,win_rate,avg_r,median_r,profit_factor,max_drawdown_r");
    foreach (var s in rows)
    {
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"{s.Group},{s.Label},{s.Count},{s.WinRate:F4},{s.AverageR:F4},{s.MedianR:F4},{(double.IsInfinity(s.ProfitFactor) ? "" : s.ProfitFactor.ToString("F3", CultureInfo.InvariantCulture))},{s.MaxDrawdownR:F2}"));
    }

    File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
}

static DateOnly? ParseDate(string? value) =>
    value is null ? null : DateOnly.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture);

static Arguments ParseArguments(string[] args)
{
    var values = new Dictionary<string, string?>(StringComparer.Ordinal);
    for (var i = 1; i < args.Length; i++)
    {
        if (!args[i].StartsWith("--", StringComparison.Ordinal))
        {
            continue;
        }

        var key = args[i][2..];
        var hasValue = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal);
        values[key] = hasValue ? args[++i] : null;
    }

    return new Arguments(args.Length > 0 ? args[0] : string.Empty, values);
}

internal sealed record Arguments(string Command, Dictionary<string, string?> Values)
{
    public string? Get(string key) => Values.TryGetValue(key, out var value) ? value : null;

    public bool Has(string key) => Values.ContainsKey(key);
}
