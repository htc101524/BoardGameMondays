using BoardGameMondays.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Memory;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace BoardGameMondays.Core;

public sealed class PwnedPasswordValidator : IPasswordValidator<ApplicationUser>
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMemoryCache _cache;
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<PwnedPasswordValidator> _logger;

    public PwnedPasswordValidator(
        IHttpClientFactory httpClientFactory,
        IMemoryCache cache,
        IConfiguration configuration,
        IWebHostEnvironment env,
        ILogger<PwnedPasswordValidator> logger)
    {
        _httpClientFactory = httpClientFactory;
        _cache = cache;
        _configuration = configuration;
        _env = env;
        _logger = logger;
    }

    public async Task<IdentityResult> ValidateAsync(UserManager<ApplicationUser> manager, ApplicationUser user, string? password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return IdentityResult.Failed(new IdentityError
            {
                Code = "PasswordRequired",
                Description = "Password is required."
            });
        }

        // Enabled by default in production, off by default in dev.
        var enabled = _configuration.GetValue<bool?>("Security:PwnedPasswords:Enabled")
            ?? !_env.IsDevelopment();

        // Default is fail-open to avoid blocking signups if the external service is unavailable.
        var failClosed = _configuration.GetValue<bool?>("Security:PwnedPasswords:FailClosed") ?? false;

        if (!enabled)
        {
            return IdentityResult.Success;
        }

        // Cheap local checks first.
        if (LooksVeryCommon(password))
        {
            return IdentityResult.Failed(new IdentityError
            {
                Code = "PasswordTooCommon",
                Description = "This password is too common. Choose a different password."
            });
        }

        if (!string.IsNullOrWhiteSpace(user?.UserName)
            && password.Contains(user.UserName, StringComparison.OrdinalIgnoreCase))
        {
            return IdentityResult.Failed(new IdentityError
            {
                Code = "PasswordContainsUsername",
                Description = "Password cannot contain your username."
            });
        }

        try
        {
            var sha1Hex = ComputeSha1Hex(password);
            if (_cache.TryGetValue(sha1Hex, out bool breached))
            {
                return breached ? BreachedResult() : IdentityResult.Success;
            }

            breached = await IsPwnedAsync(sha1Hex);
            _cache.Set(sha1Hex, breached, new MemoryCacheEntryOptions
            {
                SlidingExpiration = TimeSpan.FromHours(6),
                Size = 1
            });

            return breached ? BreachedResult() : IdentityResult.Success;
        }
        catch (Exception ex)
        {
            if (failClosed)
            {
                _logger.LogWarning(ex, "Pwned password check failed; blocking password due to fail-closed configuration.");
                return IdentityResult.Failed(new IdentityError
                {
                    Code = "PasswordCheckUnavailable",
                    Description = "Password safety check is temporarily unavailable. Please try again."
                });
            }

            _logger.LogWarning(ex, "Pwned password check failed; allowing password (fail-open). ");
            return IdentityResult.Success;
        }
    }

    private async Task<bool> IsPwnedAsync(string sha1Hex)
    {
        var prefix = sha1Hex[..5];
        var suffix = sha1Hex[5..];

        var client = _httpClientFactory.CreateClient("pwned-passwords");
        using var request = new HttpRequestMessage(HttpMethod.Get, $"range/{prefix}");

        using var response = await client.SendAsync(request);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync();
        // Response is lines of: HASH_SUFFIX:COUNT
        foreach (var line in body.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split(':', 2);
            if (parts.Length != 2)
            {
                continue;
            }

            if (string.Equals(parts[0], suffix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static IdentityResult BreachedResult()
        => IdentityResult.Failed(new IdentityError
        {
            Code = "PasswordPwned",
            Description = "This password appears in known data breaches. Choose a different password."
        });

    private static string ComputeSha1Hex(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = SHA1.HashData(bytes);
        return Convert.ToHexString(hash); // uppercase
    }

    private static bool LooksVeryCommon(string password)
    {
        // Tiny embedded denylist to catch the worst offenders quickly.
        // The HIBP check covers far more, but this helps even if it is disabled.
        return password.Length < 12
            || password.Equals("password", StringComparison.OrdinalIgnoreCase)
            || password.Equals("password123", StringComparison.OrdinalIgnoreCase)
            || password.Equals("Password123!", StringComparison.Ordinal)
            || password.Equals("qwerty", StringComparison.OrdinalIgnoreCase)
            || password.Equals("1234567890", StringComparison.Ordinal)
            || password.Equals("letmein", StringComparison.OrdinalIgnoreCase);
    }
}
