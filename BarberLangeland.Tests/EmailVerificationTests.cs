using System.Net;
using System.Text.RegularExpressions;
using BarberLangeland.Data;
using BarberLangeland.Models;
using BarberLangeland.Services;
using BarberLangeland.Tests.Support;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BarberLangeland.Tests;

public class EmailVerificationServiceTests
{
    private const string StrongPassword = "Test-Booking-2026!";

    private static async Task<ApplicationUser> RegisterAsync(IdentityHarness h, string email = "kunde@example.com")
    {
        var result = await h.Customers.RegisterAsync(email, "Kunde", "32123456", StrongPassword);
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.Description)));
        return (await h.Users.FindByEmailAsync(email))!;
    }

    private static Booking AddBooking(IdentityHarness h, ApplicationUser user, DateTime start,
        bool confirmed = false, bool cancelled = false, bool noShow = false)
    {
        var service = h.Db.Services.Find(TestData.HaircutServiceId)!;
        var booking = new Booking
        {
            BookingTime = start,
            Duration = TimeSpan.FromMinutes(service.DurationMinutes),
            BarberId = TestData.BarberId,
            Barber = h.Db.Barbers.Find(TestData.BarberId)!,
            ServiceId = service.Id,
            Service = service,
            UserId = user.Id,
            User = user,
            IsConfirmed = confirmed,
            IsCancelled = cancelled,
            IsNoShow = noShow
        };
        h.Db.Bookings.Add(booking);
        h.Db.SaveChanges();
        return booking;
    }

    private static string CodeFrom(SentEmail mail)
        => Regex.Match(FakeEmailSender.LinkPathAndQuery(mail), @"code=([^&\s]+)").Groups[1].Value;

    [Fact(DisplayName = "With mail configured a new account starts with an unconfirmed email")]
    public async Task New_account_is_unconfirmed_when_mail_works()
    {
        using var h = new IdentityHarness(emailConfigured: true);

        var user = await RegisterAsync(h);

        Assert.False(user.EmailConfirmed);
        Assert.True(h.Verification.IsEnabled);
        Assert.True(user.RegisteredAt > DateTime.Today.AddDays(-1));
    }

    [Fact(DisplayName = "Without mail configured a new account is confirmed, so nobody waits for a mail that never comes")]
    public async Task New_account_is_confirmed_when_mail_is_not_configured()
    {
        using var h = new IdentityHarness(emailConfigured: false);

        var user = await RegisterAsync(h);

        Assert.True(user.EmailConfirmed);
        Assert.False(h.Verification.IsEnabled);
    }

    [Fact(DisplayName = "The confirmation mail goes to the customer with a link, the deadline and a Danish subject")]
    public async Task Confirmation_mail_contents()
    {
        using var h = new IdentityHarness(emailConfigured: true);
        var user = await RegisterAsync(h);

        var sent = await h.Verification.SendConfirmationAsync(user, code => $"https://www.example.dk/Account/ConfirmEmail?userId={user.Id}&code={code}");

        Assert.True(sent);
        var mail = Assert.Single(h.Email.Sent);
        Assert.Equal("kunde@example.com", mail.To);
        Assert.Equal("Bekræft din e-mail hos Frisør Langeland", mail.Subject);
        Assert.Contains("https://www.example.dk/Account/ConfirmEmail?userId=", mail.Text);
        Assert.Contains("24 timer", mail.Text);
        Assert.Contains("24 timer", mail.Html);
        Assert.Contains("&amp;code=", mail.Html);
    }

    [Fact(DisplayName = "No mail is sent when mail is not configured")]
    public async Task No_mail_without_configuration()
    {
        using var h = new IdentityHarness(emailConfigured: false);
        var user = await RegisterAsync(h);

        Assert.False(await h.Verification.SendConfirmationAsync(user, c => c));
        Assert.Empty(h.Email.Sent);
    }

    [Fact(DisplayName = "A mail server failure is reported as false and does not throw")]
    public async Task Mail_failure_does_not_throw()
    {
        using var h = new IdentityHarness(emailConfigured: true);
        var user = await RegisterAsync(h);
        h.Email.Fail = true;

        Assert.False(await h.Verification.SendConfirmationAsync(user, c => c));
    }

    [Fact(DisplayName = "The link confirms the email and the customer's waiting bookings, but not cancelled or no-show ones")]
    public async Task Confirming_confirms_waiting_bookings_only()
    {
        using var h = new IdentityHarness(emailConfigured: true);
        var user = await RegisterAsync(h);
        var date = TestData.FutureDate(DayOfWeek.Wednesday);
        var waiting = AddBooking(h, user, date.AddHours(10));
        var cancelled = AddBooking(h, user, date.AddHours(11), cancelled: true);
        var noShow = AddBooking(h, user, date.AddHours(12), noShow: true);
        await h.Verification.SendConfirmationAsync(user, c => $"https://x/Account/ConfirmEmail?userId={user.Id}&code={c}");

        var ok = await h.Verification.ConfirmAsync(user.Id, CodeFrom(Assert.Single(h.Email.Sent)));

        Assert.True(ok);
        h.Db.ChangeTracker.Clear();
        Assert.True((await h.Users.FindByIdAsync(user.Id))!.EmailConfirmed);
        Assert.True((await h.Db.Bookings.FindAsync(waiting.Id))!.IsConfirmed);
        Assert.False((await h.Db.Bookings.FindAsync(cancelled.Id))!.IsConfirmed);
        Assert.False((await h.Db.Bookings.FindAsync(noShow.Id))!.IsConfirmed);
    }

    [Theory(DisplayName = "A wrong, garbled or missing code confirms nothing")]
    [InlineData("nonsense")]
    [InlineData("!!!")]
    [InlineData("")]
    [InlineData(null)]
    public async Task Bad_codes_are_refused(string? code)
    {
        using var h = new IdentityHarness(emailConfigured: true);
        var user = await RegisterAsync(h);
        var booking = AddBooking(h, user, TestData.FutureDate(DayOfWeek.Wednesday).AddHours(10));

        Assert.False(await h.Verification.ConfirmAsync(user.Id, code));

        h.Db.ChangeTracker.Clear();
        Assert.False((await h.Users.FindByIdAsync(user.Id))!.EmailConfirmed);
        Assert.False((await h.Db.Bookings.FindAsync(booking.Id))!.IsConfirmed);
    }

    [Fact(DisplayName = "A code made for one customer does not confirm another")]
    public async Task Code_is_bound_to_its_customer()
    {
        using var h = new IdentityHarness(emailConfigured: true);
        var first = await RegisterAsync(h, "en@example.com");
        var second = await RegisterAsync(h, "to@example.com");
        await h.Verification.SendConfirmationAsync(first, c => $"https://x/Account/ConfirmEmail?userId={first.Id}&code={c}");

        Assert.False(await h.Verification.ConfirmAsync(second.Id, CodeFrom(Assert.Single(h.Email.Sent))));

        h.Db.ChangeTracker.Clear();
        Assert.False((await h.Users.FindByIdAsync(second.Id))!.EmailConfirmed);
    }

    [Fact(DisplayName = "An unknown customer id confirms nothing")]
    public async Task Unknown_user_is_refused()
    {
        using var h = new IdentityHarness(emailConfigured: true);
        Assert.False(await h.Verification.ConfirmAsync("finnes-ikke", "abcd"));
    }

    [Fact(DisplayName = "Opening the link a second time still works for the same customer")]
    public async Task Link_can_be_opened_twice()
    {
        using var h = new IdentityHarness(emailConfigured: true);
        var user = await RegisterAsync(h);
        await h.Verification.SendConfirmationAsync(user, c => $"https://x/Account/ConfirmEmail?userId={user.Id}&code={c}");
        var code = CodeFrom(Assert.Single(h.Email.Sent));

        Assert.True(await h.Verification.ConfirmAsync(user.Id, code));
        Assert.True(await h.Verification.ConfirmAsync(user.Id, code));
        Assert.False(await h.Verification.ConfirmAsync(user.Id, code + "x"));
    }

    [Fact(DisplayName = "An expired link is refused")]
    public async Task Expired_link_is_refused()
    {
        using var h = new IdentityHarness(emailConfigured: true, tokenLifespan: TimeSpan.FromSeconds(1));
        var user = await RegisterAsync(h);
        await h.Verification.SendConfirmationAsync(user, c => $"https://x/Account/ConfirmEmail?userId={user.Id}&code={c}");
        var code = CodeFrom(Assert.Single(h.Email.Sent));

        await Task.Delay(TimeSpan.FromSeconds(2.5));

        Assert.False(await h.Verification.ConfirmAsync(user.Id, code));
    }

    [Fact(DisplayName = "The confirmation window is 24 hours")]
    public void Window_is_24_hours()
    {
        using var h = new IdentityHarness(emailConfigured: true);
        Assert.Equal(TimeSpan.FromHours(24), h.Verification.ConfirmationWindow);
    }

    [Fact(DisplayName = "Clean-up removes old unconfirmed accounts with their bookings, which frees the times")]
    public async Task Cleanup_removes_stale_unconfirmed_accounts_and_frees_slots()
    {
        using var h = new IdentityHarness(emailConfigured: true);
        var stale = await RegisterAsync(h, "gammel@example.com");
        var date = TestData.FutureDate(DayOfWeek.Wednesday);
        AddBooking(h, stale, date.AddHours(10));
        stale.RegisteredAt = DateTime.Now.AddHours(-30);
        await h.Db.SaveChangesAsync();

        var removed = await h.Verification.DeleteUnconfirmedAccountsAsync(DateTime.Now.AddHours(-24));

        Assert.Equal(1, removed);
        h.Db.ChangeTracker.Clear();
        Assert.Null(await h.Users.FindByEmailAsync("gammel@example.com"));
        Assert.Empty(h.Db.Bookings);
        var availability = new BookingAvailabilityService(h.Db, new CopenhagenTimeProvider());
        var days = await availability.GetAvailableDaysAsync(TestData.BarberId, TestData.HaircutServiceId, date, 1);
        Assert.Contains(new TimeSpan(10, 0, 0), days.Single().Slots.Select(s => s.Time));
    }

    [Fact(DisplayName = "Clean-up keeps recent unconfirmed accounts, confirmed accounts and the administrator")]
    public async Task Cleanup_keeps_everyone_else()
    {
        using var h = new IdentityHarness(emailConfigured: true);
        var recent = await RegisterAsync(h, "ny@example.com");
        var confirmedOld = await RegisterAsync(h, "bekraeftet@example.com");
        confirmedOld.EmailConfirmed = true;
        confirmedOld.RegisteredAt = DateTime.Now.AddDays(-60);
        var admin = await RegisterAsync(h, "admin@example.com");
        admin.RegisteredAt = DateTime.Now.AddDays(-60);
        var roles = h.Db.Roles;
        roles.Add(new IdentityRole(IdentitySeeder.AdminRole) { Id = "admin-role", NormalizedName = "ADMIN" });
        h.Db.UserRoles.Add(new IdentityUserRole<string> { UserId = admin.Id, RoleId = "admin-role" });
        await h.Db.SaveChangesAsync();

        var removed = await h.Verification.DeleteUnconfirmedAccountsAsync(DateTime.Now.AddHours(-24));

        Assert.Equal(0, removed);
        Assert.Equal(3, await h.Db.Users.CountAsync());
    }
}

