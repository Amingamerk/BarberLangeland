namespace BarberLangeland.Services
{
    public interface IPhoneNumberNormalizer
    {
        /// <summary>
        /// Normalizes a user supplied phone number to E.164 (for example +4512345678).
        /// </summary>
        /// <param name="input">Raw input as typed by the customer.</param>
        /// <param name="defaultRegion">
        /// ISO 3166-1 alpha-2 region used when the input carries no country code
        /// (for example "DK" for the +45 default in the booking form).
        /// </param>
        /// <param name="e164">The normalized number, or an empty string when invalid.</param>
        /// <returns>True when the input could be parsed as a valid number.</returns>
        bool TryNormalize(string? input, string defaultRegion, out string e164);
    }
}
