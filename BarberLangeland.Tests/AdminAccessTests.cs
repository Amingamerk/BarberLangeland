using System.Net;
using BarberLangeland.Services;
using BarberLangeland.Tests.Support;
using Xunit;

namespace BarberLangeland.Tests;

public class AdminAccessPolicyTests
{
    private static bool Allowed(string? list, string address) => AdminAccessPolicy.IsAllowed(list, IPAddress.Parse(address));

    [Fact(DisplayName = "A listed address is allowed and an unlisted one is not")]
    public void Exact_address_match()
    {
        Assert.True(Allowed("203.0.113.7", "203.0.113.7"));
        Assert.False(Allowed("203.0.113.7", "203.0.113.8"));
    }

    [Fact(DisplayName = "Addresses inside a CIDR range are allowed and outside it are not")]
    public void Cidr_range_match()
    {
        Assert.True(Allowed("198.51.100.0/24", "198.51.100.200"));
        Assert.False(Allowed("198.51.100.0/24", "198.51.101.1"));
    }

    [Fact(DisplayName = "IPv6 addresses and ranges are supported")]
    public void Ipv6_match()
    {
        Assert.True(Allowed("2001:db8::/48", "2001:db8:0:1::5"));
        Assert.True(Allowed("2001:db8::1", "2001:db8::1"));
        Assert.False(Allowed("2001:db8::/48", "2001:db9::1"));
    }

    [Fact(DisplayName = "An IPv4 address seen as IPv4-mapped IPv6 still matches")]
    public void Ipv4_mapped_address_matches()
    {
        Assert.True(AdminAccessPolicy.IsAllowed("203.0.113.7", IPAddress.Parse("::ffff:203.0.113.7")));
        Assert.True(AdminAccessPolicy.IsAllowed("203.0.113.0/24", IPAddress.Parse("::ffff:203.0.113.7")));
    }

    [Theory(DisplayName = "Entries may be separated by commas, semicolons, spaces or new lines")]
    [InlineData("1.1.1.1,203.0.113.7")]
    [InlineData("1.1.1.1; 203.0.113.7")]
    [InlineData("  1.1.1.1   203.0.113.7  ")]
    [InlineData("1.1.1.1\n203.0.113.7")]
    public void Separators(string list) => Assert.True(Allowed(list, "203.0.113.7"));

    [Theory(DisplayName = "An empty or missing list allows nobody")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_list_fails_closed(string? list) => Assert.False(Allowed(list, "203.0.113.7"));

    [Fact(DisplayName = "A malformed entry is ignored and does not affect the valid ones")]
    public void Malformed_entries_are_ignored()
    {
        Assert.True(Allowed("not-an-ip,999.1.1.1,10.0.0.0/99,203.0.113.7", "203.0.113.7"));
        Assert.False(Allowed("not-an-ip,999.1.1.1,10.0.0.0/99", "203.0.113.7"));
    }

    [Fact(DisplayName = "An unknown client address is never allowed")]
    public void Unknown_address_is_refused() => Assert.False(AdminAccessPolicy.IsAllowed("0.0.0.0/0", null));
}

public class AdminAccessWebTests : IClassFixture<AppFactory>
{
    private const string OutsideIp = "203.0.113.9";
    private readonly AppFactory _factory;

    public AdminAccessWebTests(AppFactory factory) => _factory = factory;

    private async Task<HttpClient> SignedInAdminAsync(string ip)
    {
        await _factory.EnsureAdminAsync();
        var client = _factory.CreateBrowser(ip);
        var token = await client.GetTokenAsync("/Account/Login");
        var response = await client.PostFormAsync("/Account/Login", token,
            ("Email", AppFactory.AdminEmail), ("Password", AppFactory.AdminPassword));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return client;
    }

    [Theory(DisplayName = "An allowed address reaches the admin pages")]
    [InlineData("/Admin/Schedule")]
    [InlineData("/Barbers")]
    public async Task Allowed_address_gets_in(string url)
    {
        var client = await SignedInAdminAsync("10.9.9.9");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(url)).StatusCode);
    }

    [Theory(DisplayName = "Another address is refused with 403 even when signed in as the administrator")]
    [InlineData("/Admin/Schedule")]
    [InlineData("/Barbers")]
    [InlineData("/Barbers/Create")]
    public async Task Other_address_is_refused_even_for_the_admin(string url)
    {
        var client = await SignedInAdminAsync("10.9.9.10");
        client.DefaultRequestHeaders.Remove("X-Forwarded-For");
        client.DefaultRequestHeaders.Add("X-Forwarded-For", OutsideIp);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(url)).StatusCode);
    }

    private static void ActAs(HttpClient client, string forwardedFor)
    {
        client.DefaultRequestHeaders.Remove("X-Forwarded-For");
        client.DefaultRequestHeaders.Add("X-Forwarded-For", forwardedFor);
    }

    [Fact(DisplayName = "An anonymous visitor from another address never sees the admin area")]
    public async Task Anonymous_visitor_from_other_address_is_turned_away()
    {
        var response = await _factory.CreateBrowser(OutsideIp).GetAsync("/Admin/Schedule");

        // Authentication runs first for anonymous visitors, so they are sent to the (public) login page.
        Assert.True(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Redirect);
    }

    [Fact(DisplayName = "The refusal tells the administrator which address it saw")]
    public async Task Refusal_shows_the_detected_address()
    {
        var client = await SignedInAdminAsync("10.9.9.11");
        ActAs(client, OutsideIp);

        var response = await client.GetAsync("/Admin/Schedule");

        Assert.Contains(OutsideIp, await response.Content.ReadAsStringAsync());
    }

    [Fact(DisplayName = "A spoofed X-Forwarded-For value from the client cannot grant access")]
    public async Task Spoofed_forwarded_header_does_not_grant_access()
    {
        var client = await SignedInAdminAsync("10.9.9.12");
        // The front end appends the real address last; the app trusts only that last entry.
        ActAs(client, "10.1.2.3, " + OutsideIp);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/Admin/Schedule")).StatusCode);
    }

    [Theory(DisplayName = "Public pages and the login page stay open to every address")]
    [InlineData("/")]
    [InlineData("/Booking")]
    [InlineData("/Account/Login")]
    public async Task Public_pages_stay_open(string url)
    {
        var response = await _factory.CreateBrowser(OutsideIp).GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(DisplayName = "With no allowed addresses configured, the admin area is closed to everyone")]
    public async Task Empty_setting_closes_the_admin_area()
    {
        await _factory.EnsureAdminAsync();
        using var closed = _factory.WithWebHostBuilder(builder => builder.UseSetting("Admin:AllowedIps", ""));
        var client = closed.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
            BaseAddress = new Uri("https://localhost")
        });
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "10.7.7.7");
        var token = await client.GetTokenAsync("/Account/Login");
        await client.PostFormAsync("/Account/Login", token,
            ("Email", AppFactory.AdminEmail), ("Password", AppFactory.AdminPassword));

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/Admin/Schedule")).StatusCode);
    }
}
