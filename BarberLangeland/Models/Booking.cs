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

        // The customer's name as given at booking time. Captured here rather than read from
        // the account because a first-time customer books without ever creating a login.
        // Named to match what it actually holds; it used to be called "Description", which
        // read as an unused free-text field and invited its own deletion.
        public string? CustomerName { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public bool IsConfirmed { get; set; }

        public bool IsCancelled { get; set; }

        public bool IsNoShow { get; set; }

        // Identity bruger
        public string UserId { get; set; } = string.Empty;

        public ApplicationUser User { get; set; } = null!;
    }
}
