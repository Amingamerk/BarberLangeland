using BarberLangeland.Controllers;
using BarberLangeland.Services;
using BarberLangeland.Tests.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Xunit;

namespace BarberLangeland.Tests;

public class AdminControllerTests
{
    private sealed class NullTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    private static AdminController CreateController(Data.ApplicationDbContext context)
    {
        var http = new DefaultHttpContext();
        return new AdminController(context, new BookingAvailabilityService(context))
        {
            ControllerContext = new ControllerContext { HttpContext = http },
            TempData = new TempDataDictionary(http, new NullTempDataProvider())
        };
    }

    [Theory(DisplayName = "Cancel, no-show and reopen set mutually exclusive status flags")]
    [InlineData("cancel", false, true, false)]
    [InlineData("noshow", false, false, true)]
    [InlineData("reopen", true, false, false)]
    public async Task Status_actions_set_the_expected_flags(string action, bool confirmed, bool cancelled, bool noShow)
    {
        using var db = new TestDb();
        using var context = db.CreateContext();
        var booking = TestData.AddBooking(context, TestData.FutureDate(DayOfWeek.Wednesday).AddHours(10));
        var controller = CreateController(context);

        var result = await controller.UpdateStatus(booking.Id, action, booking.BookingTime.Date, null);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(confirmed, booking.IsConfirmed);
        Assert.Equal(cancelled, booking.IsCancelled);
        Assert.Equal(noShow, booking.IsNoShow);
    }

    [Fact(DisplayName = "An unknown status action is rejected and changes nothing")]
    public async Task Unknown_status_action_is_bad_request()
    {
        using var db = new TestDb();
        using var context = db.CreateContext();
        var booking = TestData.AddBooking(context, TestData.FutureDate(DayOfWeek.Wednesday).AddHours(10));

        var result = await CreateController(context).UpdateStatus(booking.Id, "explode", booking.BookingTime.Date, null);

        Assert.IsType<BadRequestResult>(result);
        Assert.True(booking.IsConfirmed);
    }

    [Fact(DisplayName = "Updating the status of a missing booking returns NotFound")]
    public async Task Missing_booking_status_is_not_found()
    {
        using var db = new TestDb();
        using var context = db.CreateContext();

        Assert.IsType<NotFoundResult>(await CreateController(context).UpdateStatus(12345, "cancel", DateTime.Today, null));
    }

    [Fact(DisplayName = "Moving a booking to a past time is refused with an explanation")]
    public async Task Edit_into_the_past_is_refused()
    {
        using var db = new TestDb();
        using var context = db.CreateContext();
        var booking = TestData.AddBooking(context, TestData.FutureDate(DayOfWeek.Wednesday).AddHours(10));
        var controller = CreateController(context);

        await controller.EditBooking(booking.Id, DateTime.Today.AddDays(-2), TestData.BarberId,
            TestData.HaircutServiceId, new TimeSpan(10, 0, 0), null, null);

        Assert.NotNull(controller.TempData["AdminScheduleError"]);
        Assert.True(booking.BookingTime > DateTime.Now);
    }

    [Fact(DisplayName = "A cancelled booking cannot be moved until it is restored")]
    public async Task Cancelled_booking_cannot_be_edited()
    {
        using var db = new TestDb();
        using var context = db.CreateContext();
        var start = TestData.FutureDate(DayOfWeek.Wednesday).AddHours(10);
        var booking = TestData.AddBooking(context, start, cancelled: true);
        var controller = CreateController(context);

        await controller.EditBooking(booking.Id, start.Date, TestData.BarberId,
            TestData.HaircutServiceId, new TimeSpan(11, 0, 0), null, null);

        Assert.NotNull(controller.TempData["AdminScheduleError"]);
        Assert.Equal(start, booking.BookingTime);
    }

    [Fact(DisplayName = "Moving a booking onto an occupied slot is refused")]
    public async Task Edit_onto_occupied_slot_is_refused()
    {
        using var db = new TestDb();
        using var context = db.CreateContext();
        var date = TestData.FutureDate(DayOfWeek.Wednesday);
        var moving = TestData.AddBooking(context, date.AddHours(10));
        TestData.AddBooking(context, date.AddHours(12));
        var controller = CreateController(context);

        await controller.EditBooking(moving.Id, date, TestData.BarberId,
            TestData.HaircutServiceId, new TimeSpan(12, 0, 0), null, null);

        Assert.NotNull(controller.TempData["AdminScheduleError"]);
        Assert.Equal(date.AddHours(10), moving.BookingTime);
    }

    [Fact(DisplayName = "An admin can move a booking to a free slot")]
    public async Task Edit_to_free_slot_succeeds()
    {
        using var db = new TestDb();
        using var context = db.CreateContext();
        var date = TestData.FutureDate(DayOfWeek.Wednesday);
        var booking = TestData.AddBooking(context, date.AddHours(10));
        var controller = CreateController(context);

        await controller.EditBooking(booking.Id, date, TestData.BarberId,
            TestData.HaircutServiceId, new TimeSpan(14, 0, 0), null, null);

        Assert.Null(controller.TempData["AdminScheduleError"]);
        Assert.Equal(date.AddHours(14), booking.BookingTime);
    }

    [Fact(DisplayName = "An admin can move a booking with the retrying execution strategy that production uses")]
    public async Task Edit_to_free_slot_succeeds_with_retrying_execution_strategy()
    {
        using var db = new TestDb(retryingStrategy: true);
        using var context = db.CreateContext();
        var date = TestData.FutureDate(DayOfWeek.Wednesday);
        var booking = TestData.AddBooking(context, date.AddHours(10));
        var controller = CreateController(context);

        await controller.EditBooking(booking.Id, date, TestData.BarberId,
            TestData.HaircutServiceId, new TimeSpan(14, 0, 0), null, null);

        Assert.Equal(date.AddHours(14), booking.BookingTime);
    }

    [Fact(DisplayName = "Changing the service of a booking updates its stored duration")]
    public async Task Edit_updates_duration_to_the_new_service()
    {
        using var db = new TestDb();
        using var context = db.CreateContext();
        var date = TestData.FutureDate(DayOfWeek.Wednesday);
        var booking = TestData.AddBooking(context, date.AddHours(10));

        await CreateController(context).EditBooking(booking.Id, date, TestData.BarberId,
            TestData.BeardServiceId, new TimeSpan(10, 0, 0), null, null);

        Assert.Equal(TimeSpan.FromMinutes(15), booking.Duration);
    }
}
