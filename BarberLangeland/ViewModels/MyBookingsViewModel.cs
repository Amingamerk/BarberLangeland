using System.ComponentModel.DataAnnotations;
using BarberLangeland.Models;

namespace BarberLangeland.ViewModels
{
    /// <summary>Read-only list of the signed-in customer's own bookings.</summary>
    public class MyBookingsViewModel
    {
        public List<Booking> Upcoming { get; set; } = [];

        public List<Booking> Past { get; set; } = [];
    }

    /// <summary>Email + password sign-in. Sign-in is by email, not by username.</summary>
    public class LoginViewModel
    {
        [Required(ErrorMessage = "Indtast din e-mail.")]
        [EmailAddress(ErrorMessage = "Indtast en gyldig e-mail.")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Indtast din adgangskode.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [Display(Name = "Husk mig")]
        public bool RememberMe { get; set; }
    }
}
