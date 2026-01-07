using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;

namespace BoardGameMondays.Core;

public sealed class CircuitAuthStateProvider : AuthenticationStateProvider
{
    private static readonly ClaimsPrincipal Anonymous = new(new ClaimsIdentity());

    private readonly IHttpContextAccessor _httpContextAccessor;
    private ClaimsPrincipal? _cachedUser;

    public CircuitAuthStateProvider(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;

        // During the initial HTTP request (prerender / circuit bootstrapping), HttpContext is available.
        // Capture the user so subsequent interactive calls (where HttpContext is typically null) still
        // return the correct principal for the lifetime of this circuit.
        _cachedUser = httpContextAccessor.HttpContext?.User;
    }

    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var user = _cachedUser;
        if (user is null)
        {
            user = _httpContextAccessor.HttpContext?.User;
            if (user is not null)
            {
                _cachedUser = user;
            }
        }

        return Task.FromResult(new AuthenticationState(user ?? Anonymous));
    }
}
