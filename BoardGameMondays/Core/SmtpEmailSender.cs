using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BoardGameMondays.Core;

public sealed class SmtpEmailSender : IEmailSender
{
    private readonly EmailOptions _options;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(IOptions<EmailOptions> options, ILogger<SmtpEmailSender> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task SendEmailAsync(string toEmail, string subject, string htmlBody)
    {
        if (string.IsNullOrWhiteSpace(_options.Smtp.Host))
        {
            _logger.LogWarning("Email not sent because SMTP host is not configured. To={ToEmail}, Subject={Subject}", toEmail, subject);
            _logger.LogInformation("Email body (truncated): {Body}", htmlBody.Length > 500 ? htmlBody[..500] + "…" : htmlBody);
            return;
        }

        using var message = new MailMessage();
        message.From = new MailAddress(_options.FromEmail, _options.FromName);
        message.To.Add(new MailAddress(toEmail));
        message.Subject = subject;
        message.Body = htmlBody;
        message.IsBodyHtml = true;

        using var client = new SmtpClient(_options.Smtp.Host, _options.Smtp.Port)
        {
            EnableSsl = _options.Smtp.EnableSsl
        };

        if (!string.IsNullOrWhiteSpace(_options.Smtp.User))
        {
            client.Credentials = new NetworkCredential(_options.Smtp.User, _options.Smtp.Password);
        }

        try
        {
            await client.SendMailAsync(message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {ToEmail}", toEmail);
            throw;
        }
    }
}
