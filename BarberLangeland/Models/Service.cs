namespace BarberLangeland.Models
{
    public class Service
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public int DurationMinutes { get; set; }

        public decimal Price { get; set; }

        /// <summary>
        /// Hidden services are not offered for booking and not shown on the public pages, but
        /// existing bookings that use them keep working. Services are hidden, never deleted.
        /// </summary>
        public bool IsActive { get; set; } = true;

        /// <summary>Position in the lists on the booking page, prices and front page; lowest first.</summary>
        public int SortOrder { get; set; }

    }
}
