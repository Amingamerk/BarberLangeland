namespace BarberLangeland.Services
{
    /// <summary>
    /// The shop's clock. The app runs on Azure, whose local time is UTC, so <c>DateTime.Now</c>
    /// is one or two hours behind Denmark: slots that had already passed, or that came after
    /// closing time, were still offered. Everything that decides what "now" and "today" mean
    /// asks this provider instead, and tests substitute a fixed instant.
    /// </summary>
    public sealed class CopenhagenTimeProvider : TimeProvider
    {
        private static readonly TimeZoneInfo Copenhagen = FindZone();

        public override TimeZoneInfo LocalTimeZone => Copenhagen;

        private static TimeZoneInfo FindZone()
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById("Europe/Copenhagen");
            }
            catch (TimeZoneNotFoundException)
            {
                // Windows hosts without ICU-backed IANA ids.
                return TimeZoneInfo.FindSystemTimeZoneById("Romance Standard Time");
            }
        }
    }

    public static class TimeProviderExtensions
    {
        /// <summary>The current shop-local date and time.</summary>
        public static DateTime LocalNow(this TimeProvider clock) => clock.GetLocalNow().DateTime;

        /// <summary>The current shop-local date at midnight.</summary>
        public static DateTime LocalToday(this TimeProvider clock) => clock.GetLocalNow().Date;
    }
}
