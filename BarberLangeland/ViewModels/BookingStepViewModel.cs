namespace BarberLangeland.ViewModels
{
    public class BookingStepViewModel
    {
        public int Number { get; set; }

        public string Step { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;

        public string Subtitle { get; set; } = string.Empty;

        public string Status { get; set; } = string.Empty;

        public string StatusId => $"step-status-{Number}";

        public string PanelId => $"step-panel-{Number}";
    }
}
