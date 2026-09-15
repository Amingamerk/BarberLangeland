namespace BarberLangeland.Models
{
    public class Barber
    {
        public int Id { get; set; }

        public required string Name { get; set; }

        public required string Title { get; set; }

        public string ImagePath { get; set; } = string.Empty; // Image path, for example, "/images/xxx.jpg"

    }
}
