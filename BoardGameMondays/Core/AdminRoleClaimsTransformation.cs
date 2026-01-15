using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace BoardGameMondays.Core
{
    public sealed class AdminRoleClaimsTransformation : IClaimsTransformation
    {
        private const string AdminRole = "Admin";
        private const string RealAdminClaimType = "bgm:realAdmin";
        private const string RealAdminClaimValue = "1";
        private const string ViewAsNonAdminCookie = "bgm_viewAsNonAdmin";

        private readonly IConfiguration _configuration;
        private readonly IHostEnvironment _environment;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public AdminRoleClaimsTransformation(IConfiguration configuration, IHostEnvironment environment, IHttpContextAccessor httpContextAccessor)
        {
            _configuration = configuration;
            _environment = environment;
            _httpContextAccessor = httpContextAccessor;
        }

        public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
        {
            if (principal.Identity?.IsAuthenticated != true)
            {
                return Task.FromResult(principal);
            }

            var enforce = _configuration.GetValue<bool?>("Security:Admins:Enforce")
                ?? !_environment.IsDevelopment();

            var userName = principal.Identity?.Name;
            if (string.IsNullOrWhiteSpace(userName))
            {
                return Task.FromResult(principal);
            }

            var rawUserNames = _configuration.GetSection("Security:Admins:UserNames").Get<string[]>() ?? [];
            var isConfiguredAdmin = rawUserNames
                .Select(x => x?.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Any(x => string.Equals(x, userName, StringComparison.OrdinalIgnoreCase));

            if (!enforce)
            {
                var isAdmin = principal.IsInRole(AdminRole);

                if (isConfiguredAdmin && !isAdmin)
                {
                    AddAdminRoleClaim(principal);
                    isAdmin = true;
                }

                if (isAdmin || isConfiguredAdmin)
                {
                    EnsureRealAdminClaim(principal);
                }

                return Task.FromResult(principal);
            }

            if (!isConfiguredAdmin)
            {
                RemoveAdminRoleClaims(principal);
                return Task.FromResult(principal);
            }

            if (!principal.IsInRole(AdminRole))
            {
                AddAdminRoleClaim(principal);
            }

            // Tag real admins so the UI can show the toggle even when impersonating.
            EnsureRealAdminClaim(principal);

            return Task.FromResult(principal);
        }

        private bool ShouldViewAsNonAdmin()
        {
            var ctx = _httpContextAccessor.HttpContext;
            if (ctx is null)
            {
                return false;
            }

            return ctx.Request.Cookies.TryGetValue(ViewAsNonAdminCookie, out var v)
                   && string.Equals(v, "1", StringComparison.Ordinal);
        }

        private static void EnsureRealAdminClaim(ClaimsPrincipal principal)
        {
            var identity = principal.Identities.OfType<ClaimsIdentity>().FirstOrDefault(i => i.IsAuthenticated);
            if (identity is null)
            {
                return;
            }

            if (!identity.HasClaim(RealAdminClaimType, RealAdminClaimValue))
            {
                identity.AddClaim(new Claim(RealAdminClaimType, RealAdminClaimValue));
            }
        }

        private static void RemoveAdminRoleClaims(ClaimsPrincipal principal)
        {
            foreach (var identity in principal.Identities.OfType<ClaimsIdentity>())
            {
                var roleClaimType = identity.RoleClaimType;
                var toRemove = identity.FindAll(roleClaimType)
                    .Where(c => string.Equals(c.Value, AdminRole, StringComparison.OrdinalIgnoreCase))
                    .ToArray();

                foreach (var claim in toRemove)
                {
                    identity.RemoveClaim(claim);
                }
            }
        }

        private static void AddAdminRoleClaim(ClaimsPrincipal principal)
        {
            var identity = principal.Identities.OfType<ClaimsIdentity>().FirstOrDefault(i => i.IsAuthenticated);
            if (identity is null)
            {
                return;
            }

            identity.AddClaim(new Claim(identity.RoleClaimType, AdminRole));
        }
    }
}