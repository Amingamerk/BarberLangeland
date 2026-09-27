using System.Data;
using System.Globalization;
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
        public async Task<IActionResult> Schedule(DateTime? date, DateTime? month)
        {
            var selectedDate = (date ?? DateTime.Today).Date;
            // Default the calendar to the month the selected day falls in, so the plain
            // ?date=... links that already exist keep working unchanged.
            var monthStart = new DateTime(
                (month ?? date ?? DateTime.Today).Year,
                (month ?? date ?? DateTime.Today).Month,
                1);
            var monthEnd = monthStart.AddMonths(1);

            var model = new AdminScheduleViewModel { Date = selectedDate, Month = monthStart };

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

            // Per-day counts for the whole month in a single round trip, rather than one
            // query per day. Grouping on BookingTime.Date is translated to
            // CAST(BookingTime AS date) and aggregated in SQL, so this stays cheap however
            // busy the month is. The range predicate on BookingTime itself is what lets the
            // provider use an index on it.
            var dayCounts = await _context.Bookings
                .Where(b => b.BookingTime >= monthStart && b.BookingTime < monthEnd)
                .GroupBy(b => b.BookingTime.Date)
                .Select(g => new
                {
                    Day = g.Key,
                    Total = g.Count(),
                    Cancelled = g.Count(b => b.IsCancelled),
                    NoShow = g.Count(b => b.IsNoShow)
                })
                .ToListAsync();

            foreach (var row in dayCounts)
            {
                model.DayCounts[row.Day] = new AdminDayCount
                {
                    Total = row.Total,
                    Cancelled = row.Cancelled,
                    Active = row.Total - row.Cancelled - row.NoShow
                };
            }

            // The month list needs the full rows, not just counts — a second single query
            // over the same range rather than re-reading per day.
            model.MonthBookings = await _context.Bookings
                .Include(b => b.Barber)
                .Include(b => b.Service)
                .Include(b => b.User)
                .Where(b => b.BookingTime >= monthStart && b.BookingTime < monthEnd)
                .OrderBy(b => b.BookingTime)
                .ToListAsync();

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateStatus(int id, string action, DateTime date, string? returnMonth)
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

            // returnMonth is only present when the action came from the month list; it keeps
            // the admin on the month they were scanning instead of jumping to the day view.
            return RedirectToAction(nameof(Schedule), MonthRoute(date, returnMonth));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditBooking(
            int id,
            DateTime date,
            int barberId,
            int serviceId,
            TimeSpan time,
            string? returnDate,
            string? returnMonth)
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

            // A cancelled or no-show booking is not an active appointment. Moving one would
            // silently relocate a slot that still renders as Aflyst, so require the admin to
            // use Gendan first — that restores the booking and makes it editable again.
            if (booking.IsCancelled || booking.IsNoShow)
            {
                TempData["AdminScheduleError"] =
                    "Bookingen er aflyst eller markeret som mødt ikke op. Gendan den først, hvis du vil ændre den.";
                return RedirectToAction(nameof(Schedule),
                    MonthRoute(ResolveRedirectDate(returnDate), returnMonth));
            }

            var newStart = date.Date.Add(time);
            var newDuration = TimeSpan.FromMinutes(service.DurationMinutes);

            var redirectDate = ResolveRedirectDate(returnDate);

            // The availability service never offers a slot that has already passed, so moving a
            // booking into the past fails the same check as a genuinely occupied slot. Check the
            // clock here so the two cases report different reasons — otherwise the admin reads
            // "ikke ledigt" and goes looking for a phantom conflict that does not exist. Today's
            // remaining slots are still offered, so the comparison is against the full timestamp.
            if (newStart <= DateTime.Now)
            {
                TempData["AdminScheduleError"] =
                    "Du kan ikke flytte en booking til et tidspunkt der er overstået.";
                return RedirectToAction(nameof(Schedule), MonthRoute(redirectDate, returnMonth));
            }

            // Serializable, mirroring BookingService.CreateBookingAsync: the availability check
            // and the write below form one check-then-write sequence, and without this two
            // concurrent edits (or a public booking landing mid-flight) can both observe the
            // slot as free and both commit, double-booking the barber.
            await using var transaction =
                await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);

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
                await transaction.RollbackAsync();
                return RedirectToAction(nameof(Schedule), MonthRoute(redirectDate, returnMonth));
            }

            booking.BookingTime = newStart;
            booking.BarberId = barber.Id;
            booking.ServiceId = service.Id;
            // The stored duration must follow the new service, otherwise the booking keeps
            // occupying the old treatment's worth of time.
            booking.Duration = newDuration;

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            return RedirectToAction(nameof(Schedule), MonthRoute(redirectDate, returnMonth));
        }

        // Both redirect helpers keep the admin where they were. date is the day being viewed
        // and month is the calendar page; when a form came from the month list it posts
        // returnMonth so the redirect does not drop back to the single-day view.
        private static Dictionary<string, object?> MonthRoute(DateTime date, string? returnMonth)
        {
            var route = new Dictionary<string, object?> { ["date"] = date.ToString("yyyy-MM-dd") };

            if (DateTime.TryParse(returnMonth, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var parsed))
            {
                route["month"] = new DateTime(parsed.Year, parsed.Month, 1)
                    .ToString("yyyy-MM-dd");
            }

            return route;
        }

        // returnDate comes back from the form as a free-form string. Fall back to the date
        // being edited to, then to today, rather than trusting the posted value.
        private static DateTime ResolveRedirectDate(string? returnDate)
        {
            return DateTime.TryParse(returnDate, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var parsed)
                ? parsed.Date
                : DateTime.Today;
        }
    }
}
