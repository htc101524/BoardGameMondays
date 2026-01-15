namespace BoardGameMondays.Core;

public sealed class EmailOptions
{
    public string FromName { get; set; } = "Board Game Mondays";
    public string FromEmail { get; set; } = "no-reply@boardgamemondays.local";
    public SmtpOptions Smtp { get; set; } = new();
    public ApiOptions Api { get; set; } = new();
    public bool UseApi { get; set; } = false;

    public sealed class SmtpOptions
    {
        public string Host { get; set; } = string.Empty;
        public int Port { get; set; } = 587;
        public string User { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public bool EnableSsl { get; set; } = true;
    }

    public sealed class ApiOptions
    {
        public string BaseUrl { get; set; } = string.Empty;
        public string Token { get; set; } = string.Empty;
        // Optional: allow overriding the Authorization scheme (e.g., "Bearer").
        public string AuthScheme { get; set; } = "Bearer";
    }
}
