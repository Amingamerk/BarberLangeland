using BarberLangeland.Models;
using BarberLangeland.Tests.Support;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace BarberLangeland.Tests;

public class BarbersControllerTests
{
    [Fact(DisplayName = "Creating a barber without a photo stores an empty image path")]
    public async Task Create_without_image_stores_empty_string()
    {
        using var db = new TestDb();
        using var context = db.CreateContext();
        var controller = new BarbersController(context);

        var result = await controller.Create(new Barber { Name = "Ny Frisør", Title = "Frisør", ImagePath = null });

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(string.Empty, context.Barbers.Single(b => b.Name == "Ny Frisør").ImagePath);
    }

    [Fact(DisplayName = "Editing with a mismatching id returns NotFound")]
    public async Task Edit_with_mismatched_id_is_not_found()
    {
        using var db = new TestDb();
        using var context = db.CreateContext();
        var controller = new BarbersController(context);

        var result = await controller.Edit(2, new Barber { Id = 1, Name = "X", Title = "Y" });

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact(DisplayName = "A barber with bookings cannot be deleted")]
    public async Task Barber_with_bookings_cannot_be_deleted()
    {
        using var db = new TestDb();
        using var context = db.CreateContext();
        TestData.AddBooking(context, TestData.FutureDate(DayOfWeek.Wednesday).AddHours(10));
        var controller = new BarbersController(context);

        var result = await controller.DeleteConfirmed(TestData.BarberId);

        Assert.IsType<ViewResult>(result);
        Assert.NotNull(context.Barbers.Find(TestData.BarberId));
    }

    [Fact(DisplayName = "A barber without bookings can be deleted")]
    public async Task Barber_without_bookings_can_be_deleted()
    {
        using var db = new TestDb();
        using var context = db.CreateContext();
        var controller = new BarbersController(context);

        var result = await controller.DeleteConfirmed(TestData.BarberId);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Null(context.Barbers.Find(TestData.BarberId));
    }

    [Fact(DisplayName = "Deleting or viewing a missing barber returns NotFound")]
    public async Task Missing_barber_is_not_found()
    {
        using var db = new TestDb();
        using var context = db.CreateContext();
        var controller = new BarbersController(context);

        Assert.IsType<NotFoundResult>(await controller.Delete(999));
        Assert.IsType<NotFoundResult>(await controller.DeleteConfirmed(999));
        Assert.IsType<NotFoundResult>(await controller.Edit((int?)null));
    }
}
