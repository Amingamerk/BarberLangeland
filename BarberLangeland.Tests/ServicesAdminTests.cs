using System.Net;
using System.Text.RegularExpressions;
using BarberLangeland.Data;
using BarberLangeland.Models;
using BarberLangeland.Tests.Support;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BarberLangeland.Tests;

public class ServicesAdminTests : IClassFixture<AppFactory>
{
    private const string StrongPassword = "Test-Booking-2026!";
    private readonly AppFactory _factory;

    public ServicesAdminTests(AppFactory factory) => _factory = factory;

    private static async Task<HttpClient> AdminAsync(AppFactory factory)
    {
        await factory.EnsureAdminAsync();
        var client = factory.CreateBrowser();
        var token = await client.GetTokenAsync("/Account/Login");
        var response = await client.PostFormAsync("/Account/Login", token,
            ("Email", AppFactory.AdminEmail), ("Password", AppFactory.AdminPassword));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return client;
    }

    private Task<HttpClient> AdminAsync() => AdminAsync(_factory);

    private static async Task<HttpResponseMessage> CreateServiceAsync(HttpClient admin, string name, string duration = "45",
        string price = "300", string description = "Test")
    {
        var token = await admin.GetTokenAsync("/Services/Create");
        return await admin.PostFormAsync("/Services/Create", token,
            ("Name", name), ("Description", description), ("DurationMinutes", duration), ("Price", price));
    }

    private async Task<T> DbAsync<T>(Func<ApplicationDbContext, Task<T>> action)
    {
        using var scope = _factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }

    private async Task<T> DbFindAsync<T>(Func<ApplicationDbContext, ValueTask<T>> action)
    {
        using var scope = _factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }

    private async Task<int> IdOfAsync(string name) => (await DbAsync(db => db.Services.SingleAsync(s => s.Name == name))).Id;

    private static async Task<string> PageAsync(HttpClient client, string url) => await (await client.GetAsync(url)).BodyAsync();

    // ---------- access ----------

    [Theory(DisplayName = "The treatments pages redirect anonymous visitors to the login page")]
    [InlineData("/Services")]
    [InlineData("/Services/Create")]
    [InlineData("/Services/Edit/1")]
    public async Task Anonymous_is_sent_to_login(string url)
    {
        var response = await _factory.CreateBrowser().GetAsync(url);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Account/Login", response.Headers.Location!.AbsolutePath);
    }

