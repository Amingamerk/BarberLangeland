using System.Net.Mail;
using BarberLangeland.Models;
using Microsoft.AspNetCore.Identity;

namespace BarberLangeland.Services
{
    public class CustomerIdentityService : ICustomerIdentityService
    {
        private const string DefaultRegion = "DK";
        private const int MaxEmailLength = 254;

        private readonly IPhoneNumberNormalizer _normalizer;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;

        public CustomerIdentityService(
            IPhoneNumberNormalizer normalizer,
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager)
        {
            _normalizer = normalizer;
            _userManager = userManager;
            _signInManager = signInManager;
        }

        public string NormalizePhone(string? input)
        {
            return _normalizer.TryNormalize(input, DefaultRegion, out var e164) ? e164 : string.Empty;
        }

        public string NormalizeEmail(string? input)
        {
            var email = input?.Trim();

            if (string.IsNullOrEmpty(email) || email.Length > MaxEmailLength || email.Any(char.IsWhiteSpace))
            {
                return string.Empty;
            }

            // MailAddress accepts things such as "Name <a@b.dk>" and "a@b" (no dot in the domain);
            // require the parsed address to be exactly what was typed, and a dotted domain.
            if (!MailAddress.TryCreate(email, out var parsed)
                || !string.Equals(parsed.Address, email, StringComparison.Ordinal)
                || !parsed.Host.Contains('.')
                || parsed.Host.StartsWith('.')
                || parsed.Host.EndsWith('.'))
            {
                return string.Empty;
            }

            return email;
        }

        public async Task<CustomerLookupOutcome> CheckEmailAsync(string? email)
        {
            var normalized = NormalizeEmail(email);

            if (normalized.Length == 0)
            {
                return CustomerLookupOutcome.InvalidEmail;
            }

            return await _userManager.FindByEmailAsync(normalized) == null
                ? CustomerLookupOutcome.UnknownEmail
                : CustomerLookupOutcome.KnownEmail;
        }

        public async Task<ApplicationUser?> FindByEmailAsync(string? email)
        {
            var normalized = NormalizeEmail(email);
            return normalized.Length == 0 ? null : await _userManager.FindByEmailAsync(normalized);
        }

        public async Task<bool> SignInAsync(string? email, string? password)
        {
            if (string.IsNullOrEmpty(password))
            {
                return false;
            }

            var user = await FindByEmailAsync(email);
            if (user == null)
            {
                return false;
            }

            var result = await _signInManager.PasswordSignInAsync(
                user,
                password,
                isPersistent: false,
                lockoutOnFailure: true);

            return result.Succeeded;
        }

        public async Task<IdentityResult> RegisterAsync(string? email, string? name, string? phone, string? password)
        {
            var normalizedEmail = NormalizeEmail(email);
            if (normalizedEmail.Length == 0)
            {
                return IdentityResult.Failed(new IdentityError
                {
                    Code = "InvalidEmail",
                    Description = "Indtast en gyldig e-mail."
                });
            }

            var e164 = NormalizePhone(phone);
            if (e164.Length == 0)
            {
                return IdentityResult.Failed(new IdentityError
                {
                    Code = "InvalidPhone",
                    Description = "Indtast et gyldigt telefonnummer."
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

            // UserManager.CreateAsync does not enforce a unique email by itself (the database
            // index is the backstop), so check first and give a readable message.
            if (await _userManager.FindByEmailAsync(normalizedEmail) != null)
            {
                return IdentityResult.Failed(new IdentityError
                {
                    Code = "DuplicateEmail",
                    Description = "Der findes allerede en konto med denne e-mail."
                });
            }

            var user = new ApplicationUser
            {
                // The email is the customer-facing identity, so the Identity user name is an
                // opaque value rather than something the customer types.
                UserName = $"user_{Guid.NewGuid():N}",
                Email = normalizedEmail,
                EmailConfirmed = false,
                PhoneNumber = e164,
                // Not verified yet; verification by email link is planned. Do not mark it
                // confirmed until something has actually confirmed it.
                PhoneNumberConfirmed = false
            };

            var result = await _userManager.CreateAsync(user, password);
            if (!result.Succeeded)
            {
                return result;
            }

            await _signInManager.SignInAsync(user, isPersistent: false);
            return result;
        }
    }
}
