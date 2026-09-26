using BarberLangeland.Models;

namespace BarberLangeland.ViewModels
{
    /// <summary>One day's bookings, grouped by barber.</summary>
    public class AdminScheduleViewModel
    {
        public DateTime Date { get; set; } = DateTime.Today;

        public List<Barber> Barbers { get; set; } = [];

        public List<BarberScheduleGroup> Groups { get; set; } = [];
    }

    public class BarberScheduleGroup
    {
        public required Barber Barber { get; set; }

        public List<Booking> Bookings { get; set; } = [];
    }
}
