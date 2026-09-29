namespace BarberLangeland.Tests.Support;

/// <summary>A clock frozen at a UTC instant that reports Copenhagen local time, like the app's.</summary>
public sealed class TestClock : TimeProvider
{
    private static readonly TimeZoneInfo Copenhagen = TimeZoneInfo.FindSystemTimeZoneById("Europe/Copenhagen");
    private readonly DateTimeOffset _utcNow;

    public TestClock(DateTimeOffset utcNow) => _utcNow = utcNow;

    public static TestClock AtUtc(int year, int month, int day, int hour, int minute)
        => new(new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.Zero));

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public override TimeZoneInfo LocalTimeZone => Copenhagen;
}
