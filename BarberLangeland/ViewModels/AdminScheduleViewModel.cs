using BarberLangeland.Models;

namespace BarberLangeland.ViewModels
{
    /// <summary>One day's bookings, grouped by barber.</summary>
    public class AdminScheduleViewModel
    {
        public DateTime Date { get; set; }

        /// <summary>First day of the month the calendar is showing.</summary>
        public DateTime Month { get; set; }

        public List<Barber> Barbers { get; set; } = [];

        /// <summary>Services the edit modal offers, so admin can change the treatment too.</summary>
        public List<Service> Services { get; set; } = [];

        public List<BarberScheduleGroup> Groups { get; set; } = [];

        /// <summary>Every day in <see cref="Month"/> that has at least one booking, so the
        /// calendar can mark busy days without a query per day.</summary>
        public Dictionary<DateTime, AdminDayCount> DayCounts { get; set; } = [];

        /// <summary>Every booking in <see cref="Month"/>, for the month-at-a-glance list.</summary>
        public List<Booking> MonthBookings { get; set; } = [];

        /// <summary>Month bookings bucketed by day, for the month list's headings.</summary>
        public IEnumerable<IGrouping<DateTime, Booking>> MonthDays =>
            MonthBookings.GroupBy(b => b.BookingTime.Date).OrderBy(g => g.Key);
    }

    /// <summary>Arguments for the shared booking table partial.</summary>
    public class AdminBookingTableViewModel
    {
        /// <summary>Rows to render, already narrowed to the subset being listed.</summary>
        public List<Booking> Bookings { get; set; } = [];

        /// <summary>Shown when there is nothing to list.</summary>
        public string EmptyMessage { get; set; } = "Ingen bookinger.";

        /// <summary>The day a status change should return the admin to.</summary>
        public DateTime ReturnDate { get; set; }

        /// <summary>First day of the month, echoed back so the redirect keeps the month open.</summary>
        public DateTime ReturnMonth { get; set; }
    }

    /// <summary>How busy one day is, used to mark the calendar cell.</summary>
    public class AdminDayCount
    {
        /// <summary>All bookings that day, cancelled ones included.</summary>
        public int Total { get; set; }

        /// <summary>Bookings that are still live appointments (not cancelled, not a no-show).</summary>
        public int Active { get; set; }

        /// <summary>Bookings the admin has cancelled.</summary>
        public int Cancelled { get; set; }
    }

    public class BarberScheduleGroup
    {
        public required Barber Barber { get; set; }

        public List<Booking> Bookings { get; set; } = [];
    }
}
