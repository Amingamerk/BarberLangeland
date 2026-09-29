using BarberLangeland.Services;
using BarberLangeland.Tests.Support;
using Xunit;

namespace BarberLangeland.Tests;

public class BookingAvailabilityServiceTests
{
    private static List<TimeSpan> SlotsFor(TestDb db, DateTime date, int serviceId = TestData.HaircutServiceId, TimeProvider? clock = null)
    {
        using var context = db.CreateContext();
        var service = new BookingAvailabilityService(context, clock ?? new CopenhagenTimeProvider());
        var days = service.GetAvailableDaysAsync(TestData.BarberId, serviceId, date, 1).GetAwaiter().GetResult();
        return days.Single().Slots.Select(slot => slot.Time).ToList();
    }

    [Fact(DisplayName = "Sunday is closed and offers no slots")]
    public void Sunday_has_no_slots()
    {
        using var db = new TestDb();
        Assert.Empty(SlotsFor(db, TestData.FutureDate(DayOfWeek.Sunday)));
    }

    [Fact(DisplayName = "Weekday slots run 09:30 to 16:30 every 30 minutes for a 30-minute haircut")]
    public void Weekday_slot_range()
    {
        using var db = new TestDb();
        var slots = SlotsFor(db, TestData.FutureDate(DayOfWeek.Wednesday));

        Assert.Equal(new TimeSpan(9, 30, 0), slots.First());
        Assert.Equal(new TimeSpan(16, 30, 0), slots.Last());
        Assert.Equal(15, slots.Count);
    }

    [Fact(DisplayName = "Saturday closes at 13:00, so the last haircut slot is 12:30")]
    public void Saturday_closes_early()
    {
        using var db = new TestDb();
        var slots = SlotsFor(db, TestData.FutureDate(DayOfWeek.Saturday));

        Assert.Equal(new TimeSpan(9, 30, 0), slots.First());
        Assert.Equal(new TimeSpan(12, 30, 0), slots.Last());
    }

    [Fact(DisplayName = "An unknown service offers no slots")]
    public void Unknown_service_has_no_slots()
    {
        using var db = new TestDb();
        Assert.Empty(SlotsFor(db, TestData.FutureDate(DayOfWeek.Wednesday), serviceId: 999));
    }

    [Fact(DisplayName = "A booked haircut hides the slot it occupies but not its neighbours")]
    public void Existing_booking_hides_only_overlapping_slots()
    {
        using var db = new TestDb();
        var date = TestData.FutureDate(DayOfWeek.Wednesday);
        using (var context = db.CreateContext())
        {
            TestData.AddBooking(context, date.AddHours(10)); // 10:00-10:30
        }

        var slots = SlotsFor(db, date);

        Assert.DoesNotContain(new TimeSpan(10, 0, 0), slots);
        Assert.Contains(new TimeSpan(9, 30, 0), slots);   // ends exactly when the booking starts
        Assert.Contains(new TimeSpan(10, 30, 0), slots);  // starts exactly when the booking ends
    }

    [Fact(DisplayName = "A longer existing booking hides every slot it overlaps")]
    public void Long_booking_hides_multiple_slots()
    {
        using var db = new TestDb();
        var date = TestData.FutureDate(DayOfWeek.Thursday);
        using (var context = db.CreateContext())
        {
            // 30 minute haircut at 11:00 -> 11:00-11:30. A 30 minute request at 10:45 would overlap,
            // but slots are on a 30 minute grid, so 10:30 (10:30-11:00) must stay free.
            TestData.AddBooking(context, date.AddHours(11));
        }

        var slots = SlotsFor(db, date);

        Assert.DoesNotContain(new TimeSpan(11, 0, 0), slots);
        Assert.Contains(new TimeSpan(10, 30, 0), slots);
    }

    [Fact(DisplayName = "A cancelled booking frees its slot again")]
    public void Cancelled_booking_frees_slot()
    {
        using var db = new TestDb();
        var date = TestData.FutureDate(DayOfWeek.Wednesday);
        using (var context = db.CreateContext())
        {
            TestData.AddBooking(context, date.AddHours(10), cancelled: true);
        }

        var slots = SlotsFor(db, date);

        Assert.Contains(new TimeSpan(10, 0, 0), slots);
    }

