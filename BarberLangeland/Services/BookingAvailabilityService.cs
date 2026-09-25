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

        public BookingAvailabilityService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<List<BookingDayViewModel>> GetAvailableDaysAsync(
            int barberId,
            int serviceId,
            DateTime startDate)
        {
            var durationMinutes = await _context.Services
                .Where(service => service.Id == serviceId)
                .Select(service => service.DurationMinutes)
                .FirstOrDefaultAsync();

            var days = new List<BookingDayViewModel>();
            for (var offset = 0; offset < 2; offset++)
            {
                var date = startDate.Date.AddDays(offset);
                days.Add(new BookingDayViewModel
                {
                    Date = date,
                    Label = offset == 0 ? "Today" : "Tomorrow",
                    IsToday = offset == 0,
                    Slots = await GetAvailableSlotsAsync(barberId, date, durationMinutes)
                });
            }

            return days;
        }

        private async Task<List<TimeSlotViewModel>> GetAvailableSlotsAsync(
            int barberId,
            DateTime date,
            int durationMinutes)
        {
            if (!OpeningHours.TryGetValue(date.DayOfWeek, out var hours) || durationMinutes <= 0)
            {
                return [];
            }

            var dayStart = date.Date.Add(hours.Open);
            var dayEnd = date.Date.Add(hours.Close);
            var bookings = await _context.Bookings
            .Where(booking => booking.BarberId == barberId
             && booking.BookingTime < dayEnd
             && booking.BookingTime > dayStart.AddDays(-1)) // cheap safety margin, still SQL-side
             .Select(booking => new
             {
                 booking.BookingTime,
                 booking.Duration
             })
             .ToListAsync();

            // Finish the precise overlap filter in memory, where TimeSpan arithmetic just works
            bookings = bookings
                .Where(b => b.BookingTime + b.Duration > dayStart)
                .ToList();

            var duration = TimeSpan.FromMinutes(durationMinutes);
            var slots = new List<TimeSlotViewModel>();
            for (var candidate = dayStart; candidate + duration <= dayEnd; candidate = candidate.AddMinutes(30))
            {
                var overlaps = bookings.Any(booking =>
                    candidate < booking.BookingTime + booking.Duration
                    && candidate + duration > booking.BookingTime);

                if (!overlaps && (date.Date > DateTime.Today || candidate > DateTime.Now))
                {
                    slots.Add(new TimeSlotViewModel { Time = candidate.TimeOfDay });
                }
            }

            return slots;
        }
    }
}
