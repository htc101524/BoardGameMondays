using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace BoardGameMondays.Core
{
    public sealed class AdminRoleClaimsTransformation : IClaimsTransformation
    {
        private const string AdminRole = "Admin";

        private readonly IConfiguration _configuration;
        private readonly IHostEnvironment _environment;

        public AdminRoleClaimsTransformation(IConfiguration configuration, IHostEnvironment environment)
        {
            _configuration = configuration;
            _environment = environment;
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
                if (isConfiguredAdmin && !principal.IsInRole(AdminRole))
                {
                    AddAdminRoleClaim(principal);
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

            return Task.FromResult(principal);
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