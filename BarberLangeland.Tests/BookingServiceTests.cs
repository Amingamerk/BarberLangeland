using BarberLangeland.Services;
using BarberLangeland.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BarberLangeland.Tests;

public class BookingServiceTests
{
    private static Models.Booking NewBooking(Data.ApplicationDbContext context, DateTime start, int barberId = TestData.BarberId)
    {
        var user = TestData.AddUser(context);
        var service = context.Services.Find(TestData.HaircutServiceId)!;
        return new Models.Booking
        {
            BookingTime = start,
            Duration = TimeSpan.FromMinutes(service.DurationMinutes),
            BarberId = barberId,
            Barber = context.Barbers.Find(TestData.BarberId)!,
            ServiceId = service.Id,
            Service = service,
            UserId = user.Id,
            User = user,
            IsConfirmed = true
        };
    }

    [Fact(DisplayName = "A booking in a free slot is created")]
    public async Task Creates_booking_in_free_slot()
    {
        using var db = new TestDb();
        using var context = db.CreateContext();
        var sut = new BookingService(context);
        var start = TestData.FutureDate(DayOfWeek.Wednesday).AddHours(10);

        var created = await sut.CreateBookingAsync(NewBooking(context, start));

        Assert.NotNull(created);
        Assert.Equal(1, await context.Bookings.CountAsync());
    }

    [Fact(DisplayName = "A booking that overlaps an existing one is rejected")]
    public async Task Rejects_overlapping_booking()
    {
        using var db = new TestDb();
        using var context = db.CreateContext();
        var sut = new BookingService(context);
        var start = TestData.FutureDate(DayOfWeek.Wednesday).AddHours(10);
        await sut.CreateBookingAsync(NewBooking(context, start));

        var second = await sut.CreateBookingAsync(NewBooking(context, start.AddMinutes(15)));

        Assert.Null(second);
        Assert.Equal(1, await context.Bookings.CountAsync());
    }

    [Fact(DisplayName = "Back-to-back bookings are both accepted")]
    public async Task Accepts_adjacent_bookings()
    {
        using var db = new TestDb();
        using var context = db.CreateContext();
        var sut = new BookingService(context);
        var start = TestData.FutureDate(DayOfWeek.Wednesday).AddHours(10);
        await sut.CreateBookingAsync(NewBooking(context, start));

        Assert.NotNull(await sut.CreateBookingAsync(NewBooking(context, start.AddMinutes(30))));
        Assert.NotNull(await sut.CreateBookingAsync(NewBooking(context, start.AddMinutes(-30))));
    }

    [Fact(DisplayName = "A booking for an unknown barber is rejected")]
    public async Task Rejects_unknown_barber()
    {
        using var db = new TestDb();
        using var context = db.CreateContext();
        var sut = new BookingService(context);
        var booking = NewBooking(context, TestData.FutureDate(DayOfWeek.Wednesday).AddHours(10), barberId: 999);

        Assert.Null(await sut.CreateBookingAsync(booking));
    }

    [Fact(DisplayName = "Bookings can be created with the retrying execution strategy that production uses")]
    public async Task Creates_booking_with_retrying_execution_strategy()
    {
        using var db = new TestDb(retryingStrategy: true);
        using var context = db.CreateContext();
        var sut = new BookingService(context);
        var start = TestData.FutureDate(DayOfWeek.Wednesday).AddHours(10);

        var created = await sut.CreateBookingAsync(NewBooking(context, start));

        Assert.NotNull(created);
    }

    [Fact(DisplayName = "A cancelled booking does not block a new booking in the same slot")]
    public async Task Cancelled_booking_does_not_block_slot()
    {
        using var db = new TestDb();
        using var context = db.CreateContext();
        var start = TestData.FutureDate(DayOfWeek.Wednesday).AddHours(10);
        TestData.AddBooking(context, start, cancelled: true);
        var sut = new BookingService(context);

        var created = await sut.CreateBookingAsync(NewBooking(context, start));

        Assert.NotNull(created);
    }
}
