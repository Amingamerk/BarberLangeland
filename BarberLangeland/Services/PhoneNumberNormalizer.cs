using PhoneNumbers;

namespace BarberLangeland.Services
{
    /// <summary>
    /// Normalizes phone numbers to E.164 using Google's libphonenumber metadata, so the
    /// same number typed as "50505050" or "+45 50 50 50 50" always compares equal.
    /// </summary>
    public class PhoneNumberNormalizer : IPhoneNumberNormalizer
    {
        // The util is stateless and thread-safe, so a single shared instance is reused.
        private static readonly PhoneNumberUtil Util = PhoneNumberUtil.GetInstance();

        public bool TryNormalize(string? input, string defaultRegion, out string e164)
        {
            e164 = string.Empty;

            if (string.IsNullOrWhiteSpace(input))
            {
                return false;
            }

            var region = string.IsNullOrWhiteSpace(defaultRegion) ? "DK" : defaultRegion.Trim().ToUpperInvariant();

            try
            {
                var parsed = Util.Parse(input.Trim(), region);

                if (!Util.IsValidNumber(parsed))
                {
                    return false;
                }

                e164 = Util.Format(parsed, PhoneNumberFormat.E164);
                return true;
            }
            catch (NumberParseException)
            {
                // Not parseable as a phone number for the given region.
                return false;
            }
        }
    }
}
