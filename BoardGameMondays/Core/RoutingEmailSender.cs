using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace BoardGameMondays.Core;

public sealed class RoutingEmailSender : IEmailSender
{
    private readonly IOptions<EmailOptions> _options;
    private readonly ApiEmailSender _apiSender;
    private readonly SmtpEmailSender _smtpSender;

    public RoutingEmailSender(IOptions<EmailOptions> options, ApiEmailSender apiSender, SmtpEmailSender smtpSender)
    {
        _options = options;
        _apiSender = apiSender;
        _smtpSender = smtpSender;
    }

    public Task SendEmailAsync(string toEmail, string subject, string htmlBody)
    {
        var useApi = _options.Value.UseApi;
        var hasApiBaseUrl = !string.IsNullOrWhiteSpace(_options.Value.Api.BaseUrl);

        if (useApi || hasApiBaseUrl)
        {
            return _apiSender.SendEmailAsync(toEmail, subject, htmlBody);
        }

        return _smtpSender.SendEmailAsync(toEmail, subject, htmlBody);
    }
}
