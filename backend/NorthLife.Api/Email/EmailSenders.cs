using System.Net;
using System.Net.Http.Json;
using System.Net.Mail;
using Microsoft.Extensions.Options;

namespace NorthLife.Api.Email;

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>Log (default, nothing sent), Smtp (Mailpit locally) or Brevo (production).</summary>
    public string Provider { get; init; } = "Log";
    public string From { get; init; } = "no-reply@northlife.local";
    public string FromName { get; init; } = "NorthLife";

    /// <summary>Origin used in emailed links, e.g. https://northlife.onrender.com.</summary>
    public string PublicBaseUrl { get; init; } = "http://localhost:4200";
    public SmtpSettings Smtp { get; init; } = new();
    public string BrevoApiKey { get; init; } = string.Empty;

    public sealed class SmtpSettings
    {
        public string Host { get; init; } = "localhost";
        public int Port { get; init; } = 1025;
        public bool EnableSsl { get; init; }
        public string UserName { get; init; } = string.Empty;
        public string Password { get; init; } = string.Empty;
    }
}

public sealed record EmailMessage(string To, string Subject, string Html, string Text);

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

/// <summary>Development default: records that a message would be sent, without its body or links.</summary>
public sealed class LogEmailSender(ILogger<LogEmailSender> logger) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        logger.LogWarning(
            "Email provider is not configured; a message \"{Subject}\" was not sent.",
            message.Subject);
        return Task.CompletedTask;
    }
}

public sealed class SmtpEmailSender(IOptions<EmailOptions> options) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        using var client = new SmtpClient(settings.Smtp.Host, settings.Smtp.Port)
        {
            EnableSsl = settings.Smtp.EnableSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
        };
        if (!string.IsNullOrEmpty(settings.Smtp.UserName))
        {
            client.Credentials = new NetworkCredential(settings.Smtp.UserName, settings.Smtp.Password);
        }

        using var mail = new MailMessage
        {
            From = new MailAddress(settings.From, settings.FromName),
            Subject = message.Subject,
            Body = message.Html,
            IsBodyHtml = true,
        };
        mail.To.Add(message.To);
        mail.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(message.Text, null, "text/plain"));
        await client.SendMailAsync(mail, cancellationToken);
    }
}

/// <summary>Brevo transactional email API (free plan: 300 emails per day).</summary>
public sealed class BrevoEmailSender(HttpClient http, IOptions<EmailOptions> options) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.brevo.com/v3/smtp/email")
        {
            Content = JsonContent.Create(new
            {
                sender = new { name = settings.FromName, email = settings.From },
                to = new[] { new { email = message.To } },
                subject = message.Subject,
                htmlContent = message.Html,
                textContent = message.Text,
            }),
        };
        request.Headers.Add("api-key", settings.BrevoApiKey);
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
