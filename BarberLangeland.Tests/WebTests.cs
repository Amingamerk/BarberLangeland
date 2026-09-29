using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BarberLangeland.Tests.Support;
using Xunit;

namespace BarberLangeland.Tests;

/// <summary>
/// Black-box tests against the real ASP.NET Core pipeline (Production environment), mirroring
/// the probes run against the live site.
/// </summary>
public class WebTests : IClassFixture<AppFactory>
{
    private const string StrongPassword = "Test-Booking-2026!";
    private static int _slotCounter;
    private readonly AppFactory _factory;

    public WebTests(AppFactory factory)
    {
        _factory = factory;
    }

    // ---------- Public pages and routing ----------

    [Theory(DisplayName = "Public pages respond with 200")]
    [InlineData("/")]
    [InlineData("/Home/About")]
    [InlineData("/Home/Prices")]
    [InlineData("/Booking")]
    [InlineData("/Account/Login")]
    public async Task Public_pages_return_200(string url)
    {
        var response = await _factory.CreateBrowser().GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory(DisplayName = "Protected pages redirect anonymous visitors to the custom login page")]
    [InlineData("/Admin/Schedule")]
    [InlineData("/Barbers")]
    [InlineData("/Barbers/Create")]
    [InlineData("/Account/MyBookings")]
    public async Task Protected_pages_redirect_to_login(string url)
    {
        var response = await _factory.CreateBrowser().GetAsync(url);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Account/Login", response.Headers.Location!.AbsolutePath);
    }

