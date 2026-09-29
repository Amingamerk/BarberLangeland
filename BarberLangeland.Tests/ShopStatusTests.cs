using System.Net;
using BarberLangeland.Services;
using BarberLangeland.Tests.Support;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace BarberLangeland.Tests;

public class ShopStatusServiceTests
{
    // 2026-10-05 is a Monday.
    private static BarberLangeland.ViewModels.ShopStatus StatusAt(DateTime local)
    {
        using var db = new TestDb();
        using var context = db.CreateContext();
        return new BookingAvailabilityService(context, new CopenhagenTimeProvider()).GetShopStatus(local);
    }

    [Theory(DisplayName = "The shop is open from the opening time until, but not including, the closing time")]
    [InlineData("2026-10-05 09:30", true)]
    [InlineData("2026-10-05 12:00", true)]
    [InlineData("2026-10-05 16:59", true)]
    [InlineData("2026-10-05 09:29", false)]
    [InlineData("2026-10-05 17:00", false)]
    [InlineData("2026-10-05 23:30", false)]
    [InlineData("2026-10-05 03:00", false)]
    public void Weekday_hours(string local, bool open)
    {
        Assert.Equal(open, StatusAt(DateTime.Parse(local)).IsOpen);
    }

    [Theory(DisplayName = "Saturday closes at 13:00 and Sunday is closed all day")]
    [InlineData("2026-10-10 09:30", true)]
    [InlineData("2026-10-10 12:59", true)]
    [InlineData("2026-10-10 13:00", false)]
    [InlineData("2026-10-11 12:00", false)]
    public void Weekend_hours(string local, bool open)
    {
        Assert.Equal(open, StatusAt(DateTime.Parse(local)).IsOpen);
    }

    [Fact(DisplayName = "While open, the next change is today's closing time")]
    public void Open_next_change_is_closing()
    {
        Assert.Equal(DateTime.Parse("2026-10-05 17:00"), StatusAt(DateTime.Parse("2026-10-05 11:11")).NextChange);
        Assert.Equal(DateTime.Parse("2026-10-10 13:00"), StatusAt(DateTime.Parse("2026-10-10 10:00")).NextChange);
    }

    [Theory(DisplayName = "While closed, the next change is the next opening time")]
    [InlineData("2026-10-05 08:00", "2026-10-05 09:30")]  // before opening, same day
    [InlineData("2026-10-05 18:26", "2026-10-06 09:30")]  // after closing, next day
    [InlineData("2026-10-09 17:00", "2026-10-10 09:30")]  // Friday evening -> Saturday
    [InlineData("2026-10-10 13:00", "2026-10-12 09:30")]  // Saturday afternoon -> Monday
    [InlineData("2026-10-11 12:00", "2026-10-12 09:30")]  // Sunday -> Monday
    public void Closed_next_change_is_next_opening(string now, string expected)
    {
        var status = StatusAt(DateTime.Parse(now));

        Assert.False(status.IsOpen);
        Assert.Equal(DateTime.Parse(expected), status.NextChange);
    }
}

public class ShopStatusWebTests : IClassFixture<AppFactory>
{
    private readonly AppFactory _factory;

    public ShopStatusWebTests(AppFactory factory) => _factory = factory;

    private async Task<string> HeroAt(TestClock clock)
    {
        using var factory = _factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
        }));
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        var response = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.BodyAsync();
        var start = html.IndexOf("hero-today", StringComparison.Ordinal);
        return html.Substring(start, html.IndexOf("</p>", start, StringComparison.Ordinal) - start);
    }

    // October 2026 is on summer time: Copenhagen is UTC+2.
    [Fact(DisplayName = "During opening hours the dot is green and the text says open until closing time")]
    public async Task Open_shows_green_dot()
    {
        var hero = await HeroAt(TestClock.AtUtc(2026, 10, 6, 10, 0)); // Tuesday 12:00 in Copenhagen

        Assert.Contains("data-shop-status=\"open\"", hero);
        Assert.DoesNotContain("is-closed", hero);
        Assert.Contains("Åbent nu – lukker kl. 17:00", hero);
    }

    [Fact(DisplayName = "After closing time the dot is red and the text says when the shop opens tomorrow")]
    public async Task Closed_after_hours_shows_red_dot()
    {
        var hero = await HeroAt(TestClock.AtUtc(2026, 10, 6, 16, 26)); // Tuesday 18:26 in Copenhagen

        Assert.Contains("data-shop-status=\"closed\"", hero);
        Assert.Contains("is-closed", hero);
        Assert.Contains("Lukket nu – åbner i morgen kl. 09:30", hero);
    }

    [Fact(DisplayName = "Before opening time the dot is red and the text gives today's opening time")]
    public async Task Closed_before_opening_shows_red_dot()
    {
        var hero = await HeroAt(TestClock.AtUtc(2026, 10, 6, 6, 0)); // Tuesday 08:00 in Copenhagen

        Assert.Contains("is-closed", hero);
        Assert.Contains("Lukket nu – åbner kl. 09:30", hero);
    }

    [Fact(DisplayName = "On Saturday after closing the text names Monday as the next opening")]
    public async Task Saturday_afternoon_names_monday()
    {
        var hero = await HeroAt(TestClock.AtUtc(2026, 10, 10, 12, 0)); // Saturday 14:00 in Copenhagen

        Assert.Contains("is-closed", hero);
        Assert.Contains("Lukket nu – åbner mandag kl. 09:30", hero);
    }

    [Fact(DisplayName = "On Sunday the dot is red")]
    public async Task Sunday_shows_red_dot()
    {
        var hero = await HeroAt(TestClock.AtUtc(2026, 10, 11, 10, 0)); // Sunday 12:00 in Copenhagen

        Assert.Contains("is-closed", hero);
        Assert.Contains("Lukket nu", hero);
    }

    [Fact(DisplayName = "The status follows Copenhagen time, not the server's UTC clock")]
    public async Task Status_uses_copenhagen_time()
    {
        // 15:30 UTC is 17:30 in Copenhagen: closed, although it would still be open in UTC.
        var hero = await HeroAt(TestClock.AtUtc(2026, 10, 6, 15, 30));

        Assert.Contains("is-closed", hero);
    }
}
