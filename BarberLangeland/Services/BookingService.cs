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
            await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);

            var barber = await _context.Barbers.FindAsync(booking.BarberId);
            if (barber == null)
            {
                return null;
            }

            var overlaps = await _context.Bookings.AnyAsync(b => b.BarberId == booking.BarberId
                && booking.BookingTime < b.BookingTime + b.Duration
                && booking.BookingTime + booking.Duration > b.BookingTime);

            if (overlaps)
            {
                return null;
            }

            _context.Bookings.Add(booking);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
            return booking;
        }
    }
}