    [Fact(DisplayName = "A signed-in customer cannot use the treatments pages")]
    public async Task Customer_is_refused()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            await users.CreateAsync(new ApplicationUser { UserName = "user_svc", Email = "svc-kunde@example.com", EmailConfirmed = true }, StrongPassword);
        }
        var client = _factory.CreateBrowser();
        var token = await client.GetTokenAsync("/Account/Login");
        await client.PostFormAsync("/Account/Login", token, ("Email", "svc-kunde@example.com"), ("Password", StrongPassword));

        var response = await client.GetAsync("/Services");
        var post = await client.PostFormAsync("/Services/ToggleActive", await client.GetTokenAsync("/Account/MyBookings"), ("id", "1"));

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, post.StatusCode);
        Assert.True((await DbFindAsync(db => db.Services.FindAsync(1)))!.IsActive);
    }

    [Fact(DisplayName = "The administrator sees the treatments in the menu and in the list, in order")]
    public async Task Admin_sees_list_and_menu()
    {
        var admin = await AdminAsync();

        var list = await PageAsync(admin, "/Services");

        Assert.Contains("Behandlinger", list);
        Assert.Matches("href=\"/Services\"[^>]*>Behandlinger", list);
        var order = new[] { "Herreklip", "Børneklip", "Skægtrimning" }.Select(n => list.IndexOf(n, StringComparison.Ordinal)).ToArray();
        Assert.All(order, i => Assert.True(i > 0));
        Assert.True(order[0] < order[1] && order[1] < order[2]);
    }

    // ---------- create and validation ----------

    [Fact(DisplayName = "A new treatment appears last on the prices page, the front page and the booking page")]
    public async Task Created_service_is_shown_everywhere()
    {
        var admin = await AdminAsync();

        var response = await CreateServiceAsync(admin, "Hårfarve Test", "60", "450.50", "Farve og pleje");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var anonymous = _factory.CreateBrowser();
        foreach (var url in new[] { "/Home/Prices", "/", "/Booking" })
        {
            var html = await PageAsync(anonymous, url);
            Assert.Contains("Hårfarve Test", html);
        }
        var prices = await PageAsync(anonymous, "/Home/Prices");
        Assert.Contains("450.5 kr.", prices);
        Assert.Contains("60 minutter", prices);
        Assert.True(prices.IndexOf("Hårfarve Test", StringComparison.Ordinal) > prices.IndexOf("Skægtrimning", StringComparison.Ordinal));
    }

    [Theory(DisplayName = "Invalid treatment input is refused with a Danish message and nothing is saved")]
    [InlineData("", "45", "100", "Indtast et navn.")]
    [InlineData("Ugyldig A", "7", "100", "helt antal 5 minutter")]
    [InlineData("Ugyldig B", "3", "100", "mellem 5 og 240 minutter")]
    [InlineData("Ugyldig C", "300", "100", "mellem 5 og 240 minutter")]
    [InlineData("Ugyldig D", "45", "-5", "mellem 0 og 10.000 kr.")]
    [InlineData("Ugyldig E", "45", "100000", "mellem 0 og 10.000 kr.")]
    [InlineData("Ugyldig F", "45", "10.999", "højst have to decimaler")]
    public async Task Invalid_input_is_refused(string name, string duration, string price, string message)
    {
        var admin = await AdminAsync();

        var response = await CreateServiceAsync(admin, name, duration, price);
        var body = await response.BodyAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(message, body);
        Assert.False(await DbAsync(db => db.Services.AnyAsync(s => s.Name.StartsWith("Ugyldig"))));
    }

    [Fact(DisplayName = "Two treatments cannot share a name, ignoring letter case")]
    public async Task Duplicate_name_is_refused()
    {
        var admin = await AdminAsync();
        await CreateServiceAsync(admin, "Dublet Test");

        var response = await CreateServiceAsync(admin, "dublet TEST");

        Assert.Contains("Der findes allerede en behandling med det navn.", await response.BodyAsync());
        Assert.Equal(1, await DbAsync(db => db.Services.CountAsync(s => s.Name.ToLower() == "dublet test")));
    }

    [Fact(DisplayName = "The form previews how many times a duration leaves per day")]
    public async Task Form_has_duration_preview_data()
    {
        var admin = await AdminAsync();

        var html = await PageAsync(admin, "/Services/Create");

        Assert.Contains("data-duration-input", html);
        Assert.Contains("data-weekday-minutes=\"450\"", html);
        Assert.Contains("data-saturday-minutes=\"210\"", html);
        Assert.Contains("data-slot-preview", html);
    }

    // ---------- edit ----------

    [Fact(DisplayName = "Editing changes the public pages and the times the calendar offers")]
    public async Task Edit_changes_public_pages_and_slots()
    {
        var admin = await AdminAsync();
        await CreateServiceAsync(admin, "Redigér Test", "30", "200");
        var id = await IdOfAsync("Redigér Test");
        var date = TestData.FutureDate(DayOfWeek.Wednesday).ToString("yyyy-MM-dd");
        var anonymous = _factory.CreateBrowser();
        var before = Regex.Matches(await PageAsync(anonymous, $"/Booking/Availability?barberId=1&serviceId={id}&bookingDate={date}&days=1"), "\"time\"").Count;

        var token = await admin.GetTokenAsync($"/Services/Edit/{id}");
        var response = await admin.PostFormAsync($"/Services/Edit/{id}", token,
            ("Name", "Redigér Test 2"), ("Description", "Ny tekst"), ("DurationMinutes", "120"), ("Price", "999"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var prices = await PageAsync(anonymous, "/Home/Prices");
        Assert.Contains("Redigér Test 2", prices);
        Assert.Contains("999 kr.", prices);
        Assert.Contains("120 minutter", prices);
        var after = Regex.Matches(await PageAsync(anonymous, $"/Booking/Availability?barberId=1&serviceId={id}&bookingDate={date}&days=1"), "\"time\"").Count;
        Assert.True(after < before, $"A 120 minute treatment must leave fewer start times than 30 minutes ({after} vs {before}).");
    }

    [Fact(DisplayName = "Editing a treatment does not change the duration or price of bookings already made")]
    public async Task Edit_keeps_existing_bookings_unchanged()
    {
        var admin = await AdminAsync();
        await CreateServiceAsync(admin, "Historik Test", "30", "200");
        var id = await IdOfAsync("Historik Test");
        var bookingId = await DbAsync(async db =>
        {
            var user = new ApplicationUser { UserName = "user_hist", Email = "hist@example.com", SecurityStamp = Guid.NewGuid().ToString() };
            db.Users.Add(user);
            var booking = new Booking
            {
                BookingTime = TestData.FutureDate(DayOfWeek.Thursday).AddHours(10),
                Duration = TimeSpan.FromMinutes(30), Price = 200,
                BarberId = 1, Barber = (await db.Barbers.FindAsync(1))!,
                ServiceId = id, Service = (await db.Services.FindAsync(id))!,
                UserId = user.Id, User = user, IsConfirmed = true
            };
            db.Bookings.Add(booking);
            await db.SaveChangesAsync();
            return booking.Id;
        });

        var token = await admin.GetTokenAsync($"/Services/Edit/{id}");
        await admin.PostFormAsync($"/Services/Edit/{id}", token,
            ("Name", "Historik Test"), ("Description", ""), ("DurationMinutes", "90"), ("Price", "500"));

        var booking = await DbAsync(db => db.Bookings.SingleAsync(b => b.Id == bookingId));
        Assert.Equal(TimeSpan.FromMinutes(30), booking.Duration);
        Assert.Equal(200m, booking.Price);
    }

    [Fact(DisplayName = "A new booking records the treatment's price at that moment")]
    public async Task New_booking_stores_the_current_price()
    {
        var admin = await AdminAsync();
        await CreateServiceAsync(admin, "Pris Test", "30", "321");
        var id = await IdOfAsync("Pris Test");
        var customer = _factory.CreateBrowser();
        var token = await customer.GetTokenAsync("/Booking");
        var date = TestData.FutureDate(DayOfWeek.Friday).AddDays(7).ToString("yyyy-MM-dd");

        var response = await customer.PostFormAsync("/Booking", token,
            ("BarberId", "1"), ("ServiceId", id.ToString()), ("BookingDate", date), ("BookingTime", "10:00:00"),
            ("Email", "pris-test@example.com"), ("Name", "Pris"), ("Phone", "32123456"), ("Password", StrongPassword));
        Assert.Contains("er bekræftet", await response.BodyAsync());

        var etToken = await admin.GetTokenAsync($"/Services/Edit/{id}");
        await admin.PostFormAsync($"/Services/Edit/{id}", etToken,
            ("Name", "Pris Test"), ("Description", ""), ("DurationMinutes", "30"), ("Price", "777"));

        var booking = await DbAsync(db => db.Bookings.Include(b => b.User).SingleAsync(b => b.User.Email == "pris-test@example.com"));
        Assert.Equal(321m, booking.Price);
    }

    [Fact(DisplayName = "Moving a booking to another treatment updates its price")]
    public async Task Admin_changing_a_bookings_service_updates_the_price()
    {
        var admin = await AdminAsync();
        await CreateServiceAsync(admin, "Flyt Fra", "30", "100");
        await CreateServiceAsync(admin, "Flyt Til", "30", "555");
        var from = await IdOfAsync("Flyt Fra");
        var to = await IdOfAsync("Flyt Til");
        var date = TestData.FutureDate(DayOfWeek.Tuesday).AddDays(14);
        var bookingId = await DbAsync(async db =>
        {
            var user = new ApplicationUser { UserName = "user_flyt", Email = "flyt@example.com", SecurityStamp = Guid.NewGuid().ToString() };
            db.Users.Add(user);
            var booking = new Booking
            {
                BookingTime = date.AddHours(11), Duration = TimeSpan.FromMinutes(30), Price = 100,
                BarberId = 1, Barber = (await db.Barbers.FindAsync(1))!,
                ServiceId = from, Service = (await db.Services.FindAsync(from))!,
                UserId = user.Id, User = user, IsConfirmed = true
            };
            db.Bookings.Add(booking);
            await db.SaveChangesAsync();
            return booking.Id;
        });

        var token = await admin.GetTokenAsync("/Admin/Schedule");
        await admin.PostFormAsync("/Admin/EditBooking", token,
            ("id", bookingId.ToString()), ("date", date.ToString("yyyy-MM-dd")), ("barberId", "1"),
            ("serviceId", to.ToString()), ("time", "13:00:00"));

        var booking = await DbAsync(db => db.Bookings.SingleAsync(b => b.Id == bookingId));
        Assert.Equal(to, booking.ServiceId);
        Assert.Equal(555m, booking.Price);
    }

    // ---------- hide / show ----------

    [Fact(DisplayName = "A hidden treatment disappears from every public page and cannot be booked")]
    public async Task Hidden_service_is_gone_from_public_pages()
    {
        var admin = await AdminAsync();
        await CreateServiceAsync(admin, "Skjul Test");
        var id = await IdOfAsync("Skjul Test");
        var anonymous = _factory.CreateBrowser();
        Assert.Contains("Skjul Test", await PageAsync(anonymous, "/Home/Prices"));

        var token = await admin.GetTokenAsync("/Services");
        var hide = await admin.PostFormAsync("/Services/ToggleActive", token, ("id", id.ToString()));

        Assert.Equal(HttpStatusCode.Redirect, hide.StatusCode);
        foreach (var url in new[] { "/Home/Prices", "/", "/Booking", "/Home/About" })
        {
            Assert.DoesNotContain("Skjul Test", await PageAsync(anonymous, url));
        }
        var date = TestData.FutureDate(DayOfWeek.Wednesday).ToString("yyyy-MM-dd");
        var availability = await PageAsync(anonymous, $"/Booking/Availability?barberId=1&serviceId={id}&bookingDate={date}&days=1");
        Assert.DoesNotContain("\"time\"", availability);

        var bookingToken = await anonymous.GetTokenAsync("/Booking");
        var attempt = await anonymous.PostFormAsync("/Booking", bookingToken,
            ("BarberId", "1"), ("ServiceId", id.ToString()), ("BookingDate", date), ("BookingTime", "10:00:00"),
            ("Email", "skjult@example.com"), ("Name", "Skjult"), ("Phone", "32123456"), ("Password", StrongPassword));
        var body = await attempt.BodyAsync();
        Assert.Contains("Vælg en behandling.", body);
        Assert.DoesNotContain("er bekræftet", body);
        Assert.False(await DbAsync(db => db.Bookings.AnyAsync(b => b.ServiceId == id)));
    }

    [Fact(DisplayName = "Showing a hidden treatment again brings it back, and existing bookings are untouched")]
    public async Task Showing_a_service_again_restores_it()
    {
        var admin = await AdminAsync();
        await CreateServiceAsync(admin, "Vis Igen Test");
        var id = await IdOfAsync("Vis Igen Test");
        var token = await admin.GetTokenAsync("/Services");
        await admin.PostFormAsync("/Services/ToggleActive", token, ("id", id.ToString()));

        await admin.PostFormAsync("/Services/ToggleActive", token, ("id", id.ToString()));

        Assert.Contains("Vis Igen Test", await PageAsync(_factory.CreateBrowser(), "/Home/Prices"));
        Assert.True((await DbFindAsync(db => db.Services.FindAsync(id)))!.IsActive);
    }

    [Fact(DisplayName = "A hidden treatment keeps working for the bookings that already use it")]
    public async Task Hidden_service_still_resolves_for_old_bookings()
    {
        var admin = await AdminAsync();
        await CreateServiceAsync(admin, "Gammel Booking Test");
        var id = await IdOfAsync("Gammel Booking Test");
        await DbAsync<int>(async db =>
        {
            var user = new ApplicationUser { UserName = "user_gammel", Email = "gammel@example.com", SecurityStamp = Guid.NewGuid().ToString() };
            db.Users.Add(user);
            db.Bookings.Add(new Booking
            {
                BookingTime = TestData.FutureDate(DayOfWeek.Monday).AddDays(21).AddHours(10), Duration = TimeSpan.FromMinutes(45), Price = 300,
                BarberId = 1, Barber = (await db.Barbers.FindAsync(1))!, ServiceId = id,
                Service = (await db.Services.FindAsync(id))!, UserId = user.Id, User = user, IsConfirmed = true, CustomerName = "Gammel Kunde"
            });
            await db.SaveChangesAsync();
            return 0;
        });
        var token = await admin.GetTokenAsync("/Services");
        await admin.PostFormAsync("/Services/ToggleActive", token, ("id", id.ToString()));

        var day = TestData.FutureDate(DayOfWeek.Monday).AddDays(21).ToString("yyyy-MM-dd");
        var schedule = await PageAsync(admin, $"/Admin/Schedule?date={day}");

        Assert.Contains("Gammel Kunde", schedule);
        Assert.Contains("Gammel Booking Test", schedule);
    }

    [Fact(DisplayName = "The last visible treatment cannot be hidden")]
    public async Task Last_visible_service_cannot_be_hidden()
    {
        using var factory = new AppFactory();
        var admin = await AdminAsync(factory);
        var token = await admin.GetTokenAsync("/Services");

        foreach (var id in new[] { 1, 2 })
        {
            await admin.PostFormAsync("/Services/ToggleActive", token, ("id", id.ToString()));
        }
        var last = await admin.PostFormAsync("/Services/ToggleActive", token, ("id", "3"));

        Assert.Equal(HttpStatusCode.Redirect, last.StatusCode);
        var list = await PageAsync(admin, "/Services");
        Assert.Contains("Mindst én behandling skal være synlig", list);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.True((await db.Services.FindAsync(3))!.IsActive);
    }

    [Fact(DisplayName = "Hiding an unknown treatment returns NotFound")]
    public async Task Unknown_service_is_not_found()
    {
        var admin = await AdminAsync();
        var token = await admin.GetTokenAsync("/Services");

        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostFormAsync("/Services/ToggleActive", token, ("id", "9999"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/Services/Edit/9999")).StatusCode);
    }

    // ---------- order ----------

    [Fact(DisplayName = "Moving a treatment up or down changes the order on the public pages, and the ends stay put")]
    public async Task Sort_order_can_be_changed()
    {
        using var factory = new AppFactory();
        var admin = await AdminAsync(factory);
        var anonymous = factory.CreateBrowser();
        var token = await admin.GetTokenAsync("/Services");

        await admin.PostFormAsync("/Services/Move", token, ("id", "3"), ("direction", "up"));
        var prices = await PageAsync(anonymous, "/Home/Prices");
        Assert.True(prices.IndexOf("Skægtrimning", StringComparison.Ordinal) < prices.IndexOf("Børneklip", StringComparison.Ordinal));

        await admin.PostFormAsync("/Services/Move", token, ("id", "1"), ("direction", "up")); // already first
        prices = await PageAsync(anonymous, "/Home/Prices");
        Assert.True(prices.IndexOf("Herreklip", StringComparison.Ordinal) < prices.IndexOf("Skægtrimning", StringComparison.Ordinal));

        var booking = await PageAsync(anonymous, "/Booking");
        Assert.True(booking.IndexOf("Skægtrimning", StringComparison.Ordinal) < booking.IndexOf("Børneklip", StringComparison.Ordinal));

        using var scope = factory.Services.CreateScope();
        var orders = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Services.OrderBy(s => s.Id).Select(s => s.SortOrder).ToListAsync();
        Assert.Equal(new[] { 1, 3, 2 }, orders);
    }

    [Fact(DisplayName = "A move request with a bad direction is rejected")]
    public async Task Bad_move_direction_is_rejected()
    {
        var admin = await AdminAsync();
        var token = await admin.GetTokenAsync("/Services");

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostFormAsync("/Services/Move", token, ("id", "1"), ("direction", "sideways"))).StatusCode);
    }

    [Fact(DisplayName = "Changing treatments needs the antiforgery token")]
    public async Task Post_requires_antiforgery_token()
    {
        var admin = await AdminAsync();

        var response = await admin.PostAsync("/Services/ToggleActive", new FormUrlEncodedContent(HttpExtensions.Form(("id", "1"))));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await DbFindAsync(db => db.Services.FindAsync(1)))!.IsActive);
    }
}
