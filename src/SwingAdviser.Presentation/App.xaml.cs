using System.Windows;
using System.Windows.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SwingAdviser.Application.Ai;
using SwingAdviser.Application.Analysis;
using SwingAdviser.Application.DailyUpdate;
using SwingAdviser.Application.MarketData;
using SwingAdviser.Application.Positions;
using SwingAdviser.Application.Risk;
using SwingAdviser.Domain.Strategy;
using SwingAdviser.Infrastructure.Analysis;
using SwingAdviser.Infrastructure.Configuration;
using SwingAdviser.Infrastructure.MarketData;
using SwingAdviser.Infrastructure.Persistence;
using SwingAdviser.Presentation.ViewModels;

namespace SwingAdviser.Presentation;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    private ServiceProvider? _serviceProvider;
    private string? _databasePath;
    private BackupOptions? _backupOptions;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        try
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
                .Build();

            var databasePath = DatabasePathResolver.ResolveWritableDatabasePath();
            _databasePath = databasePath;
            _backupOptions = configuration.GetSection("Backup").Get<BackupOptions>();

            var marketDataOptions = configuration.GetSection("MarketData").Get<MarketDataOptions>()
                ?? throw new InvalidOperationException("appsettings.json に MarketData セクションがありません。");

            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(configuration);
            services.AddSingleton(databasePath);
            services.AddSingleton(TimeProvider.System);
            services.AddSingleton(configuration.GetSection("StrategyParameters").Get<StrategyParameters>()
                ?? throw new InvalidOperationException("appsettings.json に StrategyParameters セクションがありません。"));
            services.AddSingleton(marketDataOptions);
            services.AddSingleton(marketDataOptions.YahooFinance);
            services.AddSingleton(marketDataOptions.Jpx);
            services.AddSingleton(configuration.GetSection("LiquidityFilter").Get<LiquidityFilterOptions>()
                ?? throw new InvalidOperationException("appsettings.json に LiquidityFilter セクションがありません。"));
            services.AddSingleton(configuration.GetSection("CodexCli").Get<CodexCliOptions>()
                ?? throw new InvalidOperationException("appsettings.json に CodexCli セクションがありません。"));
            services.AddDbContextFactory<SwingAdviserDbContext>(o => o
                .UseSqlite($"Data Source={databasePath}")
                .UseSnakeCaseNamingConvention());

            services.AddSingleton<IYahooFinanceClient, YahooFinanceClient>();
            services.AddSingleton<IJpxListedIssuesClient, JpxListedIssuesClient>();
            services.AddSingleton<IAiCliExecutor, CodexCliExecutor>();
            services.AddSingleton<StockMasterSynchronizer>();
            services.AddSingleton<DailyBarSynchronizer>();
            services.AddSingleton<DailyUpdateService>();
            services.AddSingleton<ExecutionEntryService>();
            services.AddSingleton<AiEvaluationService>();
            services.AddSingleton<CandidateOverviewReader>();
            services.AddSingleton<HoldingOverviewReader>();
            services.AddSingleton<ExecutionOverviewReader>();

            services.AddTransient<MainWindowViewModel>();
            services.AddTransient<MainWindow>();

            _serviceProvider = services.BuildServiceProvider();

            _serviceProvider.GetRequiredService<IDbContextFactory<SwingAdviserDbContext>>()
                .CreateDbContext()
                .Database.Migrate();

            var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            ShowFatalErrorAndShutdown(ex);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        BackupDatabaseOnExit();
        base.OnExit(e);
    }

    /// <summary>バックアップに失敗しても終了は妨げない。失敗時だけ通知する。</summary>
    private void BackupDatabaseOnExit()
    {
        // 起動に失敗した場合は DB を触っていないのでバックアップしない。
        if (_serviceProvider is null || _databasePath is null || _backupOptions is null)
        {
            return;
        }

        try
        {
            DatabaseBackupService.Backup(_databasePath, _backupOptions, DateOnly.FromDateTime(DateTime.Now));
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"データベースのバックアップに失敗しました:\n{ex.Message}",
                "SwingAdviser",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            $"予期しないエラーが発生しました:\n{e.Exception.Message}",
            "SwingAdviser",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    private void ShowFatalErrorAndShutdown(Exception ex)
    {
        MessageBox.Show(
            $"アプリケーションを起動できません:\n{ex.Message}",
            "SwingAdviser",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        Shutdown(-1);
    }
}
