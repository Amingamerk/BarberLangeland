using BarberLangeland.Data;
using BarberLangeland.Models;
using BarberLangeland.Services;
using BarberLangeland.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BarberLangeland.Controllers
{
    public class BookingController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IBookingAvailabilityService _availabilityService;
        private readonly IBookingService _bookingService;
        private readonly IPhoneIdentityService _phoneIdentity;

        public BookingController(
            ApplicationDbContext context,
            IBookingAvailabilityService availabilityService,
            IBookingService bookingService,
            IPhoneIdentityService phoneIdentity)
        {
            _context = context;
            _availabilityService = availabilityService;
            _bookingService = bookingService;
            _phoneIdentity = phoneIdentity;
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

            if (string.IsNullOrWhiteSpace(model.Phone))
            {
                ModelState.AddModelError(nameof(model.Phone), "Indtast dit telefonnummer.");
            }
            else if (string.IsNullOrWhiteSpace(_phoneIdentity.NormalizePhone(model.Phone)))
            {
                ModelState.AddModelError(nameof(model.Phone), "Indtast et gyldigt telefonnummer.");
            }

            var phoneIsKnown = false;

            if (ModelState.IsValid && !string.IsNullOrWhiteSpace(model.Phone))
            {
                // Phone-first: an unknown number needs registration details, a known one only
                // needs the password. Name and email are therefore validated here rather than
                // up front, so a returning customer is never asked for them.
                var outcome = await _phoneIdentity.CheckPhoneAsync(model.Phone);

                if (outcome == PhoneIdentityOutcome.InvalidNumber)
                {
                    ModelState.AddModelError(nameof(model.Phone), "Indtast et gyldigt telefonnummer.");
                }
                else if (outcome == PhoneIdentityOutcome.KnownNumber)
                {
                    phoneIsKnown = true;

                    if (string.IsNullOrWhiteSpace(model.Password))
                    {
                        ModelState.AddModelError(nameof(model.Password), "Indtast din adgangskode.");
                    }
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(model.Name))
                    {
                        ModelState.AddModelError(nameof(model.Name), "Indtast dit navn.");
                    }

                    if (string.IsNullOrWhiteSpace(model.Email))
                    {
                        ModelState.AddModelError(nameof(model.Email), "Indtast din e-mail.");
                    }

                    if (string.IsNullOrWhiteSpace(model.Password))
                    {
                        ModelState.AddModelError(nameof(model.Password), "Vælg en adgangskode.");
                    }
                }
            }

            // Keep the revealed state consistent on redisplay, otherwise a failed POST would
            // collapse back to the phone-only view and silently discard what the user typed.
            model.PhoneConfirmed = !string.IsNullOrWhiteSpace(_phoneIdentity.NormalizePhone(model.Phone));
            model.IsKnownPhone = phoneIsKnown;

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

            var userIdResult = await ResolveUserAsync(model, phoneIsKnown);
            if (userIdResult.Error != null)
            {
                return View(model);
            }

            var user = userIdResult.User!;

            var booking = new Booking
            {
                BookingTime = model.BookingDate.Date.Add(model.BookingTime.Value),
                Duration = TimeSpan.FromMinutes(service.DurationMinutes),
                BarberId = barber.Id,
                Barber = barber,
                ServiceId = service.Id,
                Service = service,
                // A returning customer never typed a name, so there is nothing to store here.
                // The views fall back to the phone number rather than misusing the email.
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

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CheckPhone([FromBody] CheckPhoneRequest request)
        {
            var outcome = await _phoneIdentity.CheckPhoneAsync(request?.Phone);

            if (outcome == PhoneIdentityOutcome.InvalidNumber)
            {
                return BadRequest(new { message = "Indtast et gyldigt telefonnummer." });
            }

            // Deliberately returns nothing about the account itself so this endpoint cannot
            // be used to discover which phone numbers are registered.
            return Json(new { exists = outcome == PhoneIdentityOutcome.KnownNumber });
        }

        private async Task<(ApplicationUser? User, string? Error)> ResolveUserAsync(BookingViewModel model, bool phoneIsKnown)
        {
            if (phoneIsKnown)
            {
                var signedIn = await _phoneIdentity.SignInWithPhoneAsync(model.Phone, model.Password);

                if (!signedIn)
                {
                    ModelState.AddModelError(nameof(model.Password), "Telefonnummer eller adgangskode er forkert.");
                    return (null, "password");
                }
            }
            else
            {
                var registerResult = await _phoneIdentity.RegisterWithPhoneAsync(
                    model.Phone,
                    model.Name,
                    model.Email,
                    model.Password);

                if (!registerResult.Succeeded)
                {
                    foreach (var error in registerResult.Errors)
                    {
                        ModelState.AddModelError(string.Empty, error.Description);
                    }

                    return (null, "register");
                }
            }

            var signedInUser = await _context.Users
                .Include(u => u.Bookings)
                .FirstOrDefaultAsync(u => u.PhoneNumber == _phoneIdentity.NormalizePhone(model.Phone));

            if (signedInUser == null)
            {
                ModelState.AddModelError(string.Empty, "Kunne ikke finde din konto.");
                return (null, "missing");
            }

            return (signedInUser, null);
        }

        [HttpGet]
        public async Task<IActionResult> Availability(int barberId, int serviceId, DateTime bookingDate, int days = 2)
        {
            if (barberId <= 0 || serviceId <= 0 || bookingDate.Date < DateTime.Today)
            {
                return BadRequest();
            }

            // The month calendar needs a window large enough to fill the whole visible
            // grid; cap it so a hand-crafted query cannot ask for an unbounded scan.
            days = Math.Clamp(days, 1, 62);

            var availability = await _availabilityService.GetAvailableDaysAsync(
                barberId,
                serviceId,
                bookingDate,
                days);

            return Json(availability.Select(day => new
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

            model.OpeningHours = _availabilityService.GetOpeningHours().ToList();

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

    /// <summary>Body of the step 4 phone lookup.</summary>
    public class CheckPhoneRequest
    {
        public string? Phone { get; set; }
    }
}
