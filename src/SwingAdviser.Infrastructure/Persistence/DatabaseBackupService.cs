using System.IO.Compression;
using Microsoft.Data.Sqlite;
using SwingAdviser.Infrastructure.Configuration;

namespace SwingAdviser.Infrastructure.Persistence;

/// <summary>
/// DBの一貫したスナップショットを zip 圧縮し、日付付きで保存先へ置く。
/// 同日の再実行は上書き（その日の最終状態のみ残す）。
/// </summary>
public static class DatabaseBackupService
{
    private const string FilePrefix = "swing-adviser_";
    private const string FileExtension = ".zip";

    /// <returns>作成した zip のパス。保存先未設定なら null。</returns>
    public static string? Backup(string databasePath, BackupOptions options, DateOnly today)
    {
        if (string.IsNullOrWhiteSpace(options.DestinationDirectory))
        {
            return null;
        }

        Directory.CreateDirectory(options.DestinationDirectory);

        var snapshotPath = Path.Combine(Path.GetTempPath(), $"swing-adviser-{Guid.NewGuid():N}.db");
        var zipPath = Path.Combine(options.DestinationDirectory, $"{FilePrefix}{today:yyyyMMdd}{FileExtension}");
        var tempZipPath = zipPath + ".tmp";

        try
        {
            // WAL 上の未反映分も含めて一貫したコピーを取るため、ファイルコピーではなく SQLite のバックアップ API を使う。
            using (var source = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly;Pooling=False"))
            using (var destination = new SqliteConnection($"Data Source={snapshotPath};Pooling=False"))
            {
                source.Open();
                destination.Open();
                source.BackupDatabase(destination);
            }

            using (var zip = ZipFile.Open(tempZipPath, ZipArchiveMode.Create))
            {
                zip.CreateEntryFromFile(snapshotPath, Path.GetFileName(databasePath), CompressionLevel.Optimal);
            }

            File.Move(tempZipPath, zipPath, overwrite: true);
        }
        finally
        {
            File.Delete(snapshotPath);
            File.Delete(tempZipPath);
        }

        DeleteOldBackups(options);
        return zipPath;
    }

    private static void DeleteOldBackups(BackupOptions options)
    {
        if (options.KeepCount <= 0)
        {
            return;
        }

        // ファイル名の日付が新しい順に並ぶので、名前の降順で KeepCount 件を超えた分が古いバックアップ。
        var oldFiles = Directory
            .GetFiles(options.DestinationDirectory, $"{FilePrefix}????????{FileExtension}")
            .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
            .Skip(options.KeepCount);

        foreach (var file in oldFiles)
        {
            File.Delete(file);
        }
    }
}
