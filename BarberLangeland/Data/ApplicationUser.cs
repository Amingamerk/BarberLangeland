using Microsoft.AspNetCore.Identity;

namespace BarberLangeland.Models
{
    public class ApplicationUser : IdentityUser
    {
        /// <summary>
        /// When the account was created (shop-local time). An account whose email is still
        /// unconfirmed 24 hours after this is removed together with its bookings.
        /// </summary>
        public DateTime RegisteredAt { get; set; }

        public ICollection<Booking> Bookings { get; set; }
            = new List<Booking>();
    }
}