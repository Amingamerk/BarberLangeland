using BarberLangeland.Services;
using BarberLangeland.Tests.Support;
using Xunit;

namespace BarberLangeland.Tests;

public class BookingAvailabilityServiceTests
{
    private static List<TimeSpan> SlotsFor(TestDb db, DateTime date, int serviceId = TestData.HaircutServiceId)
    {
        using var context = db.CreateContext();
        var service = new BookingAvailabilityService(context);
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
        var service = new BookingAvailabilityService(verify);
        var days = service.GetAvailableDaysAsync(TestData.BarberId, TestData.HaircutServiceId, date, 1, bookingId)
            .GetAwaiter().GetResult();

        Assert.Contains(new TimeSpan(10, 0, 0), days.Single().Slots.Select(slot => slot.Time));
    }

    [Fact(DisplayName = "Today's slots that have already passed are not offered")]
    public void Past_slots_today_are_not_offered()
    {
        using var db = new TestDb();
        var slots = SlotsFor(db, DateTime.Today);

        Assert.All(slots, slot => Assert.True(DateTime.Today.Add(slot) > DateTime.Now));
    }

    [Fact(DisplayName = "Today's slots are computed against Copenhagen time, not the server's clock")]
    public void Today_slots_use_copenhagen_time()
    {
        // The app runs on Azure, whose clock is UTC. Slots between "UTC now" and "Copenhagen now"
        // are already in the past for the customer. Time dependent: it can only detect the defect
        // while the shop is open (or would be open, in UTC) and the two clocks disagree.
        var copenhagen = TimeZoneInfo.FindSystemTimeZoneById("Europe/Copenhagen");
        var copenhagenNow = TimeZoneInfo.ConvertTime(DateTime.UtcNow, copenhagen);
        using var db = new TestDb();

        var slots = SlotsFor(db, copenhagenNow.Date);

        Assert.All(slots, slot => Assert.True(
            copenhagenNow.Date.Add(slot) > copenhagenNow,
            $"Slot {slot:hh\\:mm} is offered but it is already {copenhagenNow:HH:mm} in Copenhagen."));
    }

    [Fact(DisplayName = "Day count below one is treated as one day")]
    public void Day_count_is_clamped_to_one()
    {
        using var db = new TestDb();
        using var context = db.CreateContext();
        var service = new BookingAvailabilityService(context);

        var days = service.GetAvailableDaysAsync(TestData.BarberId, TestData.HaircutServiceId,
            TestData.FutureDate(DayOfWeek.Wednesday), -3).GetAwaiter().GetResult();

        Assert.Single(days);
    }

    [Fact(DisplayName = "The availability window handles the maximum date without throwing")]
    public void Extreme_date_does_not_throw()
    {
        using var db = new TestDb();
        using var context = db.CreateContext();
        var service = new BookingAvailabilityService(context);

        var exception = Record.Exception(() =>
            service.GetAvailableDaysAsync(TestData.BarberId, TestData.HaircutServiceId,
                new DateTime(9999, 12, 31), 5).GetAwaiter().GetResult());

        Assert.Null(exception);
    }
}
