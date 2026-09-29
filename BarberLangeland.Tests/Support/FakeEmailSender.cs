using BarberLangeland.Services;

namespace BarberLangeland.Tests.Support;

public sealed record SentEmail(string To, string Subject, string Html, string Text);

/// <summary>Captures outgoing mail instead of sending it.</summary>
public sealed class FakeEmailSender : ISiteEmailSender
{
    private readonly List<SentEmail> _sent = [];

    public FakeEmailSender(bool isConfigured) => IsConfigured = isConfigured;

    public bool IsConfigured { get; }

    /// <summary>When set, every send throws, like an unreachable mail server.</summary>
    public bool Fail { get; set; }

    public IReadOnlyList<SentEmail> Sent
    {
        get { lock (_sent) { return _sent.ToList(); } }
    }

    public Task SendAsync(string toAddress, string subject, string htmlBody, string textBody,
        CancellationToken cancellationToken = default)
    {
        if (Fail)
        {
            throw new InvalidOperationException("Mail server unreachable.");
        }

        lock (_sent)
        {
            _sent.Add(new SentEmail(toAddress, subject, htmlBody, textBody));
        }

        return Task.CompletedTask;
    }

    /// <summary>The path and query of the confirmation link in the given mail.</summary>
    public static string LinkPathAndQuery(SentEmail mail)
    {
        var match = System.Text.RegularExpressions.Regex.Match(mail.Text, @"https?://[^\s/]+(/Account/ConfirmEmail\?\S+)");
        if (!match.Success)
        {
            throw new InvalidOperationException("No confirmation link in the mail: " + mail.Text);
        }

        return match.Groups[1].Value;
    }
}
