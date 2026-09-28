namespace SwingAdviser.Application.Common;

/// <summary>JST（Asia/Tokyo）関連の変換をまとめる。取引日・場中判定の基準はすべてJST。</summary>
public static class Jst
{
    public static readonly TimeZoneInfo TimeZone = TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time");

    public static DateTime NowJst(TimeProvider timeProvider) =>
        TimeZoneInfo.ConvertTimeFromUtc(timeProvider.GetUtcNow().UtcDateTime, TimeZone);

    public static DateOnly TodayJst(TimeProvider timeProvider) => DateOnly.FromDateTime(NowJst(timeProvider));

    public static DateTime ToJst(DateTime utcDateTime) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc), TimeZone);

    public static DateTime ToUtc(DateTime jstDateTime) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(jstDateTime, DateTimeKind.Unspecified), TimeZone);
}
