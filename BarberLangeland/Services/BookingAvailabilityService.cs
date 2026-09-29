using System.Globalization;
using BarberLangeland.Data;
using BarberLangeland.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace BarberLangeland.Services
{
    public class BookingAvailabilityService : IBookingAvailabilityService
    {
        private static readonly IReadOnlyDictionary<DayOfWeek, (TimeSpan Open, TimeSpan Close)> OpeningHours =
            new Dictionary<DayOfWeek, (TimeSpan Open, TimeSpan Close)>
            {
                [DayOfWeek.Monday] = (new TimeSpan(9, 30, 0), new TimeSpan(17, 0, 0)),
                [DayOfWeek.Tuesday] = (new TimeSpan(9, 30, 0), new TimeSpan(17, 0, 0)),
                [DayOfWeek.Wednesday] = (new TimeSpan(9, 30, 0), new TimeSpan(17, 0, 0)),
                [DayOfWeek.Thursday] = (new TimeSpan(9, 30, 0), new TimeSpan(17, 0, 0)),
                [DayOfWeek.Friday] = (new TimeSpan(9, 30, 0), new TimeSpan(17, 0, 0)),
                [DayOfWeek.Saturday] = (new TimeSpan(9, 30, 0), new TimeSpan(13, 0, 0))
            };

        private readonly ApplicationDbContext _context;
        private readonly TimeProvider _clock;

        public BookingAvailabilityService(ApplicationDbContext context, TimeProvider clock)
        {
            _context = context;
            _clock = clock;
        }

        public IReadOnlyList<OpeningHoursViewModel> GetOpeningHours()
        {
            return new[]
            {
                OpeningHoursFor(DayOfWeek.Monday, "Man"),
                OpeningHoursFor(DayOfWeek.Tuesday, "Tir"),
                OpeningHoursFor(DayOfWeek.Wednesday, "Ons"),
                OpeningHoursFor(DayOfWeek.Thursday, "Tor"),
                OpeningHoursFor(DayOfWeek.Friday, "Fre"),
                OpeningHoursFor(DayOfWeek.Saturday, "Lør"),
                OpeningHoursFor(DayOfWeek.Sunday, "Søn")
            };
        }

        public int GetOpenMinutes(DayOfWeek day)
        {
            return OpeningHours.TryGetValue(day, out var hours) ? (int)(hours.Close - hours.Open).TotalMinutes : 0;
        }

        public ShopStatus GetShopStatus(DateTime localNow)
        {
            var today = localNow.Date;

            if (OpeningHours.TryGetValue(today.DayOfWeek, out var hours)
                && localNow >= today.Add(hours.Open)
                && localNow < today.Add(hours.Close))
            {
                return new ShopStatus(true, today.Add(hours.Close));
            }

            // Closed: find the next opening, today if it has not opened yet, else the next open day.
            for (var offset = 0; offset <= 7; offset++)
            {
                var day = today.AddDays(offset);
                if (OpeningHours.TryGetValue(day.DayOfWeek, out var dayHours)
                    && day.Add(dayHours.Open) > localNow)
                {
                    return new ShopStatus(false, day.Add(dayHours.Open));
                }
            }

            return new ShopStatus(false, today.AddDays(7));
        }

        private static OpeningHoursViewModel OpeningHoursFor(DayOfWeek day, string label)
        {
            if (!OpeningHours.TryGetValue(day, out var hours))
            {
                return new OpeningHoursViewModel { Day = label, Hours = "Lukket", IsClosed = true };
            }

            return new OpeningHoursViewModel
            {
                Day = label,
                Hours = $"{hours.Open:hh\\:mm}–{hours.Close:hh\\:mm}"
            };
        }

        public async Task<List<BookingDayViewModel>> GetAvailableDaysAsync(
            int barberId,
            int serviceId,
            DateTime startDate,
            int dayCount = 2,
            int? excludeBookingId = null)
        {
            if (dayCount < 1)
            {
                dayCount = 1;
            }

            var durationMinutes = await _context.Services
                .Where(service => service.Id == serviceId)
                .Select(service => service.DurationMinutes)
                .FirstOrDefaultAsync();

            var firstDate = startDate.Date;

            // Near DateTime.MaxValue the date arithmetic below would overflow; nothing is bookable there.
            if (firstDate > DateTime.MaxValue.Date.AddDays(-(dayCount + 2)) || firstDate < DateTime.MinValue.AddDays(2))
            {
                return new List<BookingDayViewModel>();
            }

            var lastDate = firstDate.AddDays(dayCount - 1);

            // Load every booking that could touch the requested window in a single round
            // trip, then resolve each day in memory — the precise overlap test needs
            // TimeSpan arithmetic, which cannot be translated to SQL.
            var rawRows = await _context.Bookings
                .Where(booking => booking.BarberId == barberId
                    && !booking.IsCancelled
                    && booking.BookingTime < lastDate.Date.AddDays(1)
                    && booking.BookingTime > firstDate.AddDays(-1)
                    && (excludeBookingId == null || booking.Id != excludeBookingId))
                .Select(booking => new
                {
                    BookingTime = booking.BookingTime,
                    Duration = booking.Duration
                })
                .ToListAsync();

            var rawBookings = rawRows
                .Select(booking => (booking.BookingTime, booking.Duration))
                .ToList();

            var days = new List<BookingDayViewModel>(dayCount);
            for (var offset = 0; offset < dayCount; offset++)
            {
                var date = firstDate.AddDays(offset);
                days.Add(new BookingDayViewModel
                {
                    Date = date,
                    // The client fetches a window covering a whole month, so a
                    // Today/Tomorrow label would be wrong for every day after the first.
                    Label = date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
                    IsToday = offset == 0,
                    Slots = BuildSlots(rawBookings, date, durationMinutes, _clock.LocalNow())
                });
            }

            return days;
        }

        private static List<TimeSlotViewModel> BuildSlots(
            IReadOnlyList<(DateTime BookingTime, TimeSpan Duration)> bookings,
            DateTime date,
            int durationMinutes,
            DateTime now)
        {
            if (!OpeningHours.TryGetValue(date.DayOfWeek, out var hours) || durationMinutes <= 0)
            {
                return [];
            }

            var dayStart = date.Date.Add(hours.Open);
            var dayEnd = date.Date.Add(hours.Close);

            var sameDay = bookings
                .Where(booking => booking.BookingTime + booking.Duration > dayStart
                    && booking.BookingTime < dayEnd)
                .ToList();

            var duration = TimeSpan.FromMinutes(durationMinutes);
            var slots = new List<TimeSlotViewModel>();
            for (var candidate = dayStart; candidate + duration <= dayEnd; candidate = candidate.AddMinutes(30))
            {
                var overlaps = sameDay.Any(booking =>
                    candidate < booking.BookingTime + booking.Duration
                    && candidate + duration > booking.BookingTime);

                if (!overlaps && (date.Date > now.Date || candidate > now))
                {
                    slots.Add(new TimeSlotViewModel { Time = candidate.TimeOfDay });
                }
            }

            return slots;
        }
    }
}