public class VerifyingAppFactory : AppFactory
{
    protected override bool EmailConfigured => true;
}

public class EmailVerificationWebTests : IClassFixture<VerifyingAppFactory>
{
    private const string StrongPassword = "Test-Booking-2026!";
    private static int _slot;
    private readonly VerifyingAppFactory _factory;

    public EmailVerificationWebTests(VerifyingAppFactory factory) => _factory = factory;

    private static string NextTime() => new TimeSpan(9, 30, 0).Add(TimeSpan.FromMinutes(30 * Interlocked.Increment(ref _slot))).ToString();

    private static string BookingDay() => TestData.FutureDate(DayOfWeek.Monday).AddDays(42).ToString("yyyy-MM-dd");

    private static (string, string)[] Fields(string email, string? name, string? phone, string password, string time) =>
    [
        ("BarberId", "1"), ("ServiceId", "1"), ("BookingDate", BookingDay()), ("BookingTime", time),
        ("Email", email), ("Password", password),
        .. name == null ? Array.Empty<(string, string)>() : [("Name", name)],
        .. phone == null ? Array.Empty<(string, string)>() : [("Phone", phone)]
    ];

    /// <summary>Books as a new customer; returns the client (signed in), the page and the time used.</summary>
    private async Task<(HttpClient Client, string Body, string Time)> BookNewCustomerAsync(string email)
    {
        var client = _factory.CreateBrowser();
        var token = await client.GetTokenAsync("/Booking");
        var time = NextTime();
        var response = await client.PostFormAsync("/Booking", token, Fields(email, "Kunde", "32123456", StrongPassword, time));
        return (client, await response.BodyAsync(), time);
    }

