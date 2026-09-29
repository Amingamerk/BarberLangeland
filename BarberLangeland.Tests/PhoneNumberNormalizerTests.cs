using BarberLangeland.Services;
using Xunit;

namespace BarberLangeland.Tests;

public class PhoneNumberNormalizerTests
{
    private readonly PhoneNumberNormalizer _normalizer = new();

    [Theory(DisplayName = "Danish numbers in common formats normalise to the same E.164 value")]
    [InlineData("32123456")]
    [InlineData("32 12 34 56")]
    [InlineData("+45 32 12 34 56")]
    [InlineData("0045 32123456")]
    [InlineData("+4532123456")]
    public void Common_formats_normalise_identically(string input)
    {
        Assert.True(_normalizer.TryNormalize(input, "DK", out var e164));
        Assert.Equal("+4532123456", e164);
    }

    [Theory(DisplayName = "Invalid phone numbers are rejected")]
    [InlineData("abc")]
    [InlineData("123")]
    [InlineData("20000001")]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("+45 1234")]
    public void Invalid_numbers_are_rejected(string input)
    {
        Assert.False(_normalizer.TryNormalize(input, "DK", out var e164));
        Assert.Equal(string.Empty, e164);
    }

    [Theory(DisplayName = "Empty phone input is rejected")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_input_is_rejected(string? input)
    {
        Assert.False(_normalizer.TryNormalize(input, "DK", out _));
    }

    [Fact(DisplayName = "A valid foreign number with a country prefix is accepted")]
    public void Foreign_number_with_prefix_is_accepted()
    {
        Assert.True(_normalizer.TryNormalize("+46 70 123 45 67", "DK", out var e164));
        Assert.StartsWith("+46", e164);
    }
}
