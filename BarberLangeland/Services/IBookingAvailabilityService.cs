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
        ///
        /// <paramref name="excludeBookingId"/> leaves a single booking out of the occupancy
        /// calculation. Admin rescheduling needs this, otherwise a booking blocks its own
        /// current slot and cannot be saved back to where it already is.
        /// </summary>
        Task<List<BookingDayViewModel>> GetAvailableDaysAsync(
            int barberId,
            int serviceId,
            DateTime startDate,
            int dayCount = 2,
            int? excludeBookingId = null);

        /// <summary>
        /// Returns the opening hours for a full week so the UI can display the same
        /// schedule the slot generator uses.
        /// </summary>
        IReadOnlyList<OpeningHoursViewModel> GetOpeningHours();

        /// <summary>
        /// Whether the shop is open at <paramref name="localNow"/> (shop-local time), using the same
        /// opening hours as the slot generator. The opening time is inclusive and the closing time
        /// exclusive, so at exactly 17:00 the shop counts as closed.
        /// </summary>
        ShopStatus GetShopStatus(DateTime localNow);

        /// <summary>Minutes the shop is open on the given weekday (0 when closed all day).</summary>
        int GetOpenMinutes(DayOfWeek day);
    }
}
