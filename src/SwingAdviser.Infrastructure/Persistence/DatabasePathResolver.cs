namespace SwingAdviser.Infrastructure.Persistence;

/// <summary>
/// 実行中EXEと同じディレクトリの swing-adviser.db に固定する。
/// 書き込み不可なら暗黙フォールバックせず例外を投げる（CLAUDE.md「データベース」節）。
/// </summary>
public static class DatabasePathResolver
{
    private const string DatabaseFileName = "swing-adviser.db";

    public static string ResolveWritableDatabasePath(string? baseDirectory = null)
    {
        var directory = baseDirectory ?? AppContext.BaseDirectory;
        EnsureWritable(directory);
        return Path.Combine(directory, DatabaseFileName);
    }

    private static void EnsureWritable(string directory)
    {
        var probePath = Path.Combine(directory, $".write-check-{Guid.NewGuid():N}.tmp");

        try
        {
            using (File.Create(probePath))
            {
            }

            File.Delete(probePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"データベース保存先ディレクトリへ書き込めません: {directory}", ex);
        }
    }
}
