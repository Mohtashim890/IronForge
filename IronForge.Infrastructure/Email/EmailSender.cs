using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace IronForge.Infrastructure.Email;

/// <summary>
/// SMTP settings. Leave Host empty to log emails instead of sending them.
/// Locally, compose runs Mailpit (SMTP on 1025, inbox UI on 8025).
/// </summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public string? Host { get; set; }

    public int Port { get; set; } = 587;

    /// <summary>"None", "StartTls" or "SslOnConnect".</summary>
    public string Security { get; set; } = "StartTls";

    public string? Username { get; set; }

    public string? Password { get; set; }

    public string FromAddress { get; set; } = "no-reply@ironforge.local";

    public string FromName { get; set; } = "IronForge";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host);
}

public sealed record OutgoingEmail(string ToEmail, string? ToName, string Subject, string HtmlBody);

public interface IEmailSender
{
    Task SendAsync(OutgoingEmail email, CancellationToken cancellationToken = default);
}

public sealed class SmtpEmailSender : IEmailSender
{
    private readonly EmailOptions _options;

    public SmtpEmailSender(IOptions<EmailOptions> options)
    {
        _options = options.Value;
    }

    public async Task SendAsync(OutgoingEmail email, CancellationToken cancellationToken = default)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_options.FromName, _options.FromAddress));
        message.To.Add(new MailboxAddress(email.ToName ?? email.ToEmail, email.ToEmail));
        message.Subject = email.Subject;
        message.Body = new BodyBuilder { HtmlBody = email.HtmlBody }.ToMessageBody();

        var security = Enum.TryParse<SecureSocketOptions>(_options.Security, ignoreCase: true, out var parsed)
            ? parsed
            : SecureSocketOptions.StartTls;

        using var client = new SmtpClient();

        await client.ConnectAsync(_options.Host, _options.Port, security, cancellationToken);

        if (!string.IsNullOrWhiteSpace(_options.Username))
        {
            await client.AuthenticateAsync(_options.Username, _options.Password, cancellationToken);
        }

        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);
    }
}

/// <summary>Used when SMTP is not configured: the email is written to the log.</summary>
public sealed class LoggingEmailSender : IEmailSender
{
    private readonly ILogger<LoggingEmailSender> _logger;

    public LoggingEmailSender(ILogger<LoggingEmailSender> logger)
    {
        _logger = logger;
    }

    public Task SendAsync(OutgoingEmail email, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Email (SMTP not configured) to {ToEmail}: {Subject}",
            email.ToEmail,
            email.Subject);

        return Task.CompletedTask;
    }
}
