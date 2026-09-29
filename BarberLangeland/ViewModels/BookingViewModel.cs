using BarberLangeland.Models;

namespace BarberLangeland.ViewModels
{
    public class BookingViewModel
    {
        public int BarberId { get; set; }

        public int ServiceId { get; set; }

        public DateTime BookingDate { get; set; }

        public TimeSpan? BookingTime { get; set; }

        // Nullable on purpose. MVC validates non-nullable reference types as implicitly
        // [Required] before the action runs, with an English message. The controller validates
        // each of these itself, with Danish messages, and only asks for what applies: a returning
        // customer needs the email and password, a new one also name and phone number.
        public string? Email { get; set; }

        public string? Name { get; set; }

        /// <summary>Contact detail for the barber; required for a new customer, not used for lookup.</summary>
        public string? Phone { get; set; }

        public string? Password { get; set; }

        /// <summary>
        /// True once the email has been checked against existing accounts, which is what gates
        /// the name/phone/password fields in step 4.
        /// </summary>
        public bool EmailChecked { get; set; }

        /// <summary>
        /// Only meaningful once <see cref="EmailChecked"/> is true: whether the checked email
        /// belongs to an existing account (sign-in) or a new one (registration).
        /// </summary>
        public bool IsKnownEmail { get; set; }

        public List<BarberOptionViewModel> Barbers { get; set; } = [];

        public List<ServiceOptionViewModel> Services { get; set; } = [];

        public List<BookingDayViewModel> Days { get; set; } = [];

        public List<OpeningHoursViewModel> OpeningHours { get; set; } = [];

        public bool BookingConfirmed { get; set; }

        /// <summary>The time is held, but counts only once the email in the customer's inbox is confirmed.</summary>
        public bool EmailVerificationPending { get; set; }

        /// <summary>Whether the confirmation mail was actually handed to the mail server.</summary>
        public bool EmailVerificationSent { get; set; }

        public string? ConfirmedEmail { get; set; }

        public string? ConfirmationMessage { get; set; }

        // Resolved on the success path so the confirmation screen can show exactly what
        // was booked without re-querying the database or re-formatting in the view.
        // Only meaningful when BookingConfirmed is true.
        public string ConfirmedBarberName { get; set; } = string.Empty;

        public string ConfirmedServiceName { get; set; } = string.Empty;

        public int ConfirmedDurationMinutes { get; set; }

        public decimal ConfirmedPrice { get; set; }

        public DateTime ConfirmedDate { get; set; }

        public TimeSpan ConfirmedTime { get; set; }
    }

    public class BarberOptionViewModel
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;

        // Nullable to match Barber.ImagePath: a barber may legitimately have no photo,
        // and the selector already falls back to the salon logo for a blank path.
        public string? ImagePath { get; set; }
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

        // TimeSpan has no 24-hour specifier ("HH" throws), and "hh" on a DateTime is the
        // 12-hour clock. Formatting the hour as an integer keeps this unambiguous either way.
        public string Label => $"{(int)Time.TotalHours:00}:{Time.Minutes:00}";
    }
}
