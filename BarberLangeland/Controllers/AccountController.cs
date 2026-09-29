using BarberLangeland.Data;
using BarberLangeland.Models;
using BarberLangeland.Services;
using BarberLangeland.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BarberLangeland.Controllers
{
    /// <summary>Signed-in customer pages. Sign-in itself is handled by the Identity UI.</summary>
    [Authorize]
    public class AccountController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly TimeProvider _clock;
        private readonly IEmailVerificationService _verification;
        private readonly SiteOptions _site;

        public AccountController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            TimeProvider clock,
            IEmailVerificationService verification,
            IOptions<SiteOptions> site)
        {
            _verification = verification;
            _site = site.Value;
            _context = context;
            _userManager = userManager;
            _signInManager = signInManager;
            _clock = clock;
        }

        /// <summary>
        /// Email + password sign-in. The stock Identity UI login posts the email straight into
        /// PasswordSignInAsync, which performs a username lookup - and usernames here are
        /// opaque "user_&lt;guid&gt;" values, so that lookup never matches. Looking the user up
        /// by email first is what makes the shop administrator able to sign in.
        /// </summary>
        [AllowAnonymous]
        [HttpGet]
        public async Task<IActionResult> Login(string? returnUrl = null)
        {
            // Reached while already signed in: a bookmark, a stale cached page, or the back
            // button. Showing the form again would be confusing and pointless, so send the
            // user to wherever a fresh sign-in would have taken them.
            var signedInUser = await _userManager.GetUserAsync(User);
            if (signedInUser != null)
            {
                return await PostSignInRedirect(signedInUser, returnUrl);
            }

            ViewData["ReturnUrl"] = returnUrl;
            return View();
        }

        [AllowAnonymous]
        [HttpPost]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("customer-lookup")]
        public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var user = await _userManager.FindByEmailAsync(model.Email.Trim());

            if (user == null)
            {
                // Deliberately vague so the form cannot be used to discover registered emails.
                ModelState.AddModelError(string.Empty, "Forkert e-mail eller adgangskode.");
                return View(model);
            }

            var result = await _signInManager.CheckPasswordSignInAsync(
                user,
                model.Password,
                lockoutOnFailure: true);

            if (!result.Succeeded)
            {
                ModelState.AddModelError(string.Empty, "Forkert e-mail eller adgangskode.");
                return View(model);
            }

            await _signInManager.SignInAsync(user, isPersistent: model.RememberMe);

            return await PostSignInRedirect(user, returnUrl);
        }

        /// <summary>
        /// Where a freshly authenticated user should land. Shared by the POST action and by the
        /// GET action's already-signed-in shortcut, so the two can never drift apart.
        /// </summary>
        private async Task<IActionResult> PostSignInRedirect(ApplicationUser user, string? returnUrl)
        {
            // An explicit returnUrl always wins, so a sign-in triggered mid-booking lands the
            // customer back in the flow they came from. The role-based default only applies
            // when there is no returnUrl to honour.
            if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return LocalRedirect(returnUrl);
            }

            // An admin works in the schedule, not in the customer booking list, and the
            // customer-facing nav does not even offer "Mine bookinger" to an admin.
            if (await _userManager.IsInRoleAsync(user, IdentitySeeder.AdminRole))
            {
                return RedirectToAction("Schedule", "Admin");
            }

            return RedirectToAction(nameof(MyBookings));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await _signInManager.SignOutAsync();
            return RedirectToAction("Index", "Home");
        }

        /// <summary>
        /// The link in the confirmation mail. Anonymous on purpose: the customer usually opens the
        /// mail on a device where they are not signed in.
        /// </summary>
        [AllowAnonymous]
        [HttpGet]
        public async Task<IActionResult> ConfirmEmail(string? userId, string? code)
        {
            var confirmed = await _verification.ConfirmAsync(userId, code);
            return View(confirmed);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("email-send")]
        public async Task<IActionResult> ResendConfirmation()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            if (user.EmailConfirmed)
            {
                return RedirectToAction(nameof(MyBookings));
            }

            var sent = await _verification.SendConfirmationAsync(
                user,
                code => ConfirmationLinks.Build(Url, Request, _site, user.Id, code));

            TempData["ConfirmationResend"] = sent ? "sent" : "failed";
            return RedirectToAction(nameof(MyBookings));
        }

        [HttpGet]
        public async Task<IActionResult> MyBookings()
        {
            var user = await _userManager.GetUserAsync(User);

            if (user == null)
            {
                return Challenge();
            }

            var bookings = await _context.Bookings
                .Include(b => b.Barber)
                .Include(b => b.Service)
                .Where(b => b.UserId == user.Id)
                .OrderBy(b => b.BookingTime)
                .ToListAsync();

            var now = _clock.LocalNow();
            var model = new MyBookingsViewModel
            {
                EmailUnconfirmed = !user.EmailConfirmed,
                Email = user.Email,
                Upcoming = bookings.Where(b => b.BookingTime >= now).ToList(),
                Past = bookings.Where(b => b.BookingTime < now).OrderByDescending(b => b.BookingTime).ToList()
            };

            return View(model);
        }
    }
}
