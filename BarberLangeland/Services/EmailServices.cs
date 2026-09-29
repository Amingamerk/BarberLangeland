using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace BarberLangeland.Services
{
    /// <summary>
    /// Settings for outgoing mail. In Azure use app settings such as <c>Email__Smtp__Host</c>;
    /// the password belongs in <c>Email__Smtp__Password</c> (or user secrets locally), never in
    /// appsettings.json. With no host configured no mail is sent and email verification is off.
    /// </summary>
    public class SmtpOptions
    {
        public string Host { get; set; } = string.Empty;

        public int Port { get; set; } = 587;

        public string UserName { get; set; } = string.Empty;

        public string Password { get; set; } = string.Empty;

        /// <summary>Address the mail is sent from; defaults to <see cref="UserName"/>.</summary>
        public string FromAddress { get; set; } = string.Empty;

        public string FromName { get; set; } = "Frisør Langeland";

        /// <summary>Use implicit TLS (port 465) instead of STARTTLS (port 587).</summary>
        public bool UseImplicitTls { get; set; }
    }

    /// <summary>Public web address used in mail links, for example https://www.frisorlangeland.dk.</summary>
    public class SiteOptions
    {
        public string BaseUrl { get; set; } = string.Empty;
    }

    public interface ISiteEmailSender
    {
        /// <summary>
        /// True when mail can really be delivered. When false, email verification is switched
        /// off so no customer is asked to click a link that was never sent.
        /// </summary>
        bool IsConfigured { get; }

        Task SendAsync(string toAddress, string subject, string htmlBody, string textBody,
            CancellationToken cancellationToken = default);
    }

    /// <summary>Used when no SMTP host is configured: the mail is only written to the log.</summary>
    public class LoggingEmailSender : ISiteEmailSender
    {
        private readonly ILogger<LoggingEmailSender> _logger;

        public LoggingEmailSender(ILogger<LoggingEmailSender> logger) => _logger = logger;

        public bool IsConfigured => false;

        public Task SendAsync(string toAddress, string subject, string htmlBody, string textBody,
            CancellationToken cancellationToken = default)
        {
            _logger.LogWarning("Email is not configured (Email:Smtp:Host); not sending '{Subject}' to {To}.",
                subject, toAddress);
            return Task.CompletedTask;
        }
    }

    public class SmtpEmailSender : ISiteEmailSender
    {
        private readonly SmtpOptions _options;

        public SmtpEmailSender(IOptions<SmtpOptions> options) => _options = options.Value;

        public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.Host);

        public async Task SendAsync(string toAddress, string subject, string htmlBody, string textBody,
            CancellationToken cancellationToken = default)
        {
            var from = string.IsNullOrWhiteSpace(_options.FromAddress) ? _options.UserName : _options.FromAddress;

            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(_options.FromName, from));
            message.To.Add(MailboxAddress.Parse(toAddress));
            message.Subject = subject;
            message.Body = new BodyBuilder { HtmlBody = htmlBody, TextBody = textBody }.ToMessageBody();

            using var client = new SmtpClient();
            await client.ConnectAsync(
                _options.Host,
                _options.Port,
                _options.UseImplicitTls ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls,
                cancellationToken);

            if (!string.IsNullOrWhiteSpace(_options.UserName))
            {
                await client.AuthenticateAsync(_options.UserName, _options.Password, cancellationToken);
            }

            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);
        }
    }
}
