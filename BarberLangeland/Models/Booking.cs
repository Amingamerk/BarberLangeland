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

        public string? Description { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public bool IsConfirmed { get; set; }

        // Identity bruger
        public string UserId { get; set; } = string.Empty;

        public ApplicationUser User { get; set; } = null!;
    }
}
