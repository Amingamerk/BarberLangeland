using BarberLangeland.Data;
using BarberLangeland.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BarberLangeland.Services
{
    /// <summary>
    /// Phone-first authentication on top of ASP.NET Core Identity. The phone number is the
    /// customer facing identity and the lookup key; the Identity user name is an opaque value
    /// so two people can share a number without colliding.
    /// </summary>
    public class PhoneIdentityService : IPhoneIdentityService
    {
        private const string DefaultRegion = "DK";

        private readonly ApplicationDbContext _context;
        private readonly IPhoneNumberNormalizer _normalizer;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;

        public PhoneIdentityService(
            ApplicationDbContext context,
            IPhoneNumberNormalizer normalizer,
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager)
        {
            _context = context;
            _normalizer = normalizer;
            _userManager = userManager;
            _signInManager = signInManager;
        }

        public string NormalizePhone(string? input)
        {
            return _normalizer.TryNormalize(input, DefaultRegion, out var e164) ? e164 : string.Empty;
        }

        public async Task<PhoneIdentityOutcome> CheckPhoneAsync(string? input)
        {
            var e164 = NormalizePhone(input);

            if (e164.Length == 0)
            {
                return PhoneIdentityOutcome.InvalidNumber;
            }

            return await FindByPhoneAsync(e164) == null
                ? PhoneIdentityOutcome.UnknownNumber
                : PhoneIdentityOutcome.KnownNumber;
        }

        public async Task<bool> SignInWithPhoneAsync(string? phone, string? password)
        {
            var e164 = NormalizePhone(phone);

            if (e164.Length == 0 || string.IsNullOrEmpty(password))
            {
                return false;
            }

            var user = await FindByPhoneAsync(e164);
            if (user == null)
            {
                return false;
            }

            var result = await _signInManager.PasswordSignInAsync(
                user,
                password,
                isPersistent: false,
                lockoutOnFailure: false);

            return result.Succeeded;
        }

        public async Task<IdentityResult> RegisterWithPhoneAsync(
            string? phone,
            string? name,
            string? email,
            string? password)
        {
            var e164 = NormalizePhone(phone);

            if (e164.Length == 0)
            {
                return IdentityResult.Failed(new IdentityError
                {
                    Code = "InvalidPhone",
                    Description = "Telefonnummeret er ikke gyldigt."
                });
            }

            if (string.IsNullOrWhiteSpace(password))
            {
                return IdentityResult.Failed(new IdentityError
                {
                    Code = "PasswordRequired",
                    Description = "Vælg en adgangskode."
                });
            }

            // The phone number is a lookup key and may legitimately be shared, so only the
            // email has to be unique. UserManager.CreateAsync does not enforce that itself.
            var trimmedEmail = email?.Trim();

            if (string.IsNullOrWhiteSpace(trimmedEmail))
            {
                return IdentityResult.Failed(new IdentityError
                {
                    Code = "EmailRequired",
                    Description = "Indtast din e-mail."
                });
            }

            if (await _userManager.FindByEmailAsync(trimmedEmail) != null)
            {
                return IdentityResult.Failed(new IdentityError
                {
                    Code = "DuplicateEmail",
                    Description = "Der findes allerede en konto med denne e-mail."
                });
            }

            var user = new ApplicationUser
            {
                UserName = $"user_{Guid.NewGuid():N}",
                PhoneNumber = e164,
                PhoneNumberConfirmed = true,
                Email = trimmedEmail,
                EmailConfirmed = false
            };

            var result = await _userManager.CreateAsync(user, password);
            if (!result.Succeeded)
            {
                return result;
            }

            await _signInManager.SignInAsync(user, isPersistent: false);
            return result;
        }

        private Task<ApplicationUser?> FindByPhoneAsync(string e164)
        {
            return _context.Users.FirstOrDefaultAsync(user => user.PhoneNumber == e164);
        }
    }
}
