using System.Net;
using System.Text.RegularExpressions;
using BarberLangeland.Data;
using BarberLangeland.Models;
using BarberLangeland.Tests.Support;
using BarberLangeland.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BarberLangeland.Tests;

public class PublicPagesTests : IClassFixture<AppFactory>
{
    private readonly AppFactory _factory;

    public PublicPagesTests(AppFactory factory) => _factory = factory;

    private async Task<string> GetAsync(string url)
    {
        var response = await _factory.CreateBrowser().GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.BodyAsync();
    }

    [Theory(DisplayName = "Public pages declare Danish, have a description and a Danish title")]
    [InlineData("/", "Forside - Frisør Langeland")]
    [InlineData("/Home/About", "Om os - Frisør Langeland")]
    [InlineData("/Home/Prices", "Priser - Frisør Langeland")]
    public async Task Pages_have_language_description_and_title(string url, string title)
    {
        var html = await GetAsync(url);

        Assert.Contains("<html lang=\"da\">", html);
        Assert.Matches("<meta name=\"description\" content=\"[^\"]{40,}\"", html);
        Assert.Contains($"<title>{title}</title>", html);
    }

    [Theory(DisplayName = "Each public page has exactly one h1")]
    [InlineData("/")]
    [InlineData("/Home/About")]
    [InlineData("/Home/Prices")]
    public async Task Pages_have_a_single_h1(string url)
    {
        var html = await GetAsync(url);
        Assert.Single(Regex.Matches(html, "<h1[ >]"));
    }

    [Fact(DisplayName = "Prices page shows the services and prices from the database")]
    public async Task Prices_come_from_the_database()
    {
        var html = await GetAsync("/Home/Prices");

        Assert.Contains("Herreklip", html);
        Assert.Contains("250 kr.", html);
        Assert.Contains("180 kr.", html);
        Assert.Contains("120 kr.", html);
        Assert.Contains("30 minutter", html);
    }

    [Fact(DisplayName = "Changing a price in the database changes the Prices page and the front page")]
    public async Task Price_changes_show_up_without_editing_views()
    {
        await SetPriceAsync(3, 135m);
        try
        {
            Assert.Contains("135 kr.", await GetAsync("/Home/Prices"));
            Assert.Contains("135 kr.", await GetAsync("/"));
        }
        finally
        {
            await SetPriceAsync(3, 120m);
        }
    }

    private async Task SetPriceAsync(int serviceId, decimal price)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var service = await db.Services.FindAsync(serviceId);
        service!.Price = price;
        await db.SaveChangesAsync();
    }

    [Theory(DisplayName = "Every service card links to the booking page with that service preselected")]
    [InlineData("/")]
    [InlineData("/Home/Prices")]
    public async Task Service_cards_link_to_booking_with_the_service(string url)
    {
        var html = await GetAsync(url);

        foreach (var id in new[] { 1, 2, 3 })
        {
            Assert.Contains($"/Booking?serviceId={id}", html);
        }
    }

    [Theory(DisplayName = "The booking page preselects the service from the link")]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Booking_page_preselects_service(int serviceId)
    {
        var html = await GetAsync($"/Booking?serviceId={serviceId}");
        Assert.Matches($"value=\"{serviceId}\"[^>]*checked", html);
    }

    [Theory(DisplayName = "The Find os section shows address, phone, email and opening hours from one source")]
    [InlineData("/")]
    [InlineData("/Home/About")]
    [InlineData("/Home/Prices")]
    public async Task Find_us_section_is_present(string url)
    {
        var html = await GetAsync(url);

        Assert.Contains("id=\"kontakt\"", html);
        Assert.Contains(SalonInfo.AddressLine1, html);
        Assert.Contains($"href=\"tel:{SalonInfo.PhoneHref}\"", html);
        Assert.Contains($"href=\"mailto:{SalonInfo.Email}\"", html);
        Assert.Contains("09:30–17:00", html);
        Assert.Contains("Lukket", html);
    }

    [Fact(DisplayName = "The footer uses the same contact details as the rest of the site")]
    public async Task Footer_uses_salon_info()
    {
        var html = await GetAsync("/Home/About");
        var footer = html[html.IndexOf("<footer", StringComparison.Ordinal)..];

        Assert.Contains($"mailto:{SalonInfo.Email}", footer);
        Assert.Contains($"tel:{SalonInfo.PhoneHref}", footer);
        Assert.DoesNotContain("info@frisorlangeland.dk", html);
    }

    [Fact(DisplayName = "The Kontakt link in the navigation goes to the front page section on every page")]
    public async Task Kontakt_link_points_to_the_section()
    {
        var html = await GetAsync("/Home/Prices");

        Assert.Matches("href=\"/#kontakt\"[^>]*>Kontakt", html);
    }

    [Fact(DisplayName = "The front page hero shows today's opening hours and a call button")]
    public async Task Home_hero_has_today_and_call_button()
    {
        var html = await GetAsync("/");

        Assert.Contains("hero-today", html);
        Assert.Contains($"href=\"tel:{SalonInfo.PhoneHref}\" class=\"btn btn-outline-book\"", html);
    }

    [Fact(DisplayName = "The 4 MB hero video is not part of the page load (it is fetched by script on wide screens)")]
    public async Task Hero_video_is_lazy()
    {
        var html = await GetAsync("/");
        var video = Regex.Match(html, "<video[^>]*>").Value;

        Assert.DoesNotContain(" src=", video);
        Assert.Contains("data-src=", video);
        Assert.Contains("preload=\"none\"", video);
    }

    [Fact(DisplayName = "The About page lists the barbers from the database with a booking link")]
    public async Task About_lists_barbers()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Barbers.Add(new Barber { Name = "Testperson Hansen", Title = "Frisør", ImagePath = "" });
            await db.SaveChangesAsync();
        }

        var html = await GetAsync("/Home/About");

        Assert.Contains("Testperson Hansen", html);
        Assert.Contains("Book hos Testperson", html);
        Assert.Contains("Bashar Al-Haj", html);
    }

    [Fact(DisplayName = "Content the owner still has to write is shown as clearly marked placeholders")]
    public async Task Placeholders_are_clearly_marked()
    {
        var about = await GetAsync("/Home/About");
        var prices = await GetAsync("/Home/Prices");

        Assert.Contains("[Udfyld: vores historie]", about);
        Assert.Contains("[Udfyld: billedgalleri]", about);
        Assert.Contains("[Udfyld: FAQ]", prices);
    }

    [Fact(DisplayName = "The reviews section has a fallback message for when the widget does not load")]
    public async Task Reviews_have_a_fallback()
    {
        var html = await GetAsync("/");
        Assert.Contains("id=\"reviews-fallback\"", html);
    }
}