    private async Task<T> WithDbAsync<T>(Func<ApplicationDbContext, Task<T>> action)
    {
        using var scope = _factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }

    private static async Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password)
    {
        var token = await client.GetTokenAsync("/Account/Login");
        return await client.PostFormAsync("/Account/Login", token, ("Email", email), ("Password", password));
    }

    [Fact(DisplayName = "A new customer's booking is reserved, not confirmed, and a mail with a link is sent")]
    public async Task New_customer_gets_a_reservation_and_a_mail()
    {
        var (_, body, time) = await BookNewCustomerAsync("verify1@example.com");

        Assert.Contains("er reserveret", body);
        Assert.DoesNotContain("er bekræftet", body);
        Assert.Contains("Bekræft din e-mail", body);
        Assert.Contains("verify1@example.com", body);
        Assert.Contains("inden for 24 timer", body);

        var mail = Assert.Single(_factory.Emails.Sent, m => m.To == "verify1@example.com");
        Assert.Contains("/Account/ConfirmEmail?userId=", FakeEmailSender.LinkPathAndQuery(mail));

        var pending = await WithDbAsync(db => db.Bookings.Include(b => b.User)
            .SingleAsync(b => b.User.Email == "verify1@example.com"));
        Assert.False(pending.IsConfirmed);
        Assert.False(pending.User.EmailConfirmed);
        Assert.Equal(TimeSpan.Parse(time), pending.BookingTime.TimeOfDay);
    }

    [Fact(DisplayName = "A reserved time is not offered to other customers")]
    public async Task Reserved_time_is_blocked()
    {
        var (client, _, time) = await BookNewCustomerAsync("verify2@example.com");

        var availability = await client.GetStringAsync($"/Booking/Availability?barberId=1&serviceId=1&bookingDate={BookingDay()}&days=1");

        Assert.DoesNotContain($"\"{TimeSpan.Parse(time):hh\\:mm}\"", availability);
    }

    [Fact(DisplayName = "Opening the link, even when not signed in, confirms the email and the booking")]
    public async Task Link_confirms_email_and_booking()
    {
        await BookNewCustomerAsync("verify3@example.com");
        var link = FakeEmailSender.LinkPathAndQuery(_factory.Emails.Sent.Single(m => m.To == "verify3@example.com"));

        var stranger = _factory.CreateBrowser();
        var response = await stranger.GetAsync(link);
        var body = await response.BodyAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("din e-mail er bekræftet", body);
        var booking = await WithDbAsync(db => db.Bookings.Include(b => b.User).SingleAsync(b => b.User.Email == "verify3@example.com"));
        Assert.True(booking.IsConfirmed);
        Assert.True(booking.User.EmailConfirmed);
    }

    [Fact(DisplayName = "A tampered link shows a friendly message and confirms nothing")]
    public async Task Tampered_link_is_refused()
    {
        await BookNewCustomerAsync("verify4@example.com");
        var link = FakeEmailSender.LinkPathAndQuery(_factory.Emails.Sent.Single(m => m.To == "verify4@example.com"));

        var response = await _factory.CreateBrowser().GetAsync(link[..^3] + "abc");
        var body = await response.BodyAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Linket virker ikke", body);
        var booking = await WithDbAsync(db => db.Bookings.Include(b => b.User).SingleAsync(b => b.User.Email == "verify4@example.com"));
        Assert.False(booking.IsConfirmed);
    }

    [Fact(DisplayName = "The link page without parameters shows the friendly message, not an error")]
    public async Task Link_without_parameters_is_handled()
    {
        var response = await _factory.CreateBrowser().GetAsync("/Account/ConfirmEmail");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Linket virker ikke", await response.BodyAsync());
    }

    [Fact(DisplayName = "My bookings warns about the unconfirmed email and marks the booking as waiting")]
    public async Task My_bookings_shows_banner_and_status()
    {
        var (client, _, _) = await BookNewCustomerAsync("verify5@example.com");

        var body = await (await client.GetAsync("/Account/MyBookings")).BodyAsync();

        Assert.Contains("data-email-unconfirmed", body);
        Assert.Contains("verify5@example.com", body);
        Assert.Contains("Afventer e-mail", body);
        Assert.Contains("Send bekræftelsesmail igen", body);
    }

    [Fact(DisplayName = "After confirming, the warning is gone and the booking is confirmed")]
    public async Task Banner_disappears_after_confirming()
    {
        var (client, _, _) = await BookNewCustomerAsync("verify6@example.com");
        await client.GetAsync(FakeEmailSender.LinkPathAndQuery(_factory.Emails.Sent.Single(m => m.To == "verify6@example.com")));

        var body = await (await client.GetAsync("/Account/MyBookings")).BodyAsync();

        Assert.DoesNotContain("data-email-unconfirmed", body);
        Assert.DoesNotContain("Afventer e-mail", body);
        Assert.Contains("Bekræftet", body);
    }

    [Fact(DisplayName = "A customer can ask for the mail again, and only a few times per hour")]
    public async Task Resend_sends_mail_and_is_limited()
    {
        var (client, _, _) = await BookNewCustomerAsync("verify7@example.com");
        var token = await client.GetTokenAsync("/Account/MyBookings");
        var before = _factory.Emails.Sent.Count(m => m.To == "verify7@example.com");

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 5; i++)
        {
            statuses.Add((await client.PostFormAsync("/Account/ResendConfirmation", token)).StatusCode);
        }

        Assert.Equal(before + 3, _factory.Emails.Sent.Count(m => m.To == "verify7@example.com"));
        Assert.Equal(3, statuses.Count(s => s == HttpStatusCode.Redirect));
        Assert.Equal(2, statuses.Count(s => s == HttpStatusCode.TooManyRequests));
    }

    [Fact(DisplayName = "Asking for the mail again requires being signed in")]
    public async Task Resend_requires_login()
    {
        var client = _factory.CreateBrowser();
        var token = await client.GetTokenAsync("/Account/Login");

        var response = await client.PostFormAsync("/Account/ResendConfirmation", token);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Account/Login", response.Headers.Location!.AbsolutePath);
    }

    [Fact(DisplayName = "A confirmed customer is not sent another mail")]
    public async Task Confirmed_customer_gets_no_resend()
    {
        var (client, _, _) = await BookNewCustomerAsync("verify8@example.com");
        await client.GetAsync(FakeEmailSender.LinkPathAndQuery(_factory.Emails.Sent.Single(m => m.To == "verify8@example.com")));
        var before = _factory.Emails.Sent.Count(m => m.To == "verify8@example.com");
        var token = await client.GetTokenAsync("/Account/MyBookings");

        await client.PostFormAsync("/Account/ResendConfirmation", token);

        Assert.Equal(before, _factory.Emails.Sent.Count(m => m.To == "verify8@example.com"));
    }

    [Fact(DisplayName = "A returning customer with an unconfirmed email books another time, held until confirmed")]
    public async Task Returning_unconfirmed_customer_books_pending()
    {
        await BookNewCustomerAsync("verify9@example.com");
        var client = _factory.CreateBrowser();
        var token = await client.GetTokenAsync("/Booking");

        var response = await client.PostFormAsync("/Booking", token, Fields("verify9@example.com", null, null, StrongPassword, NextTime()));
        var body = await response.BodyAsync();

        Assert.Contains("er reserveret", body);
        var bookings = await WithDbAsync(db => db.Bookings.Include(b => b.User).Where(b => b.User.Email == "verify9@example.com").ToListAsync());
        Assert.Equal(2, bookings.Count);
        Assert.All(bookings, b => Assert.False(b.IsConfirmed));
        Assert.Equal(2, _factory.Emails.Sent.Count(m => m.To == "verify9@example.com"));
    }

    [Fact(DisplayName = "A confirmed customer's next booking is confirmed straight away, without a mail")]
    public async Task Confirmed_customer_books_confirmed()
    {
        var (first, _, _) = await BookNewCustomerAsync("verify10@example.com");
        await first.GetAsync(FakeEmailSender.LinkPathAndQuery(_factory.Emails.Sent.Single(m => m.To == "verify10@example.com")));
        var client = _factory.CreateBrowser();
        var token = await client.GetTokenAsync("/Booking");

        var body = await (await client.PostFormAsync("/Booking", token,
            Fields("verify10@example.com", null, null, StrongPassword, NextTime()))).BodyAsync();

        Assert.Contains("er bekræftet", body);
        Assert.Single(_factory.Emails.Sent, m => m.To == "verify10@example.com");
    }

    [Fact(DisplayName = "If the mail cannot be sent the booking is kept and the customer is told how to get a new mail")]
    public async Task Mail_failure_keeps_the_booking()
    {
        _factory.Emails.Fail = true;
        string body;
        try
        {
            (_, body, _) = await BookNewCustomerAsync("verify11@example.com");
        }
        finally
        {
            _factory.Emails.Fail = false;
        }

        Assert.Contains("er reserveret", body);
        Assert.Contains("kunne ikke sende bekræftelsesmailen", body);
        Assert.Contains("Mine bookinger", body);
        Assert.DoesNotContain(_factory.Emails.Sent, m => m.To == "verify11@example.com");
        Assert.Single(await WithDbAsync(db => db.Bookings.Include(b => b.User).Where(b => b.User.Email == "verify11@example.com").ToListAsync()));
    }

    [Fact(DisplayName = "The administrator sees a booking that waits for the customer's email")]
    public async Task Admin_sees_waiting_bookings()
    {
        await BookNewCustomerAsync("verify12@example.com");
        await _factory.EnsureAdminAsync();
        var admin = _factory.CreateBrowser();
        await LoginAsync(admin, AppFactory.AdminEmail, AppFactory.AdminPassword);

        var body = await (await admin.GetAsync($"/Admin/Schedule?date={BookingDay()}")).BodyAsync();

        Assert.Contains("Afventer e-mail", body);
    }

    [Theory(DisplayName = "The link in the mail uses the configured public address when there is one")]
    [InlineData("https://www.frisorlangeland.dk", "https://www.frisorlangeland.dk/Account/ConfirmEmail?userId=u1&code=c1")]
    [InlineData("https://www.frisorlangeland.dk/", "https://www.frisorlangeland.dk/Account/ConfirmEmail?userId=u1&code=c1")]
    [InlineData("", "https://minhost.example/Account/ConfirmEmail?userId=u1&code=c1")]
    public void Link_uses_base_url(string baseUrl, string expected)
    {
        var http = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        http.Request.Scheme = "https";
        http.Request.Host = new Microsoft.AspNetCore.Http.HostString("minhost.example");
        var url = new StubUrlHelper();

        var link = ConfirmationLinks.Build(url, http.Request, new SiteOptions { BaseUrl = baseUrl }, "u1", "c1");

        Assert.Equal(expected, link);
    }

    private sealed class StubUrlHelper : Microsoft.AspNetCore.Mvc.IUrlHelper
    {
        public Microsoft.AspNetCore.Mvc.ActionContext ActionContext => throw new NotSupportedException();
        public string? Action(Microsoft.AspNetCore.Mvc.Routing.UrlActionContext actionContext)
        {
            var values = new Microsoft.AspNetCore.Routing.RouteValueDictionary(actionContext.Values);
            return $"/Account/ConfirmEmail?userId={values["userId"]}&code={values["code"]}";
        }
        public string? Content(string? contentPath) => contentPath;
        public bool IsLocalUrl(string? url) => true;
        public string? RouteUrl(Microsoft.AspNetCore.Mvc.Routing.UrlRouteContext routeContext) => null;
        public string? Link(string? routeName, object? values) => null;
    }
}
