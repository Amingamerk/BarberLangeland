using System.ComponentModel.DataAnnotations;

namespace BarberLangeland.Models
{
    public class Barber
    {
        public int Id { get; set; }

        public required string Name { get; set; }

        public required string Title { get; set; }

        // Optional; views fall back to initials. Nullable so model binding accepts an empty field, and
        // BarbersController stores null as an empty string.
        public string? ImagePath { get; set; } // Image path, for example, "/images/xxx.jpg"

    }
}
