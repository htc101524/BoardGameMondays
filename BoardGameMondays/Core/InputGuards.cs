namespace BoardGameMondays.Core;

internal static class InputGuards
{
    public static string RequireTrimmed(string? value, int maxLength, string paramName, string message)
    {
        var trimmed = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new ArgumentException(message, paramName);
        }

        if (trimmed.Length > maxLength)
        {
            throw new ArgumentException($"{message} (max {maxLength} characters).", paramName);
        }

        return trimmed;
    }

    public static string? OptionalTrimToNull(string? value, int maxLength, string paramName)
    {
        if (value is null)
        {
            return null;
        }

        var trimmed = value.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return null;
        }

        if (trimmed.Length > maxLength)
        {
            throw new ArgumentException($"Maximum length is {maxLength} characters.", paramName);
        }

        return trimmed;
    }

    public static string? OptionalHttpUrl(string? value, int maxLength, string paramName)
    {
        value = OptionalTrimToNull(value, maxLength, paramName);
        if (value is null)
        {
            return null;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("Must be an absolute http/https URL.", paramName);
        }

        return value;
    }

    public static string? OptionalRootRelativeOrHttpUrl(string? value, int maxLength, string paramName)
    {
        value = OptionalTrimToNull(value, maxLength, paramName);
        if (value is null)
        {
            return null;
        }

        if (value.StartsWith("/", StringComparison.Ordinal))
        {
            return value;
        }

        // Allow app-hosted relative paths like "images/..." and "uploads/...".
        if (value.StartsWith("images/", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("uploads/", StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return value;
        }

        throw new ArgumentException("Must be a root-relative path or an absolute http/https URL.", paramName);
    }
}
