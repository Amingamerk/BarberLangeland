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
        private readonly IBookingAvailabilityService _availabilityService;

        public AdminController(ApplicationDbContext context, IBookingAvailabilityService availabilityService)
        {
            _context = context;
            _availabilityService = availabilityService;
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

            model.Services = await _context.Services
                .OrderBy(s => s.Name)
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
            // Bookings are confirmed on creation, so there is no separate confirm action and no
            // pending state to resolve here.
            switch (action)
            {
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
                    // Restoring returns the booking to the state it had when created: an active,
                    // confirmed appointment. Leaving it unconfirmed would strand it, since the
                    // manual confirm action no longer exists. A no-show that turns out to have
                    // been a no-show in name only correctly comes back as confirmed too.
                    booking.IsConfirmed = true;
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

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditBooking(
            int id,
            DateTime date,
            int barberId,
            int serviceId,
            TimeSpan time,
            string? returnDate)
        {
            var booking = await _context.Bookings.FindAsync(id);
            if (booking == null)
            {
                return NotFound();
            }

            var barber = await _context.Barbers.FindAsync(barberId);
            var service = await _context.Services.FindAsync(serviceId);
            if (barber == null || service == null)
            {
                return BadRequest();
            }

            var newStart = date.Date.Add(time);
            var newDuration = TimeSpan.FromMinutes(service.DurationMinutes);

            // Reuse the booking flow's own availability rules rather than duplicating the
            // opening-hours and overlap logic here. The booking being moved is excluded so it
            // does not occupy its own current slot.
            var availableDays = await _availabilityService.GetAvailableDaysAsync(
                barber.Id,
                service.Id,
                newStart.Date,
                dayCount: 1,
                excludeBookingId: booking.Id);

            var slotIsAvailable = availableDays
                .SelectMany(day => day.Slots.Select(slot => slot.Time))
                .Any(slot => slot == time);

            if (!slotIsAvailable)
            {
                TempData["AdminScheduleError"] =
                    "Tidspunktet er ikke ledigt for den valgte frisør og behandling.";
                return RedirectToAction(nameof(Schedule), new { date = returnDate ?? date.ToString("yyyy-MM-dd") });
            }

            booking.BookingTime = newStart;
            booking.BarberId = barber.Id;
            booking.ServiceId = service.Id;
            // The stored duration must follow the new service, otherwise the booking keeps
            // occupying the old treatment's worth of time.
            booking.Duration = newDuration;

            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Schedule), new { date = returnDate ?? date.ToString("yyyy-MM-dd") });
        }
    }
}
