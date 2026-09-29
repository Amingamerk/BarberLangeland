using BarberLangeland.Models;

namespace BarberLangeland.ViewModels
{
    /// <summary>What the public pages (Forside, Om os, Priser) show: live data instead of typed-in copies.</summary>
    public class SitePageViewModel
    {
        public List<Service> Services { get; set; } = [];

        public List<Barber> Barbers { get; set; } = [];

        public IReadOnlyList<OpeningHoursViewModel> OpeningHours { get; set; } = [];

        /// <summary>Today's opening hours in Copenhagen, for the "Åbent i dag" line.</summary>
        public OpeningHoursViewModel? Today { get; set; }
    }
}
