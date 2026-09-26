using BarberLangeland.ViewModels;

namespace BarberLangeland.Services
{
    public interface IBookingAvailabilityService
    {
        /// <summary>
        /// Returns availability for a window of <paramref name="dayCount"/> days starting at
        /// <paramref name="startDate"/>. The default of 2 keeps today/tomorrow behaviour for
        /// existing callers; the booking calendar requests a full 42-day window so every
        /// visible cell of the month grid has real data behind it.
        /// </summary>
        Task<List<BookingDayViewModel>> GetAvailableDaysAsync(
            int barberId,
            int serviceId,
            DateTime startDate,
            int dayCount = 2);

        /// <summary>
        /// Returns the opening hours for a full week so the UI can display the same
        /// schedule the slot generator uses.
        /// </summary>
        IReadOnlyList<OpeningHoursViewModel> GetOpeningHours();
    }
}
