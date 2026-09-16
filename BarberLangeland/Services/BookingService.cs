using BarberLangeland.Data;
using BarberLangeland.Models;
using Microsoft.EntityFrameworkCore;

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
            var barber = await _context.Barbers.FindAsync(booking.BarberId);
            if (barber == null) { return null; }


            bool overlapper = await _context.Bookings.AnyAsync(b => b.BarberId == booking.BarberId
            && booking.BookingTime < b.BookingTime + b.Duration
            && booking.BookingTime + booking.Duration > b.BookingTime);

            if (overlapper) { return null; }


            _context.Bookings.Add(booking);
            await _context.SaveChangesAsync();
            return booking;
        }
    }
}
