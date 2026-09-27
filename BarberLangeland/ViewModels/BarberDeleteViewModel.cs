using BarberLangeland.Models;

namespace BarberLangeland.ViewModels
{
    /// <summary>
    /// Backs the admin delete-confirmation page. Booking is part of the model so the page can
    /// warn up front, and CheckCancellation lets the same check run again on POST — the foreign
    /// key from Booking to Barber is Restrict, so deleting a barber who has bookings would
    /// otherwise surface as a raw database exception.
    /// </summary>
    public class BarberDeleteViewModel
    {
        public required Barber Barber { get; set; }

        public int BookingCount { get; set; }

        public bool CanDelete => BookingCount == 0;
    }
}
