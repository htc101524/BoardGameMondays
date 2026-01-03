using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace BoardGameMondays.Core
{
    // Very small, in-memory auth provider for demo/login UI only.
    public class SimpleAuthStateProvider : AuthenticationStateProvider
    {
        private ClaimsPrincipal _anonymous = new ClaimsPrincipal(new ClaimsIdentity());
        private ClaimsPrincipal _current = null!;

        public override Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            if (_current == null)
                return Task.FromResult(new AuthenticationState(_anonymous));

            return Task.FromResult(new AuthenticationState(_current));
        }

        public Task MarkUserAsAuthenticated(string userName, bool isAdmin = false)
        {
            var claims = new List<Claim> { new Claim(ClaimTypes.Name, userName) };
            if (isAdmin)
            {
                claims.Add(new Claim(ClaimTypes.Role, "Admin"));
            }

            var identity = new ClaimsIdentity(claims, "SimpleAuth");
            _current = new ClaimsPrincipal(identity);
            NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(_current)));
            return Task.CompletedTask;
        }

        public Task MarkUserAsLoggedOut()
        {
            _current = _anonymous;
            NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(_anonymous)));
            return Task.CompletedTask;
        }
    }
}