    [Fact(DisplayName = "Excluding a booking id frees the slot that booking occupies")]
    public void Excluded_booking_does_not_block_itself()
    {
        using var db = new TestDb();
        var date = TestData.FutureDate(DayOfWeek.Wednesday);
        int bookingId;
        using (var context = db.CreateContext())
        {
            bookingId = TestData.AddBooking(context, date.AddHours(10)).Id;
        }

        using var verify = db.CreateContext();
        var service = new BookingAvailabilityService(verify, new CopenhagenTimeProvider());
        var days = service.GetAvailableDaysAsync(TestData.BarberId, TestData.HaircutServiceId, date, 1, bookingId)
            .GetAwaiter().GetResult();

        Assert.Contains(new TimeSpan(10, 0, 0), days.Single().Slots.Select(slot => slot.Time));
    }

    // 2026-10-05 is a Monday and Copenhagen is on summer time (UTC+2) until 25 October.
    [Fact(DisplayName = "Today's slots that have already passed are not offered")]
    public void Past_slots_today_are_not_offered()
    {
        using var db = new TestDb();
        var clock = TestClock.AtUtc(2026, 10, 5, 8, 15); // 10:15 in Copenhagen

        var slots = SlotsFor(db, new DateTime(2026, 10, 5), clock: clock);

        Assert.Equal(new TimeSpan(10, 30, 0), slots.First());
        Assert.Equal(new TimeSpan(16, 30, 0), slots.Last());
    }

    [Fact(DisplayName = "Just after opening in Copenhagen, the server's UTC clock does not hide or add slots")]
    public void Slots_follow_copenhagen_time_in_the_morning()
    {
        using var db = new TestDb();
        var clock = TestClock.AtUtc(2026, 10, 5, 7, 45); // 09:45 in Copenhagen, 07:45 UTC

        var slots = SlotsFor(db, new DateTime(2026, 10, 5), clock: clock);

        // Against the UTC clock 09:30 would still look bookable, although it passed 15 minutes ago.
        Assert.Equal(new TimeSpan(10, 0, 0), slots.First());
    }

    [Fact(DisplayName = "After closing time in Copenhagen, no slots are offered for today")]
    public void No_slots_after_closing_in_copenhagen()
    {
        using var db = new TestDb();
        var clock = TestClock.AtUtc(2026, 10, 5, 16, 26); // 18:26 in Copenhagen, shop closed at 17:00

        Assert.Empty(SlotsFor(db, new DateTime(2026, 10, 5), clock: clock));
    }

    [Fact(DisplayName = "Winter time (UTC+1) is applied too")]
    public void Slots_follow_copenhagen_winter_time()
    {
        using var db = new TestDb();
        var clock = TestClock.AtUtc(2026, 12, 7, 9, 50); // Monday 10:50 in Copenhagen (UTC+1)

        var slots = SlotsFor(db, new DateTime(2026, 12, 7), clock: clock);

        Assert.Equal(new TimeSpan(11, 0, 0), slots.First());
    }

    [Fact(DisplayName = "Tomorrow's slots are all offered regardless of the time of day today")]
    public void Future_days_are_unaffected_by_the_clock()
    {
        using var db = new TestDb();
        var clock = TestClock.AtUtc(2026, 10, 5, 15, 0);

        var slots = SlotsFor(db, new DateTime(2026, 10, 6), clock: clock);

        Assert.Equal(new TimeSpan(9, 30, 0), slots.First());
    }

    [Fact(DisplayName = "Day count below one is treated as one day")]
    public void Day_count_is_clamped_to_one()
    {
        using var db = new TestDb();
        using var context = db.CreateContext();
        var service = new BookingAvailabilityService(context, new CopenhagenTimeProvider());

        var days = service.GetAvailableDaysAsync(TestData.BarberId, TestData.HaircutServiceId,
            TestData.FutureDate(DayOfWeek.Wednesday), -3).GetAwaiter().GetResult();

        Assert.Single(days);
    }

    [Fact(DisplayName = "The availability window handles the maximum date without throwing")]
    public void Extreme_date_does_not_throw()
    {
        using var db = new TestDb();
        using var context = db.CreateContext();
        var service = new BookingAvailabilityService(context, new CopenhagenTimeProvider());

        var exception = Record.Exception(() =>
            service.GetAvailableDaysAsync(TestData.BarberId, TestData.HaircutServiceId,
                new DateTime(9999, 12, 31), 5).GetAwaiter().GetResult());

        Assert.Null(exception);
    }
}
