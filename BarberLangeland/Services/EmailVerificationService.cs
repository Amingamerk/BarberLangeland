using System.Net;
using System.Text;
using BarberLangeland.Data;
using BarberLangeland.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace BarberLangeland.Services
{
    public interface IEmailVerificationService
    {
        /// <summary>False when no mail can be sent; new accounts are then treated as confirmed.</summary>
        bool IsEnabled { get; }

        /// <summary>How long a customer has to confirm before the account and its bookings are removed.</summary>
        TimeSpan ConfirmationWindow { get; }

        /// <summary>
        /// Mails a confirmation link to the account's email. <paramref name="linkForCode"/> turns the
        /// URL-safe code into the absolute link. Returns false if the mail could not be sent.
        /// </summary>
        Task<bool> SendConfirmationAsync(ApplicationUser user, Func<string, string> linkForCode);

        /// <summary>
        /// Confirms the email with the code from the link and confirms the customer's waiting
        /// bookings. Returns false for an unknown user, a wrong code or an expired link.
        /// </summary>
        Task<bool> ConfirmAsync(string? userId, string? code);

        /// <summary>
        /// Removes accounts that registered before <paramref name="registeredBefore"/> and never
        /// confirmed their email, together with their bookings (which frees the times). Returns the
        /// number of accounts removed.
        /// </summary>
        Task<int> DeleteUnconfirmedAccountsAsync(DateTime registeredBefore);
    }

    public class EmailVerificationService : IEmailVerificationService
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ApplicationDbContext _context;
        private readonly ISiteEmailSender _sender;
        private readonly ILogger<EmailVerificationService> _logger;

        public EmailVerificationService(
            UserManager<ApplicationUser> userManager,
            ApplicationDbContext context,
            ISiteEmailSender sender,
            ILogger<EmailVerificationService> logger)
        {
            _userManager = userManager;
            _context = context;
            _sender = sender;
            _logger = logger;
        }

        public bool IsEnabled => _sender.IsConfigured;

        public TimeSpan ConfirmationWindow => TimeSpan.FromHours(24);

        public async Task<bool> SendConfirmationAsync(ApplicationUser user, Func<string, string> linkForCode)
        {
            if (!IsEnabled || string.IsNullOrWhiteSpace(user.Email))
            {
                return false;
            }

            var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
            var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
            var link = linkForCode(code);
            var hours = (int)ConfirmationWindow.TotalHours;

            var text =
                "Hej,\n\n" +
                "Tak fordi du bookede en tid hos Frisør Langeland. Bekræft din e-mail ved at åbne dette link:\n\n" +
                $"{link}\n\n" +
                $"Linket gælder i {hours} timer. Bekræfter du ikke inden da, bliver din booking annulleret, og tiden frigives.\n\n" +
                "Hvis det ikke var dig, kan du blot ignorere denne mail.\n\n" +
                "Frisør Langeland";

            var safeLink = WebUtility.HtmlEncode(link);
            var html =
                "<p>Hej,</p>" +
                "<p>Tak fordi du bookede en tid hos Frisør Langeland. Bekræft din e-mail ved at klikke på knappen:</p>" +
                $"<p><a href=\"{safeLink}\" style=\"background:#7C9885;color:#FAF7F2;padding:12px 24px;border-radius:24px;text-decoration:none;font-weight:bold\">Bekræft min e-mail</a></p>" +
                $"<p>Virker knappen ikke, så kopiér dette link ind i din browser:<br>{safeLink}</p>" +
                $"<p>Linket gælder i {hours} timer. Bekræfter du ikke inden da, bliver din booking annulleret, og tiden frigives.</p>" +
                "<p>Hvis det ikke var dig, kan du blot ignorere denne mail.</p>" +
                "<p>Frisør Langeland</p>";

            try
            {
                await _sender.SendAsync(user.Email, "Bekræft din e-mail hos Frisør Langeland", html, text);
                return true;
            }
            catch (Exception ex)
            {
                // Never lose the booking because the mail server hiccuped; the customer can ask
                // for a new mail from "Mine bookinger".
                _logger.LogError(ex, "Could not send the confirmation mail to {UserId}.", user.Id);
                return false;
            }
        }

        public async Task<bool> ConfirmAsync(string? userId, string? code)
        {
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(code))
            {
                return false;
            }

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return false;
            }

            string token;
            try
            {
                token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code));
            }
            catch (FormatException)
            {
                return false;
            }

            if (!user.EmailConfirmed)
            {
                var result = await _userManager.ConfirmEmailAsync(user, token);
                if (!result.Succeeded)
                {
                    return false;
                }
            }
            else if (!await _userManager.VerifyUserTokenAsync(
                         user, _userManager.Options.Tokens.EmailConfirmationTokenProvider, "EmailConfirmation", token))
            {
                // Already confirmed: an old but valid link is fine, a made-up one is not.
                return false;
            }

            var waiting = await _context.Bookings
                .Where(b => b.UserId == user.Id && !b.IsConfirmed && !b.IsCancelled && !b.IsNoShow)
                .ToListAsync();

            foreach (var booking in waiting)
            {
                booking.IsConfirmed = true;
            }

            if (waiting.Count > 0)
            {
                await _context.SaveChangesAsync();
            }

            return true;
        }

        public async Task<int> DeleteUnconfirmedAccountsAsync(DateTime registeredBefore)
        {
            var adminRoleId = await _context.Roles
                .Where(r => r.Name == IdentitySeeder.AdminRole)
                .Select(r => r.Id)
                .FirstOrDefaultAsync();

            var stale = await _context.Users
                .Where(u => !u.EmailConfirmed && u.RegisteredAt < registeredBefore)
                .Where(u => adminRoleId == null || !_context.UserRoles.Any(ur => ur.UserId == u.Id && ur.RoleId == adminRoleId))
                .ToListAsync();

            if (stale.Count == 0)
            {
                return 0;
            }

            // Bookings, claims, logins and tokens are removed by the cascading foreign keys.
            _context.Users.RemoveRange(stale);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Removed {Count} accounts that never confirmed their email.", stale.Count);
            return stale.Count;
        }
    }

    /// <summary>Runs the clean-up every ten minutes; a sleeping app catches up on the next start.</summary>
    public class UnconfirmedAccountCleanupService : BackgroundService
    {
        private static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

        private readonly IServiceScopeFactory _scopes;
        private readonly TimeProvider _clock;
        private readonly ILogger<UnconfirmedAccountCleanupService> _logger;

        public UnconfirmedAccountCleanupService(
            IServiceScopeFactory scopes,
            TimeProvider clock,
            ILogger<UnconfirmedAccountCleanupService> logger)
        {
            _scopes = scopes;
            _clock = clock;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(Interval);

            do
            {
                try
                {
                    using var scope = _scopes.CreateScope();
                    var verification = scope.ServiceProvider.GetRequiredService<IEmailVerificationService>();

                    // Only when mail really works. Otherwise every account is created confirmed and
                    // there is nothing to clean up; an unconfigured server must never delete accounts.
                    if (verification.IsEnabled)
                    {
                        await verification.DeleteUnconfirmedAccountsAsync(
                            _clock.LocalNow() - verification.ConfirmationWindow);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Clean-up of unconfirmed accounts failed.");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
    }
}
