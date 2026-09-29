using BarberLangeland.Data;
using BarberLangeland.Models;
using BarberLangeland.Services;
using BarberLangeland.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace BarberLangeland.Controllers
{
    public class BookingController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IBookingAvailabilityService _availabilityService;
        private readonly IBookingService _bookingService;
        private readonly ICustomerIdentityService _customers;
        private readonly TimeProvider _clock;

        public BookingController(
            ApplicationDbContext context,
            IBookingAvailabilityService availabilityService,
            IBookingService bookingService,
            ICustomerIdentityService customers,
            TimeProvider clock)
        {
            _clock = clock;
            _context = context;
            _availabilityService = availabilityService;
            _bookingService = bookingService;
            _customers = customers;
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
        [EnableRateLimiting("customer-lookup")]
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

            var email = _customers.NormalizeEmail(model.Email);

            if (string.IsNullOrWhiteSpace(model.Email))
            {
                ModelState.AddModelError(nameof(model.Email), "Indtast din e-mail.");
            }
            else if (email.Length == 0)
            {
                ModelState.AddModelError(nameof(model.Email), "Indtast en gyldig e-mail.");
            }

            var emailIsKnown = false;

            if (ModelState.IsValid && email.Length > 0)
            {
                // Email-first: a returning customer only needs the password. A new customer is
                // asked for name, phone number (contact detail for the barber) and a password,
                // so those are validated here rather than up front.
                var outcome = await _customers.CheckEmailAsync(email);

                if (outcome == CustomerLookupOutcome.KnownEmail)
                {
                    emailIsKnown = true;

                    if (string.IsNullOrWhiteSpace(model.Password))
                    {
                        ModelState.AddModelError(nameof(model.Password), "Indtast din adgangskode.");
                    }
                }
                else if (outcome == CustomerLookupOutcome.UnknownEmail)
                {
                    if (string.IsNullOrWhiteSpace(model.Name))
                    {
                        ModelState.AddModelError(nameof(model.Name), "Indtast dit navn.");
                    }

                    if (string.IsNullOrWhiteSpace(model.Phone))
                    {
                        ModelState.AddModelError(nameof(model.Phone), "Indtast dit telefonnummer.");
                    }
                    else if (_customers.NormalizePhone(model.Phone).Length == 0)
                    {
                        ModelState.AddModelError(nameof(model.Phone), "Indtast et gyldigt telefonnummer.");
                    }

                    if (string.IsNullOrWhiteSpace(model.Password))
                    {
                        ModelState.AddModelError(nameof(model.Password), "Vælg en adgangskode.");
                    }
                }
            }

            // Keep the revealed state consistent on redisplay, otherwise a failed POST would
            // collapse back to the email-only view and silently discard what the user typed.
            model.EmailChecked = email.Length > 0;
            model.IsKnownEmail = emailIsKnown;
            if (email.Length > 0)
            {
                model.Email = email;
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

            var userIdResult = await ResolveUserAsync(model, emailIsKnown);
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
                // The views then fall back to "Ukendt navn" and show the account's phone number.
                CustomerName = model.Name?.Trim(),
                // Bookings are confirmed as soon as they are placed; admin no longer has to
                // approve each one. Admin can still cancel or mark a no-show afterwards.
                IsConfirmed = true,
                CreatedAt = _clock.LocalNow(),
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
            model.ConfirmedBarberName = barber.Name;
            model.ConfirmedServiceName = service.Name;
            model.ConfirmedDurationMinutes = service.DurationMinutes;
            model.ConfirmedPrice = service.Price;
            model.ConfirmedDate = createdBooking.BookingTime.Date;
            model.ConfirmedTime = createdBooking.BookingTime.TimeOfDay;
            ModelState.Clear();
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("customer-lookup")]
        public async Task<IActionResult> CheckEmail([FromBody] CheckEmailRequest request)
        {
            var outcome = await _customers.CheckEmailAsync(request?.Email);

            if (outcome == CustomerLookupOutcome.InvalidEmail)
            {
                return BadRequest(new { message = "Indtast en gyldig e-mail." });
            }

            // The email-first form has to tell the browser whether to ask for a password or for
            // name/phone/password, so this does reveal whether an email is registered. It returns
            // nothing else about the account, and the customer-lookup rate limit keeps bulk
            // enumeration impractical.
            return Json(new { exists = outcome == CustomerLookupOutcome.KnownEmail });
        }

        private async Task<(ApplicationUser? User, string? Error)> ResolveUserAsync(BookingViewModel model, bool emailIsKnown)
        {
            if (emailIsKnown)
            {
                var signedIn = await _customers.SignInAsync(model.Email, model.Password);

                if (!signedIn)
                {
                    ModelState.AddModelError(nameof(model.Password), "E-mail eller adgangskode er forkert, eller kontoen er midlertidigt låst efter for mange forsøg.");
                    return (null, "password");
                }
            }
            else
            {
                var registerResult = await _customers.RegisterAsync(
                    model.Email,
                    model.Name,
                    model.Phone,
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

            var signedInUser = await _customers.FindByEmailAsync(model.Email);

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
            if (barberId <= 0 || serviceId <= 0 || bookingDate.Date < _clock.LocalToday())
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

            var today = _clock.LocalToday();
            if (model.BookingDate.Date < today)
            {
                model.BookingDate = today;
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

    /// <summary>Body of the step 4 email lookup.</summary>
    public class CheckEmailRequest
    {
        public string? Email { get; set; }
    }
}
