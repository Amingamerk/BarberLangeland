using BarberLangeland.Data;
using BarberLangeland.Models;
using Microsoft.AspNetCore.Identity;

namespace BarberLangeland.Services
{
    /// <summary>
    /// Creates the Admin role and the salon administrator account on startup. Safe to run on
    /// every boot: each step checks the current state before changing anything.
    /// </summary>
    public class IdentitySeeder
    {
        public const string AdminRole = "Admin";

        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ILogger<IdentitySeeder> _logger;

        public IdentitySeeder(
            RoleManager<IdentityRole> roleManager,
            UserManager<ApplicationUser> userManager,
            ILogger<IdentitySeeder> logger)
        {
            _roleManager = roleManager;
            _userManager = userManager;
            _logger = logger;
        }

        /// <summary>
        /// Ensures the Admin role exists and the administrator is in it. The password is never
        /// defaulted: without one supplied through user secrets or environment configuration the
        /// account is not created, because a guessable seeded password is worse than no account.
        /// </summary>
        public async Task SeedAsync(string? adminEmail, string? adminPassword)
        {
            if (string.IsNullOrWhiteSpace(adminEmail))
            {
                _logger.LogWarning(
                    "No salon administrator configured; set Salon:AdminEmail to create one.");
                return;
            }

            if (string.IsNullOrWhiteSpace(adminPassword))
            {
                // Still create the role so the rest of the app behaves normally, but do not
                // create an account that anyone could sign into with a known password.
                await EnsureRoleAsync();

                _logger.LogWarning(
                    "Salon:AdminPassword is not configured, so no administrator account was created " +
                    "for {Email}. Set it outside source control and restart, for example: " +
                    "dotnet user-secrets set \"Salon:AdminPassword\" \"<your-password>\"",
                    adminEmail);
                return;
            }

            await EnsureAdminUserAsync(adminEmail, adminPassword);
        }

        private async Task EnsureRoleAsync()
        {
            if (!await _roleManager.RoleExistsAsync(AdminRole))
            {
                var result = await _roleManager.CreateAsync(new IdentityRole(AdminRole));

                if (!result.Succeeded)
                {
                    _logger.LogError(
                        "Could not create the {Role} role: {Errors}",
                        AdminRole,
                        string.Join("; ", result.Errors.Select(error => error.Description)));
                    return;
                }
            }
        }

        private async Task EnsureAdminUserAsync(string adminEmail, string adminPassword)
        {
            await EnsureRoleAsync();

            var email = adminEmail.Trim();
            var user = await _userManager.FindByEmailAsync(email);

            if (user == null)
            {
                user = new ApplicationUser
                {
                    // The phone number is the customer facing identity, so the Identity user
                    // name is an opaque value rather than something the customer types.
                    UserName = $"user_{Guid.NewGuid():N}",
                    Email = email,
                    EmailConfirmed = true
                };

                var created = await _userManager.CreateAsync(user, adminPassword);
                if (!created.Succeeded)
                {
                    _logger.LogError(
                        "Could not create the salon administrator: {Errors}",
                        string.Join("; ", created.Errors.Select(error => error.Description)));
                    return;
                }

                _logger.LogInformation(
                    "Seeded salon administrator {Email} using the configured password.",
                    email);
            }

            if (!await _userManager.IsInRoleAsync(user, AdminRole))
            {
                await _userManager.AddToRoleAsync(user, AdminRole);
            }
        }
    }
}
