using BarberLangeland.Data;
using BarberLangeland.Models;
using BarberLangeland.Services;
using BarberLangeland.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BarberLangeland.Controllers
{
    /// <summary>
    /// Admin page for the treatments (name, description, duration, price). A treatment is hidden,
    /// never deleted, so bookings that already use it keep working.
    /// </summary>
    [Authorize(Roles = IdentitySeeder.AdminRole)]
    public class ServicesController : Controller
    {
        private const string ErrorKey = "ServiceError";

        private readonly ApplicationDbContext _context;
        private readonly IBookingAvailabilityService _availability;

        public ServicesController(ApplicationDbContext context, IBookingAvailabilityService availability)
        {
            _context = context;
            _availability = availability;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var services = await _context.Services
                .AsNoTracking()
                .OrderBy(s => s.SortOrder).ThenBy(s => s.Id)
                .ToListAsync();

            var bookingCounts = await _context.Bookings
                .GroupBy(b => b.ServiceId)
                .Select(g => new { ServiceId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.ServiceId, x => x.Count);

            ViewData["BookingCounts"] = bookingCounts;
            return View(services);
        }

        [HttpGet]
        public IActionResult Create()
        {
            return View("Form", WithOpeningMinutes(new ServiceFormViewModel()));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(ServiceFormViewModel model)
        {
            await ValidateAsync(model);

            if (!ModelState.IsValid)
            {
                return View("Form", WithOpeningMinutes(model));
            }

            var lastOrder = await _context.Services.MaxAsync(s => (int?)s.SortOrder) ?? 0;

            _context.Services.Add(new Service
            {
                Name = model.Name!.Trim(),
                Description = model.Description?.Trim() ?? string.Empty,
                DurationMinutes = model.DurationMinutes,
                Price = decimal.Round(model.Price, 2),
                IsActive = true,
                SortOrder = lastOrder + 1
            });
            await _context.SaveChangesAsync();

            TempData[ErrorKey + "Ok"] = $"{model.Name.Trim()} er oprettet.";
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var service = await _context.Services.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);
            if (service == null)
            {
                return NotFound();
            }

            return View("Form", WithOpeningMinutes(new ServiceFormViewModel
            {
                Id = service.Id,
                Name = service.Name,
                Description = service.Description,
                DurationMinutes = service.DurationMinutes,
                Price = service.Price
            }));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, ServiceFormViewModel model)
        {
            var service = await _context.Services.FindAsync(id);
            if (service == null)
            {
                return NotFound();
            }

            model.Id = id;
            await ValidateAsync(model);

            if (!ModelState.IsValid)
            {
                return View("Form", WithOpeningMinutes(model));
            }

            // Existing bookings keep their own duration and price; only new bookings see the change.
            service.Name = model.Name!.Trim();
            service.Description = model.Description?.Trim() ?? string.Empty;
            service.DurationMinutes = model.DurationMinutes;
            service.Price = decimal.Round(model.Price, 2);
            await _context.SaveChangesAsync();

            TempData[ErrorKey + "Ok"] = $"{service.Name} er gemt.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleActive(int id)
        {
            var service = await _context.Services.FindAsync(id);
            if (service == null)
            {
                return NotFound();
            }

            if (service.IsActive && !await _context.Services.AnyAsync(s => s.IsActive && s.Id != id))
            {
                TempData[ErrorKey] = "Mindst én behandling skal være synlig, ellers kan ingen booke en tid.";
                return RedirectToAction(nameof(Index));
            }

            service.IsActive = !service.IsActive;
            await _context.SaveChangesAsync();

            TempData[ErrorKey + "Ok"] = service.IsActive
                ? $"{service.Name} er synlig igen."
                : $"{service.Name} er skjult. Eksisterende bookinger er upåvirkede.";
            return RedirectToAction(nameof(Index));
        }

        /// <summary>Moves a treatment one step up or down in the lists on the public pages.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Move(int id, string direction)
        {
            if (direction is not ("up" or "down"))
            {
                return BadRequest();
            }

            var ordered = await _context.Services.OrderBy(s => s.SortOrder).ThenBy(s => s.Id).ToListAsync();
            var index = ordered.FindIndex(s => s.Id == id);
            if (index < 0)
            {
                return NotFound();
            }

            var target = direction == "up" ? index - 1 : index + 1;
            if (target >= 0 && target < ordered.Count)
            {
                (ordered[index], ordered[target]) = (ordered[target], ordered[index]);
            }

            // Renumber 1..n so the order never ends up with duplicates or gaps.
            for (var i = 0; i < ordered.Count; i++)
            {
                ordered[i].SortOrder = i + 1;
            }

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        private async Task ValidateAsync(ServiceFormViewModel model)
        {
            if (model.DurationMinutes % 5 != 0)
            {
                ModelState.AddModelError(nameof(model.DurationMinutes), "Varigheden skal være et helt antal 5 minutter.");
            }

            if (model.Price != decimal.Round(model.Price, 2))
            {
                ModelState.AddModelError(nameof(model.Price), "Prisen må højst have to decimaler.");
            }

            var name = model.Name?.Trim();
            if (!string.IsNullOrEmpty(name)
                && await _context.Services.AnyAsync(s => s.Id != model.Id && s.Name.ToLower() == name.ToLower()))
            {
                ModelState.AddModelError(nameof(model.Name), "Der findes allerede en behandling med det navn.");
            }
        }

        private ServiceFormViewModel WithOpeningMinutes(ServiceFormViewModel model)
        {
            model.WeekdayOpenMinutes = _availability.GetOpenMinutes(DayOfWeek.Monday);
            model.SaturdayOpenMinutes = _availability.GetOpenMinutes(DayOfWeek.Saturday);
            return model;
        }
    }
}
