using System;
using System.Collections.Generic;
using System.Linq;

namespace adrapi.Entra
{
    /// <summary>
    /// Maps adrapi authorization policies onto the Microsoft Graph **application
    /// permissions** (app roles) required when a domain is backed by Entra ID.
    ///
    /// Client-credentials always requests the <c>.default</c> scope, so the
    /// effective rights are whatever application permissions the app registration
    /// has been granted admin consent for. This map declares which app roles each
    /// policy needs and validates that the configured grants cover them — the
    /// closest equivalent to the on-prem `Reading`/`Writting` distinction.
    ///
    /// A <c>*.ReadWrite.All</c> role also satisfies the matching <c>*.Read.All</c>.
    /// </summary>
    public static class EntraScopeMap
    {
        public const string Reading = "Reading";
        public const string Writting = "Writting";

        /// <summary>App roles required to satisfy the <c>Reading</c> policy.</summary>
        public static readonly IReadOnlyList<string> ReadRoles = new[]
        {
            "User.Read.All",
            "Group.Read.All",
            "GroupMember.Read.All",
        };

        /// <summary>App roles required to satisfy the <c>Writting</c> policy.</summary>
        public static readonly IReadOnlyList<string> WriteRoles = new[]
        {
            "User.ReadWrite.All",
            "Group.ReadWrite.All",
            "GroupMember.ReadWrite.All",
        };

        public static IReadOnlyList<string> RequiredRoles(string policy) => policy switch
        {
            Writting => WriteRoles,
            Reading => ReadRoles,
            _ => throw new ArgumentException($"Unknown authorization policy '{policy}'.", nameof(policy)),
        };

        /// <summary>The OAuth2 scope used for client-credentials Graph access.</summary>
        public static string DefaultScope => EntraConfig.DefaultScope;

        // A *.ReadWrite.All grant implicitly covers the corresponding *.Read.All.
        private static IEnumerable<string> Expand(string role)
        {
            yield return role;
            const string rw = ".ReadWrite.All";
            if (!string.IsNullOrEmpty(role) && role.EndsWith(rw, StringComparison.OrdinalIgnoreCase))
            {
                yield return role.Substring(0, role.Length - rw.Length) + ".Read.All";
            }
        }

        private static HashSet<string> ExpandGranted(IEnumerable<string> grantedPermissions)
            => new(
                (grantedPermissions ?? Enumerable.Empty<string>())
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .SelectMany(Expand),
                StringComparer.OrdinalIgnoreCase);

        /// <summary>True when the granted permissions cover everything the policy needs.</summary>
        public static bool IsPolicySatisfied(IEnumerable<string> grantedPermissions, string policy)
        {
            var granted = ExpandGranted(grantedPermissions);
            return RequiredRoles(policy).All(granted.Contains);
        }

        /// <summary>Roles the policy needs that are not covered by the grants.</summary>
        public static IReadOnlyList<string> MissingRoles(IEnumerable<string> grantedPermissions, string policy)
        {
            var granted = ExpandGranted(grantedPermissions);
            return RequiredRoles(policy).Where(r => !granted.Contains(r)).ToList();
        }
    }
}
