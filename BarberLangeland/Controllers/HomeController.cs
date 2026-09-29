using BarberLangeland.Data;
using BarberLangeland.Models;
using BarberLangeland.Services;
using BarberLangeland.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.Globalization;

namespace BarberLangeland.Controllers
{
    public class HomeController : Controller
    {
        private static readonly CultureInfo Danish = new("da-DK");

        private readonly ApplicationDbContext _context;
        private readonly IBookingAvailabilityService _availability;
        private readonly TimeProvider _clock;

        public HomeController(ApplicationDbContext context, IBookingAvailabilityService availability, TimeProvider clock)
        {
            _context = context;
            _availability = availability;
            _clock = clock;
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

            // Everything here is in shop-local (Copenhagen) time; the server clock is UTC on Azure.
            var now = _clock.LocalNow();
            var todayIndex = ((int)now.DayOfWeek + 6) % 7; // GetOpeningHours lists Monday first
            var status = _availability.GetShopStatus(now);

            return new SitePageViewModel
            {
                Services = await _context.Services.AsNoTracking()
                    .Where(s => s.IsActive)
                    .OrderBy(s => s.SortOrder).ThenBy(s => s.Id)
                    .ToListAsync(),
                Barbers = includeBarbers
                    ? await _context.Barbers.AsNoTracking().OrderBy(b => b.Name).ToListAsync()
                    : [],
                OpeningHours = hours,
                Today = todayIndex < hours.Count ? hours[todayIndex] : null,
                Status = status,
                StatusText = StatusText(status, now)
            };
        }

        /// <summary>The one-line open/closed message shown in the hero.</summary>
        internal static string StatusText(ShopStatus status, DateTime now)
        {
            var time = status.NextChange.ToString("HH:mm", CultureInfo.InvariantCulture);

            if (status.IsOpen)
            {
                return $"Åbent nu – lukker kl. {time}";
            }

            var days = (status.NextChange.Date - now.Date).Days;
            var when = days switch
            {
                0 => $"åbner kl. {time}",
                1 => $"åbner i morgen kl. {time}",
                _ => $"åbner {status.NextChange.ToString("dddd", Danish)} kl. {time}"
            };

            return $"Lukket nu – {when}";
        }
    }
}
