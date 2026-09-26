namespace BarberLangeland.ViewModels
{
    public class BookingOptionCardViewModel
    {
        public string Name { get; set; } = string.Empty;

        public string InputId { get; set; } = string.Empty;

        public string Value { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;

        public string? Meta { get; set; }

        public string? Description { get; set; }

        public string? Badge { get; set; }

        public string? ImagePath { get; set; }

        public bool Checked { get; set; }

        public bool Disabled { get; set; }
    }
}
