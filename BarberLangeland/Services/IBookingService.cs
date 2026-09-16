using BarberLangeland.Models;

namespace BarberLangeland.Services
{
    public interface IBookingService
    {
        Task<Booking?> CreateBookingAsync(Booking booking);
    }
}
