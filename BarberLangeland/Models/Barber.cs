using System.ComponentModel.DataAnnotations;

namespace BarberLangeland.Models
{
    public class Barber
    {
        public int Id { get; set; }

        public required string Name { get; set; }

        public required string Title { get; set; }

        // Optional: the admin views fall back to initials when no photo is set. Declared
        // nullable on purpose — a non-nullable string is implicitly treated as required by
        // model binding, and model binding converts an empty form field to null, so the
        // photo could never be left blank. The column stays NOT NULL in the database;
        // BarbersController normalises null to string.Empty before saving.
        public string? ImagePath { get; set; } // Image path, for example, "/images/xxx.jpg"

    }
}
