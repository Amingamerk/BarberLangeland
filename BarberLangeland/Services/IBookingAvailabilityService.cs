using BarberLangeland.ViewModels;

namespace BarberLangeland.Services
{
    public interface IBookingAvailabilityService
    {
        Task<List<BookingDayViewModel>> GetAvailableDaysAsync(int barberId, int serviceId, DateTime startDate);
    }
}
