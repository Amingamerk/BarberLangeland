namespace BarberLangeland.Models
{
    public class Booking
    {
        public int Id { get; set; }

        public DateTime BookingTime { get; set; }

        public TimeSpan Duration { get; set; }

        public int BarberId { get; set; }
        public required Barber Barber { get; set; }

        public int ServiceId { get; set; }
        public required Service Service { get; set; }

        // The customer's name as given when booking (a first-time customer has no account name yet).
        public string? CustomerName { get; set; }

        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// The service's price when the booking was made, so a later price change does not
        /// rewrite what an existing booking cost.
        /// </summary>
        public decimal Price { get; set; }

        public bool IsConfirmed { get; set; }

        public bool IsCancelled { get; set; }

        public bool IsNoShow { get; set; }

        // Identity bruger
        public string UserId { get; set; } = string.Empty;

        public ApplicationUser User { get; set; } = null!;
    }
}
