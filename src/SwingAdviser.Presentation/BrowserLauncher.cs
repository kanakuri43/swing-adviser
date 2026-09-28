using System.Diagnostics;
using System.Windows;

namespace SwingAdviser.Presentation;

/// <summary>既定ブラウザでURLを開く。http/https以外は開かない
/// （UseShellExecuteでファイル・コマンドが誤って実行されるのを防ぐ）。</summary>
public static class BrowserLauncher
{
    public static Uri YahooFinanceChartUri(string stockCode) =>
        new($"https://finance.yahoo.co.jp/quote/{Uri.EscapeDataString(stockCode)}.T/chart");

    public static void Open(Uri uri)
    {
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            MessageBox.Show($"ブラウザを起動できませんでした: {exception.Message}", "SwingAdviser", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
