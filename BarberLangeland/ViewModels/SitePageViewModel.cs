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

        /// <summary>Open or closed right now (shop-local time), for the dot and text in the hero.</summary>
        public ShopStatus? Status { get; set; }

        /// <summary>The hero line, for example "Åbent nu – lukker kl. 17:00" or "Lukket nu – åbner i morgen kl. 09:30".</summary>
        public string StatusText { get; set; } = string.Empty;
    }
}
