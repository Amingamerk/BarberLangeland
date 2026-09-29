using BarberLangeland.Services;
using BarberLangeland.Tests.Support;
using Microsoft.AspNetCore.Identity;
using Xunit;

namespace BarberLangeland.Tests;

public class PhoneIdentityServiceTests
{
    private const string StrongPassword = "Test-Booking-2026!";

    private static Task<IdentityResult> Register(IdentityHarness h, string phone = "32123456",
        string email = "kunde@example.com", string password = StrongPassword)
        => h.Phone.RegisterWithPhoneAsync(phone, "Kunde", email, password);

    [Fact(DisplayName = "Checking an invalid phone number reports InvalidNumber")]
    public async Task Invalid_number_is_reported()
    {
        using var h = new IdentityHarness();
        Assert.Equal(PhoneIdentityOutcome.InvalidNumber, await h.Phone.CheckPhoneAsync("abc"));
        Assert.Equal(PhoneIdentityOutcome.InvalidNumber, await h.Phone.CheckPhoneAsync(null));
    }

    [Fact(DisplayName = "An unregistered number is unknown, and known after registration in any format")]
    public async Task Number_becomes_known_after_registration()
    {
        using var h = new IdentityHarness();
        Assert.Equal(PhoneIdentityOutcome.UnknownNumber, await h.Phone.CheckPhoneAsync("32123456"));

        Assert.True((await Register(h)).Succeeded);

        Assert.Equal(PhoneIdentityOutcome.KnownNumber, await h.Phone.CheckPhoneAsync("+45 32 12 34 56"));
    }

    [Fact(DisplayName = "Registration stores the E.164 phone number and an opaque user name")]
    public async Task Registration_stores_normalised_phone_and_opaque_username()
    {
        using var h = new IdentityHarness();
        await Register(h, phone: "32 12 34 56");

        var user = await h.Users.FindByEmailAsync("kunde@example.com");

        Assert.NotNull(user);
        Assert.Equal("+4532123456", user!.PhoneNumber);
        Assert.StartsWith("user_", user.UserName);
    }

    [Fact(DisplayName = "Registering a second account with the same email is rejected")]
    public async Task Duplicate_email_is_rejected()
    {
        using var h = new IdentityHarness();
        Assert.True((await Register(h)).Succeeded);

        var second = await Register(h, phone: "32123457", email: "KUNDE@example.com");

        Assert.False(second.Succeeded);
    }

    [Fact(DisplayName = "Two accounts may share the same phone number")]
    public async Task Phone_number_may_be_shared()
    {
        using var h = new IdentityHarness();
        Assert.True((await Register(h, email: "a@example.com")).Succeeded);
        Assert.True((await Register(h, email: "b@example.com")).Succeeded);
    }

    [Theory(DisplayName = "Weak passwords are rejected at registration")]
    [InlineData("abc")]
    [InlineData("password")]
    [InlineData("12345678")]
    public async Task Weak_passwords_are_rejected(string password)
    {
        using var h = new IdentityHarness();
        Assert.False((await Register(h, password: password)).Succeeded);
        Assert.Null(await h.Users.FindByEmailAsync("kunde@example.com"));
    }

    [Fact(DisplayName = "Registration requires a valid phone number, a password and an email")]
    public async Task Registration_requires_mandatory_fields()
    {
        using var h = new IdentityHarness();
        Assert.False((await h.Phone.RegisterWithPhoneAsync("abc", "K", "k@example.com", StrongPassword)).Succeeded);
        Assert.False((await h.Phone.RegisterWithPhoneAsync("32123456", "K", "k@example.com", "")).Succeeded);
        Assert.False((await h.Phone.RegisterWithPhoneAsync("32123456", "K", "  ", StrongPassword)).Succeeded);
    }

    [Theory(DisplayName = "Registration rejects an email address that is not a valid address")]
    [InlineData("abc")]
    [InlineData("not an email")]
    [InlineData("@example.com")]
    public async Task Invalid_email_format_is_rejected(string email)
    {
        using var h = new IdentityHarness();

        var result = await Register(h, email: email);

        Assert.False(result.Succeeded, $"'{email}' was accepted as an email address.");
    }

    [Fact(DisplayName = "Signing in with the right password succeeds and a wrong one fails")]
    public async Task Sign_in_checks_the_password()
    {
        using var h = new IdentityHarness();
        await Register(h);

        Assert.True(await h.Phone.SignInWithPhoneAsync("32123456", StrongPassword));
        Assert.False(await h.Phone.SignInWithPhoneAsync("32123456", "Wrong-password-1"));
        Assert.False(await h.Phone.SignInWithPhoneAsync("32123456", ""));
        Assert.False(await h.Phone.SignInWithPhoneAsync("91234567", StrongPassword));
    }

    [Fact(DisplayName = "Repeated wrong passwords on the phone sign-in lock the account")]
    public async Task Repeated_wrong_passwords_lock_the_account()
    {
        using var h = new IdentityHarness();
        await Register(h);

        for (var attempt = 0; attempt < 10; attempt++)
        {
            await h.Phone.SignInWithPhoneAsync("32123456", $"Wrong-password-{attempt}!");
        }

        // Once locked out, even the correct password must be refused.
        Assert.False(await h.Phone.SignInWithPhoneAsync("32123456", StrongPassword),
            "The account was never locked out after 10 wrong passwords, so the booking form allows unlimited password guessing.");
    }
}
