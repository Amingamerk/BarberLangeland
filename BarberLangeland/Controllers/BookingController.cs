using BarberLangeland.Data;
using BarberLangeland.Models;
using BarberLangeland.Services;
using BarberLangeland.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BarberLangeland.Controllers
{
    public class BookingController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IBookingAvailabilityService _availabilityService;
        private readonly IBookingService _bookingService;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;

        public BookingController(
            ApplicationDbContext context,
            IBookingAvailabilityService availabilityService,
            IBookingService bookingService,
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager)
        {
            _context = context;
            _availabilityService = availabilityService;
            _bookingService = bookingService;
            _userManager = userManager;
            _signInManager = signInManager;
        }

        [HttpGet]
        public async Task<IActionResult> Index(int? barberId, int? serviceId)
        {
            var model = new BookingViewModel
            {
                BarberId = barberId ?? 0,
                ServiceId = serviceId ?? 0
            };

            await PopulateOptionsAsync(model);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Index(BookingViewModel model)
        {
            await PopulateOptionsAsync(model);

            var barber = await _context.Barbers.FindAsync(model.BarberId);
            var service = await _context.Services.FindAsync(model.ServiceId);

            if (barber == null)
            {
                ModelState.AddModelError(nameof(model.BarberId), "Vælg en frisør.");
            }

            if (service == null)
            {
                ModelState.AddModelError(nameof(model.ServiceId), "Vælg en behandling.");
            }

            if (model.BookingTime == null)
            {
                ModelState.AddModelError(nameof(model.BookingTime), "Vælg et ledigt tidspunkt.");
            }

            if (string.IsNullOrWhiteSpace(model.Name))
            {
                ModelState.AddModelError(nameof(model.Name), "Indtast dit navn.");
            }

            if (string.IsNullOrWhiteSpace(model.Email))
            {
                ModelState.AddModelError(nameof(model.Email), "Indtast din e-mail.");
            }

            if (string.IsNullOrWhiteSpace(model.Phone))
            {
                ModelState.AddModelError(nameof(model.Phone), "Indtast dit telefonnummer.");
            }

            if (string.IsNullOrWhiteSpace(model.Password))
            {
                ModelState.AddModelError(nameof(model.Password), "Indtast din adgangskode.");
            }

            if (!ModelState.IsValid || barber == null || service == null || model.BookingTime == null)
            {
                return View(model);
            }

            var availableDays = await _availabilityService.GetAvailableDaysAsync(
                barber.Id,
                service.Id,
                model.BookingDate);
            var selectedSlotIsAvailable = availableDays
                .SelectMany(day => day.Slots.Select(slot => new { day.Date, slot.Time }))
                .Any(slot => slot.Date.Date == model.BookingDate.Date && slot.Time == model.BookingTime.Value);

            if (!selectedSlotIsAvailable)
            {
                ModelState.AddModelError(nameof(model.BookingTime), "Det valgte tidspunkt er ikke længere ledigt.");
                return View(model);
            }

            var phone = model.Phone.Trim();
            var user = await _userManager.Users.FirstOrDefaultAsync(candidate => candidate.PhoneNumber == phone);
            if (user != null)
            {
                var signInResult = await _signInManager.PasswordSignInAsync(
                    user.UserName ?? phone,
                    model.Password,
                    isPersistent: false,
                    lockoutOnFailure: false);

                if (!signInResult.Succeeded)
                {
                    ModelState.AddModelError(nameof(model.Password), "Telefonnummer eller adgangskode er forkert.");
                    return View(model);
                }
            }
            else
            {
                user = new ApplicationUser
                {
                    UserName = phone,
                    PhoneNumber = phone,
                    Email = model.Email.Trim()
                };

                var createResult = await _userManager.CreateAsync(user, model.Password);
                if (!createResult.Succeeded)
                {
                    foreach (var error in createResult.Errors)
                    {
                        ModelState.AddModelError(string.Empty, error.Description);
                    }

                    return View(model);
                }

                await _signInManager.SignInAsync(user, isPersistent: false);
            }

            var booking = new Booking
            {
                BookingTime = model.BookingDate.Date.Add(model.BookingTime.Value),
                Duration = TimeSpan.FromMinutes(service.DurationMinutes),
                BarberId = barber.Id,
                Barber = barber,
                ServiceId = service.Id,
                Service = service,
                Description = model.Name.Trim(),
                UserId = user.Id,
                User = user
            };

            var createdBooking = await _bookingService.CreateBookingAsync(booking);
            if (createdBooking == null)
            {
                ModelState.AddModelError(string.Empty, "Det valgte tidspunkt blev taget. Vælg venligst et andet tidspunkt.");
                return View(model);
            }

            model.BookingConfirmed = true;
            model.ConfirmationMessage = $"Din tid hos {barber.Name} er bekræftet.";
            ModelState.Clear();
            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> Availability(int barberId, int serviceId, DateTime bookingDate)
        {
            if (barberId <= 0 || serviceId <= 0 || bookingDate.Date < DateTime.Today)
            {
                return BadRequest();
            }

            var days = await _availabilityService.GetAvailableDaysAsync(barberId, serviceId, bookingDate);
            return Json(days.Select(day => new
            {
                date = day.Date.ToString("yyyy-MM-dd"),
                label = day.Label,
                slots = day.Slots.Select(slot => new
                {
                    time = slot.Time.ToString(),
                    label = slot.Label
                })
            }));
        }

        private async Task PopulateOptionsAsync(BookingViewModel model)
        {
            model.Barbers = await _context.Barbers
                .AsNoTracking()
                .Select(barber => new BarberOptionViewModel
                {
                    Id = barber.Id,
                    Name = barber.Name,
                    Title = barber.Title,
                    ImagePath = barber.ImagePath
                })
                .ToListAsync();

            model.Services = await _context.Services
                .AsNoTracking()
                .OrderBy(service => service.Id)
                .Select(service => new ServiceOptionViewModel
                {
                    Id = service.Id,
                    Name = service.Name,
                    Description = service.Description,
                    DurationMinutes = service.DurationMinutes,
                    Price = service.Price
                })
                .ToListAsync();

            if (model.BookingDate.Date < DateTime.Today)
            {
                model.BookingDate = DateTime.Today;
            }

            if (model.BarberId != 0 && model.ServiceId != 0)
            {
                model.Days = await _availabilityService.GetAvailableDaysAsync(
                    model.BarberId,
                    model.ServiceId,
                    model.BookingDate);
            }
        }
    }
}
