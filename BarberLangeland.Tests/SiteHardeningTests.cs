using System.Net;
using BarberLangeland.Data;
using BarberLangeland.Models;
using BarberLangeland.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BarberLangeland.Tests;

public class SiteHardeningTests : IClassFixture<AppFactory>
{
    private readonly AppFactory _factory;

    public SiteHardeningTests(AppFactory factory) => _factory = factory;

    [Fact(DisplayName = "The privacy policy page exists, names the business and is linked from the footer")]
    public async Task Privacy_page_is_available_and_linked()
    {
        var client = _factory.CreateBrowser();

        var page = await client.GetAsync("/Home/Privacy");
        var body = await page.BodyAsync();

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("Privatlivspolitik", body);
        Assert.Contains("Datatilsynet", body);
        Assert.Contains("/Home/Privacy", await client.GetStringAsync("/"));
    }

    [Fact(DisplayName = "The not-found page is Danish and links back to the front page")]
    public async Task Not_found_page_is_danish()
    {
        var response = await _factory.CreateBrowser().GetAsync("/findes-ikke");
        var body = await response.BodyAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("Siden findes ikke", body);
        Assert.Contains("Til forsiden", body);
    }

    [Fact(DisplayName = "Availability more than two years ahead is a bad request")]
    public async Task Availability_far_future_is_rejected()
    {
        var date = DateTime.Today.AddYears(3).ToString("yyyy-MM-dd");
        var response = await _factory.CreateBrowser().GetAsync($"/Booking/Availability?barberId=1&serviceId=1&bookingDate={date}&days=1");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact(DisplayName = "A cancelled booking cannot be restored once someone else has taken its slot")]
    public async Task Reopen_is_refused_when_slot_was_taken()
    {
        await _factory.EnsureAdminAsync();
        var admin = _factory.CreateBrowser();
        var login = await admin.GetTokenAsync("/Account/Login");
        await admin.PostFormAsync("/Account/Login", login,
            ("Email", AppFactory.AdminEmail), ("Password", AppFactory.AdminPassword));

        var date = TestData.FutureDate(DayOfWeek.Wednesday).AddDays(21);
        int cancelledId, takenId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = new ApplicationUser { UserName = "user_reopen", Email = "reopen@example.com", SecurityStamp = Guid.NewGuid().ToString() };
            db.Users.Add(user);

            Booking Make(bool cancelled) => new()
            {
                BookingTime = date.AddHours(10), Duration = TimeSpan.FromMinutes(30), Price = 100,
                BarberId = 1, Barber = db.Barbers.Find(1)!, ServiceId = 1, Service = db.Services.Find(1)!,
                UserId = user.Id, User = user, IsConfirmed = !cancelled, IsCancelled = cancelled
            };

            var cancelled = Make(true);
            var taken = Make(false);
            db.Bookings.AddRange(cancelled, taken);
            await db.SaveChangesAsync();
            cancelledId = cancelled.Id;
            takenId = taken.Id;
        }

        var token = await admin.GetTokenAsync("/Admin/Schedule");
        await admin.PostFormAsync("/Admin/UpdateStatus", token,
            ("id", cancelledId.ToString()), ("action", "reopen"), ("date", date.ToString("yyyy-MM-dd")));

        using var check = _factory.Services.CreateScope();
        var context = check.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.True((await context.Bookings.SingleAsync(b => b.Id == cancelledId)).IsCancelled);
        Assert.False((await context.Bookings.SingleAsync(b => b.Id == takenId)).IsCancelled);
    }
}
