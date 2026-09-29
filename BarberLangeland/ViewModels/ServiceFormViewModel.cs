using System.ComponentModel.DataAnnotations;

namespace BarberLangeland.ViewModels
{
    /// <summary>Create/edit form for a treatment.</summary>
    public class ServiceFormViewModel
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Indtast et navn.")]
        [StringLength(60, ErrorMessage = "Navnet må højst være 60 tegn.")]
        public string? Name { get; set; }

        [StringLength(200, ErrorMessage = "Beskrivelsen må højst være 200 tegn.")]
        public string? Description { get; set; }

        [Range(5, 240, ErrorMessage = "Varigheden skal være mellem 5 og 240 minutter.")]
        public int DurationMinutes { get; set; } = 30;

        [Range(typeof(decimal), "0", "10000", ErrorMessage = "Prisen skal være mellem 0 og 10.000 kr.")]
        public decimal Price { get; set; }

        /// <summary>Minutes the shop is open on a weekday and on a Saturday, for the duration preview.</summary>
        public int WeekdayOpenMinutes { get; set; }

        public int SaturdayOpenMinutes { get; set; }
    }
}
