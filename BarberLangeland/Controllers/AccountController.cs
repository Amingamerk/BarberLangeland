using BarberLangeland.Data;
using BarberLangeland.Models;
using BarberLangeland.Services;
using BarberLangeland.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BarberLangeland.Controllers
{
    /// <summary>Signed-in customer pages. Sign-in itself is handled by the Identity UI.</summary>
    [Authorize]
    public class AccountController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;

        public AccountController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager)
        {
            _context = context;
            _userManager = userManager;
            _signInManager = signInManager;
        }

        /// <summary>
        /// Email + password sign-in. The stock Identity UI login posts the email straight into
        /// PasswordSignInAsync, which performs a username lookup - and usernames here are
        /// opaque "user_&lt;guid&gt;" values, so that lookup never matches. Looking the user up
        /// by email first is what makes the shop administrator able to sign in.
        /// </summary>
        [AllowAnonymous]
        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;
            return View();
        }

        [AllowAnonymous]
        [HttpPost]
        [ValidateAntiForgeryToken]
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

            if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return LocalRedirect(returnUrl);
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

            var now = DateTime.Now;
            var model = new MyBookingsViewModel
            {
                Upcoming = bookings.Where(b => b.BookingTime >= now).ToList(),
                Past = bookings.Where(b => b.BookingTime < now).OrderByDescending(b => b.BookingTime).ToList()
            };

            return View(model);
        }
    }
}
