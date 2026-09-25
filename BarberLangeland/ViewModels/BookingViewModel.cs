using BarberLangeland.Models;

namespace BarberLangeland.ViewModels
{
    public class BookingViewModel
    {
        public int BarberId { get; set; }

        public int ServiceId { get; set; }

        public DateTime BookingDate { get; set; } = DateTime.Today;

        public TimeSpan? BookingTime { get; set; }

        public string Name { get; set; } = string.Empty;

        public string Email { get; set; } = string.Empty;

        public string Phone { get; set; } = string.Empty;

        public string Password { get; set; } = string.Empty;

        public List<BarberOptionViewModel> Barbers { get; set; } = [];

        public List<ServiceOptionViewModel> Services { get; set; } = [];

        public List<BookingDayViewModel> Days { get; set; } = [];

        public bool BookingConfirmed { get; set; }

        public string? ConfirmationMessage { get; set; }
    }

    public class BarberOptionViewModel
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;

        public string ImagePath { get; set; } = string.Empty;
    }

    public class ServiceOptionViewModel
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public int DurationMinutes { get; set; }

        public decimal Price { get; set; }
    }

    public class BookingDayViewModel
    {
        public DateTime Date { get; set; }

        public string Label { get; set; } = string.Empty;

        public bool IsToday { get; set; }

        public List<TimeSlotViewModel> Slots { get; set; } = [];
    }

    public class TimeSlotViewModel
    {
        public TimeSpan Time { get; set; }

        public string Label => Time.ToString(@"hh\:mm");
    }
}
