using System;
using System.Linq;
using adrapi.domain.Exceptions;

namespace adrapi.Directory
{
    /// <summary>The shape of a directory object identifier.</summary>
    public enum DirectoryIdentifierKind
    {
        /// <summary>An LDAP/AD distinguished name, e.g. <c>CN=Ada,OU=People,DC=corp,DC=example</c>.</summary>
        DistinguishedName,

        /// <summary>An Entra ID objectId (a GUID).</summary>
        ObjectId,

        /// <summary>A userPrincipalName, e.g. <c>ada@contoso.com</c>.</summary>
        UserPrincipalName,

        /// <summary>A bare name / account (sAMAccountName, CN, displayName, ...).</summary>
        Name,
    }

    /// <summary>
    /// Classifies and validates directory identifiers at the backend edge so each
    /// backend only receives identifier forms it can actually address.
    ///
    /// The two backends use disjoint addressing schemes:
    /// <list type="bullet">
    /// <item><b>LDAP/AD</b> addresses by distinguished name (or resolvable account/CN).</item>
    /// <item><b>Entra ID</b> addresses by objectId (GUID) or userPrincipalName — it has
    /// no DN. A DN cannot be translated into a Graph key, so passing one to the
    /// Graph backend is rejected with a clear error rather than yielding an opaque 404.</item>
    /// </list>
    /// </summary>
    public static class DirectoryIdentifiers
    {
        // RDN attribute prefixes that mark a string as a distinguished name.
        private static readonly string[] DnPrefixes = { "cn=", "ou=", "dc=", "o=", "uid=" };

        public static bool IsObjectId(string identifier)
            => !string.IsNullOrWhiteSpace(identifier) && Guid.TryParse(identifier.Trim(), out _);

        public static bool IsDistinguishedName(string identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier))
            {
                return false;
            }

            var trimmed = identifier.TrimStart();
            return identifier.Contains('=')
                && DnPrefixes.Any(p => trimmed.StartsWith(p, StringComparison.OrdinalIgnoreCase));
        }

        public static bool IsUserPrincipalName(string identifier)
            => !string.IsNullOrWhiteSpace(identifier)
               && identifier.Contains('@')
               && !IsDistinguishedName(identifier);

        public static DirectoryIdentifierKind Classify(string identifier)
        {
            if (IsObjectId(identifier)) return DirectoryIdentifierKind.ObjectId;
            if (IsDistinguishedName(identifier)) return DirectoryIdentifierKind.DistinguishedName;
            if (IsUserPrincipalName(identifier)) return DirectoryIdentifierKind.UserPrincipalName;
            return DirectoryIdentifierKind.Name;
        }

        /// <summary>
        /// Throws when <paramref name="identifier"/> cannot be addressed by the
        /// Entra ID/Graph backend (i.e. it is an LDAP distinguished name).
        /// </summary>
        public static void EnsureGraphAddressable(string identifier)
        {
            if (IsDistinguishedName(identifier))
            {
                throw new WrongParameterException(
                    $"'{identifier}' is an LDAP distinguished name; the Entra ID backend addresses objects by objectId or userPrincipalName.");
            }
        }
    }
}
