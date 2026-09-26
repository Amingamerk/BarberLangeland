using BarberLangeland.Data;
using BarberLangeland.Models;
using BarberLangeland.Services;
using BarberLangeland.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BarberLangeland.Controllers
{
    /// <summary>Shop-side schedule. Restricted to members of the seeded Admin role.</summary>
    [Authorize(Roles = IdentitySeeder.AdminRole)]
    public class AdminController : Controller
    {
        private readonly ApplicationDbContext _context;

        public AdminController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> Schedule(DateTime? date)
        {
            var selectedDate = (date ?? DateTime.Today).Date;
            var model = new AdminScheduleViewModel { Date = selectedDate };

            // The barbers are the grouping axis, so load them all and let the view render
            // empty groups for a barber with nothing booked.
            model.Barbers = await _context.Barbers
                .OrderBy(b => b.Name)
                .ToListAsync();

            var bookings = await _context.Bookings
                .Include(b => b.Barber)
                .Include(b => b.Service)
                .Include(b => b.User)
                .Where(b => b.BookingTime.Date == selectedDate)
                .OrderBy(b => b.BookingTime)
                .ToListAsync();

            foreach (var barber in model.Barbers)
            {
                var group = new BarberScheduleGroup
                {
                    Barber = barber,
                    Bookings = bookings
                        .Where(b => b.BarberId == barber.Id)
                        .ToList()
                };

                model.Groups.Add(group);
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateStatus(int id, string action, DateTime date)
        {
            var booking = await _context.Bookings.FindAsync(id);

            if (booking == null)
            {
                return NotFound();
            }

            // Flags are mutually exclusive: a booking that is cancelled is no longer merely
            // unconfirmed, and no-show only makes sense for a booking that was kept.
            switch (action)
            {
                case "confirm":
                    booking.IsConfirmed = true;
                    booking.IsCancelled = false;
                    booking.IsNoShow = false;
                    break;
                case "cancel":
                    booking.IsConfirmed = false;
                    booking.IsCancelled = true;
                    booking.IsNoShow = false;
                    break;
                case "noshow":
                    booking.IsConfirmed = false;
                    booking.IsCancelled = false;
                    booking.IsNoShow = true;
                    break;
                case "reopen":
                    booking.IsConfirmed = false;
                    booking.IsCancelled = false;
                    booking.IsNoShow = false;
                    break;
                default:
                    // An unrecognised action must not silently leave the flags untouched and
                    // report success, so reject it instead of redirecting as though it worked.
                    return BadRequest();
            }

            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Schedule), new { date = date.Date });
        }
    }
}
