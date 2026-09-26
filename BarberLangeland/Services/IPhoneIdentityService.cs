using Microsoft.AspNetCore.Identity;

namespace BarberLangeland.Services
{
    public enum PhoneIdentityOutcome
    {
        /// <summary>The number is not valid; the customer must correct it.</summary>
        InvalidNumber,

        /// <summary>No account owns this number; registration details are required.</summary>
        UnknownNumber,

        /// <summary>An account owns this number; a password is required to continue.</summary>
        KnownNumber
    }

    public interface IPhoneIdentityService
    {
        /// <summary>
        /// Normalizes the supplied number to E.164, or returns an empty string when invalid.
        /// </summary>
        string NormalizePhone(string? input);

        /// <summary>
        /// Normalizes the number and reports whether an account already owns it.
        /// </summary>
        Task<PhoneIdentityOutcome> CheckPhoneAsync(string? input);

        /// <summary>
        /// Signs in the customer whose account owns the supplied number. Returns false when the
        /// number is unknown or the password does not match.
        /// </summary>
        Task<bool> SignInWithPhoneAsync(string? phone, string? password);

        /// <summary>
        /// Creates a new Identity account for the supplied number and signs the customer in.
        /// </summary>
        Task<IdentityResult> RegisterWithPhoneAsync(string? phone, string? name, string? email, string? password);
    }
}
