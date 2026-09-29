using BarberLangeland.Data;
using BarberLangeland.Models;
using BarberLangeland.Services;
using BarberLangeland.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;

namespace BarberLangeland.Controllers
{
    public class HomeController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IBookingAvailabilityService _availability;

        public HomeController(ApplicationDbContext context, IBookingAvailabilityService availability)
        {
            _context = context;
            _availability = availability;
        }

        public async Task<IActionResult> Index()
        {
            return View(await BuildModelAsync(includeBarbers: false));
        }

        public async Task<IActionResult> About()
        {
            return View(await BuildModelAsync(includeBarbers: true));
        }

        public async Task<IActionResult> Prices()
        {
            return View(await BuildModelAsync(includeBarbers: false));
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }

        private async Task<SitePageViewModel> BuildModelAsync(bool includeBarbers)
        {
            var hours = _availability.GetOpeningHours();

            // The server clock is UTC on Azure; the shop's weekday is what matters here.
            var today = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, CopenhagenZone()).DayOfWeek;
            var todayIndex = ((int)today + 6) % 7; // GetOpeningHours lists Monday first

            return new SitePageViewModel
            {
                Services = await _context.Services.AsNoTracking().OrderBy(s => s.Id).ToListAsync(),
                Barbers = includeBarbers
                    ? await _context.Barbers.AsNoTracking().OrderBy(b => b.Name).ToListAsync()
                    : [],
                OpeningHours = hours,
                Today = todayIndex < hours.Count ? hours[todayIndex] : null
            };
        }

        private static TimeZoneInfo CopenhagenZone()
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById("Europe/Copenhagen");
            }
            catch (TimeZoneNotFoundException)
            {
                return TimeZoneInfo.FindSystemTimeZoneById("Romance Standard Time");
            }
        }
    }
}
