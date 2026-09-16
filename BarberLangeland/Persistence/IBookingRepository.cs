using BarberLangeland.Models;

namespace BarberLangeland.Persistence
{
    public interface IBookingRepository
    {
        Booking? GetById(int id);

        List<Booking> GetAll();

        List<Booking> GetByUserId(string userId);

        List<Booking> GetByBarberId(int barberId);

        List<Booking> GetByDate(DateOnly date);

        List<Booking> GetUpcomingBookings();

        List<Booking> GetUpcomingBookingsForBarber(int barberId);

        List<Booking> GetBookingsForDay(int barberId, DateOnly date);

        Booking? FindOverlappingBooking(
            int barberId,
            DateTime startTime,
            DateTime endTime,
            int? excludedBookingId = null);

        void Add(Booking booking);

        void Update(Booking booking);

        void Delete(int id);

    }
}
