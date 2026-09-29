using BarberLangeland.Models;
using Microsoft.AspNetCore.Identity;

namespace BarberLangeland.Services
{
    public enum CustomerLookupOutcome
    {
        /// <summary>The value is not an email address; the customer must correct it.</summary>
        InvalidEmail,

        /// <summary>No account uses this email; registration details are required.</summary>
        UnknownEmail,

        /// <summary>An account uses this email; a password is required to continue.</summary>
        KnownEmail
    }

    /// <summary>
    /// Customer accounts on top of ASP.NET Core Identity. The email address is the customer's
    /// key: it is what the booking form looks accounts up by and what the login page uses. The
    /// phone number is a required contact detail for the barber and is not used for lookup.
    /// </summary>
    public interface ICustomerIdentityService
    {
        /// <summary>
        /// Normalizes the supplied number to E.164, or returns an empty string when invalid.
        /// </summary>
        string NormalizePhone(string? input);

        /// <summary>
        /// Returns the trimmed email address, or an empty string when the value is not a valid
        /// address (no whitespace, one @, a dotted domain, at most 254 characters).
        /// </summary>
        string NormalizeEmail(string? input);

        /// <summary>Reports whether an account already uses the supplied email.</summary>
        Task<CustomerLookupOutcome> CheckEmailAsync(string? email);

        /// <summary>The account that uses this email (case-insensitive), or null.</summary>
        Task<ApplicationUser?> FindByEmailAsync(string? email);

        /// <summary>
        /// Signs in the customer with this email. Returns false when there is no such account,
        /// the password does not match, or the account is locked out after repeated failures.
        /// </summary>
        Task<bool> SignInAsync(string? email, string? password);

        /// <summary>
        /// Creates a new account for the supplied email, stores the phone number as contact
        /// detail and signs the customer in. Neither the email nor the phone number is verified.
        /// </summary>
        Task<IdentityResult> RegisterAsync(string? email, string? name, string? phone, string? password);
    }
}
