using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using adrapi.domain;

namespace adrapi.Directory
{
    /// <summary>The directory backend kind behind a provider.</summary>
    public enum DirectoryBackend
    {
        Ldap,
        EntraId,
    }

    /// <summary>
    /// Backend-agnostic contract for directory operations, letting controllers
    /// target either on-prem LDAP/AD or Entra ID (Microsoft Graph) through one
    /// shape. A concrete provider is bound to a single domain and is selected per
    /// request (by the <c>{domain}</c> route segment) or per deployment (by the
    /// default domain's configured <c>kind</c>) via <see cref="DirectoryProviderFactory"/>.
    ///
    /// Identifiers are backend-relative: an LDAP provider expects a DN (or an
    /// account/CN it can resolve); a Graph provider expects an objectId/UPN.
    /// Normalising those at the edges is Stage 6 work.
    ///
    /// Booleans follow the manager convention: <c>true</c> on success, <c>false</c>
    /// on a handled failure. Operations a backend cannot perform throw
    /// <see cref="System.NotSupportedException"/> (e.g. OUs on Entra ID, which has
    /// no OU object).
    /// </summary>
    public interface IDirectoryProvider
    {
        /// <summary>Normalized domain key this provider serves.</summary>
        string DomainKey { get; }

        /// <summary>Which backend this provider talks to.</summary>
        DirectoryBackend Backend { get; }

        /// <summary>
        /// Whether organizational-unit operations are meaningful. False for
        /// Entra ID (no OU object — administrative units are a distinct concept).
        /// </summary>
        bool SupportsOrganizationalUnits { get; }

        // ---- Users ----
        Task<List<User>> GetUsersAsync(CancellationToken cancellationToken = default);
        Task<User> GetUserAsync(string identifier, CancellationToken cancellationToken = default);

        /// <summary>True when a user with the given identifier exists.</summary>
        Task<bool> UserExistsAsync(string identifier, CancellationToken cancellationToken = default);

        /// <summary>Prefix/substring search across a backend's natural name fields.</summary>
        Task<List<User>> SearchUsersAsync(string query, CancellationToken cancellationToken = default);

        Task<bool> CreateUserAsync(User user, CancellationToken cancellationToken = default);
        Task<bool> UpdateUserAsync(User user, CancellationToken cancellationToken = default);
        Task<bool> DeleteUserAsync(User user, CancellationToken cancellationToken = default);

        /// <summary>Enable or disable (block sign-in for) a user account.</summary>
        Task<bool> SetUserEnabledAsync(string identifier, bool enabled, CancellationToken cancellationToken = default);

        /// <summary>Set a user's password (requires a privileged backend credential).</summary>
        Task<bool> SetUserPasswordAsync(string identifier, string password, bool forceChangeAtNextLogin = true, CancellationToken cancellationToken = default);

        // ---- Groups ----
        Task<List<Group>> GetGroupsAsync(CancellationToken cancellationToken = default);
        Task<Group> GetGroupAsync(string identifier, CancellationToken cancellationToken = default);
        Task<bool> CreateGroupAsync(Group group, CancellationToken cancellationToken = default);
        Task<bool> UpdateGroupAsync(Group group, CancellationToken cancellationToken = default);
        Task<bool> DeleteGroupAsync(Group group, CancellationToken cancellationToken = default);

        // ---- Organizational Units (LDAP/AD only) ----
        Task<List<OU>> GetOrganizationalUnitsAsync(CancellationToken cancellationToken = default);
        Task<OU> GetOrganizationalUnitAsync(string dn, CancellationToken cancellationToken = default);
        Task<bool> CreateOrganizationalUnitAsync(OU ou, CancellationToken cancellationToken = default);
        Task<bool> UpdateOrganizationalUnitAsync(OU ou, CancellationToken cancellationToken = default);
        Task<bool> DeleteOrganizationalUnitAsync(OU ou, CancellationToken cancellationToken = default);
    }
}
