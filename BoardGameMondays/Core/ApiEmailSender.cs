using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BoardGameMondays.Core;

public sealed class ApiEmailSender : IEmailSender
{
    private readonly EmailOptions _options;
    private readonly ILogger<ApiEmailSender> _logger;
    private readonly IHttpClientFactory _httpFactory;

    public ApiEmailSender(IOptions<EmailOptions> options, ILogger<ApiEmailSender> logger, IHttpClientFactory httpFactory)
    {
        _options = options.Value;
        _logger = logger;
        _httpFactory = httpFactory;
    }

    public async Task SendEmailAsync(string toEmail, string subject, string htmlBody)
    {
        if (string.IsNullOrWhiteSpace(_options.Api.BaseUrl))
        {
            _logger.LogWarning("Email not sent because API base URL is not configured. To={ToEmail}, Subject={Subject}", toEmail, subject);
            _logger.LogInformation("Email body (truncated): {Body}", htmlBody.Length > 500 ? htmlBody[..500] + "…" : htmlBody);
            return;
        }

        var client = _httpFactory.CreateClient("email-api");
        client.BaseAddress = new Uri(_options.Api.BaseUrl);
        if (!string.IsNullOrWhiteSpace(_options.Api.Token))
        {
            if (!string.IsNullOrWhiteSpace(_options.Api.TokenHeaderName))
            {
                client.DefaultRequestHeaders.Remove(_options.Api.TokenHeaderName);
                client.DefaultRequestHeaders.Add(_options.Api.TokenHeaderName, _options.Api.Token);
            }
            else
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                    _options.Api.AuthScheme ?? "Bearer",
                    _options.Api.Token);
            }
        }

        // Mailtrap Send API expects a from object and an array of recipients.
        var payload = new
        {
            from = new
            {
                email = _options.FromEmail,
                name = _options.FromName
            },
            to = new[]
            {
                new
                {
                    email = toEmail
                }
            },
            subject = subject,
            html = htmlBody
        };

        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            // POST to the configured URL. If the provider requires a specific path (e.g., /send),
            // include it in Email:Api:BaseUrl in configuration.
            var resp = await client.PostAsync(string.Empty, content);
            if (!resp.IsSuccessStatusCode)
            {
                var respText = await resp.Content.ReadAsStringAsync();
                _logger.LogError("Email API returned non-success status {Status} when sending to {ToEmail}: {Body}", resp.StatusCode, toEmail, respText);
                throw new EmailApiException($"Email API failed: {resp.StatusCode}", resp.StatusCode, respText);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email (API) to {ToEmail}", toEmail);
            throw;
        }
    }
}