    [Fact(DisplayName = "An unknown URL shows a friendly not-found page in the site layout")]
    public async Task Unknown_url_shows_friendly_404()
    {
        var response = await _factory.CreateBrowser().GetAsync("/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.BodyAsync();
        Assert.True(body.Contains("<html", StringComparison.OrdinalIgnoreCase),
            "The 404 response has an empty body: visitors get a blank browser error page instead of the site layout.");
    }

    [Fact(DisplayName = "The error page does not tell visitors about the Development environment")]
    public async Task Error_page_hides_development_advice()
    {
        var response = await _factory.CreateBrowser().GetAsync("/Home/Error");
        var body = await response.BodyAsync();

        Assert.DoesNotContain("Development environment", body);
        Assert.DoesNotContain("ASPNETCORE_ENVIRONMENT", body);
    }

    [Fact(DisplayName = "Responses carry basic browser security headers")]
    public async Task Responses_have_security_headers()
    {
        var response = await _factory.CreateBrowser().GetAsync("/");

        Assert.True(response.Headers.Contains("X-Content-Type-Options"), "X-Content-Type-Options is missing.");
        Assert.True(
            response.Headers.Contains("X-Frame-Options") || response.Headers.Contains("Content-Security-Policy"),
            "Neither X-Frame-Options nor Content-Security-Policy is set, so the site can be framed (clickjacking).");
    }

    [Theory(DisplayName = "The stock Identity UI pages are not exposed next to the custom login")]
    [InlineData("/Identity/Account/Register")]
    [InlineData("/Identity/Account/Login")]
    [InlineData("/Identity/Account/ForgotPassword")]
    public async Task Stock_identity_pages_are_not_reachable(string url)
    {
        var response = await _factory.CreateBrowser().GetAsync(url);

        Assert.True(response.StatusCode == HttpStatusCode.NotFound || response.StatusCode == HttpStatusCode.Redirect,
            $"{url} returned {(int)response.StatusCode}: the default Identity page is publicly reachable.");
    }

    [Fact(DisplayName = "Logout cannot be triggered with a plain GET")]
    public async Task Logout_requires_post()
    {
        var response = await _factory.CreateBrowser().GetAsync("/Account/Logout");
        Assert.True(response.StatusCode is HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotFound,
            $"GET /Account/Logout returned {(int)response.StatusCode}.");
    }

    // ---------- Availability endpoint ----------

    private static string Today => DateTime.Today.ToString("yyyy-MM-dd");

    [Theory(DisplayName = "The availability endpoint rejects invalid ids with 400")]
    [InlineData("barberId=0&serviceId=1")]
    [InlineData("barberId=1&serviceId=0")]
    [InlineData("barberId=-5&serviceId=1")]
    [InlineData("barberId=abc&serviceId=1")]
    public async Task Availability_rejects_invalid_ids(string query)
    {
        var response = await _factory.CreateBrowser().GetAsync($"/Booking/Availability?{query}&bookingDate={Today}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory(DisplayName = "The availability endpoint rejects past and malformed dates with 400")]
    [InlineData("2020-01-01")]
    [InlineData("notadate")]
    public async Task Availability_rejects_bad_dates(string date)
    {
        var response = await _factory.CreateBrowser().GetAsync($"/Booking/Availability?barberId=1&serviceId=1&bookingDate={date}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact(DisplayName = "The availability endpoint returns slots for a valid request")]
    public async Task Availability_returns_slots()
    {
        var date = TestData.FutureDate(DayOfWeek.Wednesday).ToString("yyyy-MM-dd");
        var response = await _factory.CreateBrowser().GetAsync($"/Booking/Availability?barberId=1&serviceId=1&bookingDate={date}&days=1");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(json[0].GetProperty("slots").GetArrayLength() > 0);
    }

    [Fact(DisplayName = "The availability endpoint caps the requested day window at 62 days")]
    public async Task Availability_caps_days()
    {
        var response = await _factory.CreateBrowser().GetAsync($"/Booking/Availability?barberId=1&serviceId=1&bookingDate={Today}&days=100000");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(62, json.GetArrayLength());
    }

    [Fact(DisplayName = "The availability endpoint offers no slots for a barber that does not exist")]
    public async Task Availability_for_unknown_barber_is_empty()
    {
        var date = TestData.FutureDate(DayOfWeek.Wednesday).ToString("yyyy-MM-dd");
        var response = await _factory.CreateBrowser().GetAsync($"/Booking/Availability?barberId=999&serviceId=1&bookingDate={date}&days=1");
        var text = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode == HttpStatusCode.NotFound || !text.Contains("\"time\""),
            "Slots were offered for barber 999, which does not exist.");
    }

    [Fact(DisplayName = "The availability endpoint answers extreme dates without a server error")]
    public async Task Availability_survives_extreme_dates()
    {
        var response = await _factory.CreateBrowser().GetAsync("/Booking/Availability?barberId=1&serviceId=1&bookingDate=9999-12-31&days=5");
        Assert.True((int)response.StatusCode < 500, $"Got HTTP {(int)response.StatusCode}.");
    }

    // ---------- CheckPhone endpoint ----------

    private async Task<(HttpClient Client, string Token)> BookingSessionAsync()
    {
        var client = _factory.CreateBrowser();
        return (client, await client.GetTokenAsync("/Booking"));
    }

    private static HttpRequestMessage CheckPhone(string token, string body) => new(HttpMethod.Post, "/Booking/CheckPhone")
    {
        Headers = { { "RequestVerificationToken", token } },
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    [Fact(DisplayName = "CheckPhone without an antiforgery token is rejected")]
    public async Task CheckPhone_requires_antiforgery_token()
    {
        var response = await _factory.CreateBrowser().PostAsJsonAsync("/Booking/CheckPhone", new { Phone = "32123456" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory(DisplayName = "CheckPhone rejects invalid, malformed and null bodies with 400")]
    [InlineData("{\"Phone\":\"abc\"}")]
    [InlineData("{bad")]
    [InlineData("null")]
    public async Task CheckPhone_rejects_bad_bodies(string body)
    {
        var (client, token) = await BookingSessionAsync();
        var response = await client.SendAsync(CheckPhone(token, body));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact(DisplayName = "CheckPhone tells the form only whether a number is registered, nothing else about the account")]
    public async Task CheckPhone_returns_only_the_exists_flag()
    {
        var (client, token) = await BookingSessionAsync();
        await RegisterCustomerAsync("32123400", "enumeration@example.com");

        var known = await (await client.SendAsync(CheckPhone(token, "{\"Phone\":\"32123400\"}"))).Content.ReadAsStringAsync();
        var unknown = await (await client.SendAsync(CheckPhone(token, "{\"Phone\":\"32123499\"}"))).Content.ReadAsStringAsync();

        Assert.Equal("{\"exists\":true}", known);
        Assert.Equal("{\"exists\":false}", unknown);
    }

    [Fact(DisplayName = "CheckPhone is rate limited")]
    public async Task CheckPhone_is_rate_limited()
    {
        var (client, token) = await BookingSessionAsync();
        var statuses = new List<HttpStatusCode>();

        for (var i = 0; i < 40; i++)
        {
            statuses.Add((await client.SendAsync(CheckPhone(token, "{\"Phone\":\"32123499\"}"))).StatusCode);
        }

        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
    }

    // ---------- Booking wizard POST ----------

    private static (string, string)[] BookingFields(string date, string time, string phone, string? name, string? email,
        string password, int barberId = 1, int serviceId = 1)
    {
        var fields = new List<(string, string)>
        {
            ("BarberId", barberId.ToString()), ("ServiceId", serviceId.ToString()),
            ("BookingDate", date), ("BookingTime", time), ("Phone", phone), ("Password", password)
        };
        if (name != null) fields.Add(("Name", name));
        if (email != null) fields.Add(("Email", email));
        return fields.ToArray();
    }

    /// <summary>Registers a customer through the booking form, using a slot no other test uses.</summary>
    private async Task<HttpResponseMessage> RegisterCustomerAsync(string phone, string email)
    {
        var client = _factory.CreateBrowser();
        var token = await client.GetTokenAsync("/Booking");
        var slot = Interlocked.Increment(ref _slotCounter);
        var date = TestData.FutureDate(DayOfWeek.Monday).AddDays(35).ToString("yyyy-MM-dd");
        var time = new TimeSpan(9, 30, 0).Add(TimeSpan.FromMinutes(30 * slot)).ToString();
        return await client.PostFormAsync("/Booking", token,
            BookingFields(date, time, phone, "Kunde", email, StrongPassword));
    }

    [Fact(DisplayName = "Posting the booking form without an antiforgery token is rejected")]
    public async Task Booking_post_requires_antiforgery_token()
    {
        var response = await _factory.CreateBrowser().PostAsync("/Booking",
            new FormUrlEncodedContent(HttpExtensions.Form(("BarberId", "1"), ("ServiceId", "1"))));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact(DisplayName = "An empty booking form shows Danish validation messages")]
    public async Task Empty_booking_form_shows_validation()
    {
        var (client, token) = await BookingSessionAsync();

        var response = await client.PostFormAsync("/Booking", token);
        var body = await response.BodyAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Vælg en frisør.", body);
        Assert.Contains("Vælg en behandling.", body);
        Assert.Contains("Indtast dit telefonnummer.", body);
    }

    [Fact(DisplayName = "A booking with an invalid phone number is refused")]
    public async Task Booking_with_invalid_phone_is_refused()
    {
        var (client, token) = await BookingSessionAsync();
        var date = TestData.FutureDate(DayOfWeek.Wednesday).ToString("yyyy-MM-dd");

        var response = await client.PostFormAsync("/Booking", token,
            BookingFields(date, "10:00:00", "12", "Kunde", "kunde@example.com", StrongPassword));

        Assert.Contains("Indtast et gyldigt telefonnummer.", await response.BodyAsync());
    }

    [Fact(DisplayName = "User input is HTML-encoded when the booking form is redisplayed")]
    public async Task Booking_form_encodes_user_input()
    {
        var (client, token) = await BookingSessionAsync();

        var response = await client.PostFormAsync("/Booking", token,
            ("BarberId", "1"), ("ServiceId", "1"),
            ("Name", "<script>alert(1)</script>"), ("Phone", "<img src=x onerror=alert(2)>"));
        var raw = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("<script>alert(1)</script>", raw);
        Assert.DoesNotContain("<img src=x onerror", raw);
    }

    [Fact(DisplayName = "Malformed date and time values produce Danish error messages")]
    public async Task Malformed_date_time_messages_are_danish()
    {
        var (client, token) = await BookingSessionAsync();

        var response = await client.PostFormAsync("/Booking", token,
            BookingFields("xx", "99:99", "32123456", null, null, ""));
        var body = await response.BodyAsync();

        Assert.DoesNotContain("is not valid for", body);
    }

    [Fact(DisplayName = "A new customer can book a slot, the slot disappears, and it cannot be booked twice")]
    public async Task Full_booking_flow()
    {
        var client = _factory.CreateBrowser();
        var token = await client.GetTokenAsync("/Booking");
        var date = TestData.FutureDate(DayOfWeek.Friday).AddDays(14).ToString("yyyy-MM-dd");

        var first = await client.PostFormAsync("/Booking", token,
            BookingFields(date, "11:00:00", "32123411", "Første Kunde", "forste@example.com", StrongPassword));
        Assert.Contains("er bekræftet", await first.BodyAsync());

        var availability = await client.GetStringAsync($"/Booking/Availability?barberId=1&serviceId=1&bookingDate={date}&days=1");
        Assert.DoesNotContain("\"11:00\"", availability);

        var other = _factory.CreateBrowser();
        var otherToken = await other.GetTokenAsync("/Booking");
        var second = await other.PostFormAsync("/Booking", otherToken,
            BookingFields(date, "11:00:00", "32123412", "Anden Kunde", "anden@example.com", StrongPassword));
        var secondBody = await second.BodyAsync();

        Assert.DoesNotContain("er bekræftet", secondBody);
        Assert.Contains("ikke længere ledigt", secondBody);
    }

    [Fact(DisplayName = "Booking with a weak password creates neither an account nor a booking")]
    public async Task Weak_password_creates_nothing()
    {
        var client = _factory.CreateBrowser();
        var token = await client.GetTokenAsync("/Booking");
        var date = TestData.FutureDate(DayOfWeek.Thursday).AddDays(21).ToString("yyyy-MM-dd");

        var response = await client.PostFormAsync("/Booking", token,
            BookingFields(date, "12:00:00", "32123422", "Svag Kunde", "svag@example.com", "abc"));

        Assert.DoesNotContain("er bekræftet", await response.BodyAsync());
        var availability = await client.GetStringAsync($"/Booking/Availability?barberId=1&serviceId=1&bookingDate={date}&days=1");
        Assert.Contains("\"12:00\"", availability);
    }

    [Fact(DisplayName = "A returning customer must give the right password to book")]
    public async Task Returning_customer_needs_password()
    {
        await RegisterCustomerAsync("32123433", "retur@example.com");
        var client = _factory.CreateBrowser();
        var token = await client.GetTokenAsync("/Booking");
        var date = TestData.FutureDate(DayOfWeek.Tuesday).AddDays(21).ToString("yyyy-MM-dd");

        var wrong = await client.PostFormAsync("/Booking", token,
            BookingFields(date, "13:00:00", "32123433", null, null, "Wrong-password-1"));
        Assert.Contains("Telefonnummer eller adgangskode er forkert", await wrong.BodyAsync());

        var right = await client.PostFormAsync("/Booking", token,
            BookingFields(date, "13:00:00", "32123433", null, null, StrongPassword));
        Assert.Contains("er bekræftet", await right.BodyAsync());
    }

    [Fact(DisplayName = "The booking form locks a customer out after repeated wrong passwords")]
    public async Task Booking_form_locks_out_after_repeated_wrong_passwords()
    {
        await RegisterCustomerAsync("32123466", "booking-laas@example.com");
        var client = _factory.CreateBrowser();
        var token = await client.GetTokenAsync("/Booking");
        var date = TestData.FutureDate(DayOfWeek.Tuesday).AddDays(28).ToString("yyyy-MM-dd");

        for (var i = 0; i < 6; i++)
        {
            await client.PostFormAsync("/Booking", token,
                BookingFields(date, "14:00:00", "32123466", null, null, $"Wrong-password-{i}!"));
        }

        var response = await client.PostFormAsync("/Booking", token,
            BookingFields(date, "14:00:00", "32123466", null, null, StrongPassword));

        Assert.DoesNotContain("er bekræftet", await response.BodyAsync());
    }

    // ---------- Login and admin ----------

    private static async Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password, string? returnUrl = null)
    {
        var url = returnUrl == null ? "/Account/Login" : $"/Account/Login?returnUrl={Uri.EscapeDataString(returnUrl)}";
        var token = await client.GetTokenAsync(url);
        return await client.PostFormAsync(url, token, ("Email", email), ("Password", password));
    }

    [Fact(DisplayName = "Login with unknown credentials shows a generic error")]
    public async Task Login_with_bad_credentials_is_generic()
    {
        var response = await LoginAsync(_factory.CreateBrowser(), "nobody@example.com", "Wrong-password-1");
        Assert.Contains("Forkert e-mail eller adgangskode.", await response.BodyAsync());
    }

    [Fact(DisplayName = "Login validates the email format and required fields")]
    public async Task Login_validates_input()
    {
        var client = _factory.CreateBrowser();
        var token = await client.GetTokenAsync("/Account/Login");

        var bad = await client.PostFormAsync("/Account/Login", token, ("Email", "notanemail"), ("Password", "x"));
        Assert.Contains("Indtast en gyldig e-mail.", await bad.BodyAsync());

        var empty = await client.PostFormAsync("/Account/Login", token);
        Assert.Contains("Indtast din e-mail.", await empty.BodyAsync());
    }

    [Fact(DisplayName = "Login is refused without an antiforgery token")]
    public async Task Login_requires_antiforgery_token()
    {
        var response = await _factory.CreateBrowser().PostAsync("/Account/Login",
            new FormUrlEncodedContent(HttpExtensions.Form(("Email", "a@b.dk"), ("Password", "x"))));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact(DisplayName = "The administrator signs in and lands on the schedule")]
    public async Task Admin_login_redirects_to_schedule()
    {
        await _factory.EnsureAdminAsync();
        var client = _factory.CreateBrowser();

        var response = await LoginAsync(client, AppFactory.AdminEmail, AppFactory.AdminPassword);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Admin/Schedule", response.Headers.Location!.OriginalString);
    }

    [Fact(DisplayName = "Login never redirects to an external returnUrl")]
    public async Task Login_ignores_external_return_url()
    {
        await _factory.EnsureAdminAsync();

        var response = await LoginAsync(_factory.CreateBrowser(), AppFactory.AdminEmail, AppFactory.AdminPassword, "https://evil.example/");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.False(response.Headers.Location!.IsAbsoluteUri, $"Redirected to {response.Headers.Location}");
    }

    [Fact(DisplayName = "Repeated failed logins lock the account")]
    public async Task Login_locks_out_after_repeated_failures()
    {
        await RegisterCustomerAsync("32123444", "laas@example.com");

        var client = _factory.CreateBrowser();
        for (var i = 0; i < 6; i++)
        {
            await LoginAsync(client, "laas@example.com", $"Wrong-password-{i}!");
        }

        var response = await LoginAsync(client, "laas@example.com", StrongPassword);
        Assert.NotEqual(HttpStatusCode.Redirect, response.StatusCode);
    }

    [Fact(DisplayName = "Admin pages work for the administrator and are refused for customers")]
    public async Task Admin_pages_respect_roles()
    {
        await _factory.EnsureAdminAsync();
        await RegisterCustomerAsync("32123455", "kunde-roller@example.com");

        var admin = _factory.CreateBrowser();
        await LoginAsync(admin, AppFactory.AdminEmail, AppFactory.AdminPassword);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/Admin/Schedule")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/Barbers")).StatusCode);

        var customer = _factory.CreateBrowser();
        await LoginAsync(customer, "kunde-roller@example.com", StrongPassword);
        Assert.Equal(HttpStatusCode.Redirect, (await customer.GetAsync("/Admin/Schedule")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await customer.GetAsync("/Account/MyBookings")).StatusCode);
    }

    [Fact(DisplayName = "The admin schedule survives extreme query dates")]
    public async Task Admin_schedule_handles_odd_dates()
    {
        await _factory.EnsureAdminAsync();
        var admin = _factory.CreateBrowser();
        await LoginAsync(admin, AppFactory.AdminEmail, AppFactory.AdminPassword);

        var response = await admin.GetAsync("/Admin/Schedule?date=9999-12-31&month=9999-12-01");

        Assert.True((int)response.StatusCode < 500, $"Got HTTP {(int)response.StatusCode}.");
    }
}
