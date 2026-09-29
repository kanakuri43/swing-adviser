namespace SwingAdviser.Infrastructure.Configuration;

public sealed class BackupOptions
{
    /// <summary>終了時バックアップの保存先。空ならバックアップしない。</summary>
    public string DestinationDirectory { get; init; } = "";

    /// <summary>保存先に残す最新のバックアップ数。これを超えた古いものは削除する。</summary>
    public int KeepCount { get; init; } = 30;
}
