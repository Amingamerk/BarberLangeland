using BarberLangeland.Data;
using BarberLangeland.Models;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace BarberLangeland.Services
{
    public class BookingService : IBookingService
    {
        private readonly ApplicationDbContext _context;

        public BookingService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<Booking?> CreateBookingAsync(Booking booking)
        {
            // Program.cs enables EnableRetryOnFailure, and the retrying execution strategy
            // refuses user-initiated transactions unless the whole unit of work runs inside
            // strategy.ExecuteAsync. Without this wrapper BeginTransactionAsync throws
            // InvalidOperationException and every booking fails.
            var strategy = _context.Database.CreateExecutionStrategy();

            return await strategy.ExecuteAsync<Booking?>(async () =>
            {
                await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);

                var barber = await _context.Barbers.FindAsync(booking.BarberId);
                if (barber == null)
                {
                    return null;
                }

                var start = booking.BookingTime;
                var end = booking.BookingTime + booking.Duration;

                // Duration is a SQL Server "time" column, so BookingTime + Duration cannot be
                // translated server-side. Narrow with an index-friendly window in SQL, then apply
                // the precise overlap test in memory where TimeSpan arithmetic works.
                var candidates = await _context.Bookings
                    .Where(b => b.BarberId == booking.BarberId
                        && b.BookingTime < end
                        && b.BookingTime > start.AddDays(-1))
                    .Select(b => new { b.BookingTime, b.Duration })
                    .ToListAsync();

                if (candidates.Any(b => b.BookingTime + b.Duration > start))
                {
                    return null;
                }

                _context.Bookings.Add(booking);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
                return booking;
            });
        }
    }
}
