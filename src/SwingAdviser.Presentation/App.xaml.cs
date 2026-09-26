using System.Windows;
using System.Windows.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SwingAdviser.Domain.Strategy;
using SwingAdviser.Infrastructure.Configuration;
using SwingAdviser.Infrastructure.Persistence;

namespace SwingAdviser.Presentation;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    private ServiceProvider? _serviceProvider;

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

            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(configuration);
            services.AddSingleton(databasePath);
            services.AddSingleton(configuration.GetSection("StrategyParameters").Get<StrategyParameters>()
                ?? throw new InvalidOperationException("appsettings.json に StrategyParameters セクションがありません。"));
            services.AddSingleton(configuration.GetSection("MarketData").Get<MarketDataOptions>()
                ?? throw new InvalidOperationException("appsettings.json に MarketData セクションがありません。"));
            services.AddSingleton(configuration.GetSection("LiquidityFilter").Get<LiquidityFilterOptions>()
                ?? throw new InvalidOperationException("appsettings.json に LiquidityFilter セクションがありません。"));
            services.AddSingleton(configuration.GetSection("CodexCli").Get<CodexCliOptions>()
                ?? throw new InvalidOperationException("appsettings.json に CodexCli セクションがありません。"));
            services.AddDbContext<SwingAdviserDbContext>(o => o
                .UseSqlite($"Data Source={databasePath}")
                .UseSnakeCaseNamingConvention());
            services.AddTransient<MainWindow>();

            _serviceProvider = services.BuildServiceProvider();

            using (var scope = _serviceProvider.CreateScope())
            {
                scope.ServiceProvider.GetRequiredService<SwingAdviserDbContext>().Database.Migrate();
            }

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
        base.OnExit(e);
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
