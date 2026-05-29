using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using adrapi.domain;
using adrapi.domain.Exceptions;
using adrapi.Entra;

namespace adrapi.Directory
{
    /// <summary>
    /// Entra ID (Microsoft Graph)-backed <see cref="IDirectoryProvider"/>.
    ///
    /// Stage 4 implements the full user lifecycle over Graph (read/list/search/
    /// exists/create/update/disable/delete + password) via <see cref="GraphClient"/>
    /// and <see cref="GraphUserMapper"/>. Group/membership operations (Stage 5)
    /// still throw <see cref="NotSupportedException"/> until implemented.
    ///
    /// Organizational units are intentionally <b>never</b> supported: Entra ID has
    /// no OU object (administrative units are a separate Graph concept). See
    /// <c>roadmap.md</c> Stage 6.
    /// </summary>
    public class GraphDirectoryProvider : IDirectoryProvider
    {
        private readonly EntraConfig config;

        /// <summary>The reusable Graph client Stage 4/5 operations will issue requests through.</summary>
        public IGraphClient Graph { get; }

        public GraphDirectoryProvider(EntraConfig config, IGraphClient graphClient)
        {
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            Graph = graphClient ?? throw new ArgumentNullException(nameof(graphClient));
        }

        public string DomainKey => config.DomainKey;
        public DirectoryBackend Backend => DirectoryBackend.EntraId;
        public bool SupportsOrganizationalUnits => false;

        private static Task<T> NotYet<T>(string operation, int stage)
            => throw new NotSupportedException(
                $"{operation} over Microsoft Graph is not yet implemented (arrives in Entra ID Stage {stage}).");

        private static Task<T> OuUnsupported<T>()
            => throw new NotSupportedException(
                "Organizational units are not supported on Entra ID; it has no OU object. Use an LDAP/AD-backed domain for OU operations.");

        // ---- Users ----
        public async Task<List<User>> GetUsersAsync(CancellationToken cancellationToken = default)
        {
            var items = await Graph.GetPagedAsync($"users?$select={GraphUserMapper.SelectFields}", cancellationToken);
            return items.Select(GraphUserMapper.ToUser).ToList();
        }

        public async Task<User> GetUserAsync(string identifier, CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await Graph.GetAsync(UserUrl(identifier, withSelect: true), cancellationToken);
                return result.Body is { } body ? GraphUserMapper.ToUser(body) : null;
            }
            catch (GraphException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
        }

        public async Task<bool> UserExistsAsync(string identifier, CancellationToken cancellationToken = default)
        {
            try
            {
                await Graph.GetAsync($"users/{Escape(identifier)}?$select=id", cancellationToken);
                return true;
            }
            catch (GraphException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                return false;
            }
        }

        public async Task<List<User>> SearchUsersAsync(string query, CancellationToken cancellationToken = default)
        {
            var escaped = (query ?? string.Empty).Replace("'", "''");
            var filter =
                $"startswith(displayName,'{escaped}') or startswith(userPrincipalName,'{escaped}') or startswith(mailNickname,'{escaped}')";
            var url = $"users?$filter={Uri.EscapeDataString(filter)}&$select={GraphUserMapper.SelectFields}";

            var items = await Graph.GetPagedAsync(url, cancellationToken);
            return items.Select(GraphUserMapper.ToUser).ToList();
        }

        public async Task<bool> CreateUserAsync(User user, CancellationToken cancellationToken = default)
        {
            var result = await Graph.PostAsync("users", GraphUserMapper.ToCreateBody(user), cancellationToken);
            if (result.Body is { } body && body.TryGetProperty("id", out var id) && id.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                user.ID = id.GetString();
            }

            return true; // GraphClient throws on a non-success status.
        }

        public async Task<bool> UpdateUserAsync(User user, CancellationToken cancellationToken = default)
        {
            await Graph.PatchAsync($"users/{Escape(ResolveIdentifier(user))}", GraphUserMapper.ToUpdateBody(user), cancellationToken);
            return true;
        }

        public async Task<bool> DeleteUserAsync(User user, CancellationToken cancellationToken = default)
        {
            await Graph.DeleteAsync($"users/{Escape(ResolveIdentifier(user))}", cancellationToken);
            return true;
        }

        public async Task<bool> SetUserEnabledAsync(string identifier, bool enabled, CancellationToken cancellationToken = default)
        {
            await Graph.PatchAsync($"users/{Escape(identifier)}", new Dictionary<string, object> { ["accountEnabled"] = enabled }, cancellationToken);
            return true;
        }

        public async Task<bool> SetUserPasswordAsync(string identifier, string password, bool forceChangeAtNextLogin = true, CancellationToken cancellationToken = default)
        {
            var body = new Dictionary<string, object>
            {
                ["passwordProfile"] = new Dictionary<string, object>
                {
                    ["password"] = password,
                    ["forceChangePasswordNextSignIn"] = forceChangeAtNextLogin,
                },
            };
            await Graph.PatchAsync($"users/{Escape(identifier)}", body, cancellationToken);
            return true;
        }

        private static string Escape(string identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier))
            {
                throw new WrongParameterException("A user identifier (objectId or userPrincipalName) is required.");
            }

            return Uri.EscapeDataString(identifier);
        }

        private static string UserUrl(string identifier, bool withSelect)
            => withSelect
                ? $"users/{Escape(identifier)}?$select={GraphUserMapper.SelectFields}"
                : $"users/{Escape(identifier)}";

        // For update/delete adrapi uses the durable objectId when present, falling
        // back to the UPN (both are valid /users/{id-or-upn} keys in Graph).
        private static string ResolveIdentifier(User user)
        {
            if (user == null)
            {
                throw new WrongParameterException("User cannot be null.");
            }

            return !string.IsNullOrWhiteSpace(user.ID) ? user.ID : user.Login;
        }

        // ---- Groups (Stage 5) ----
        public Task<List<Group>> GetGroupsAsync(CancellationToken cancellationToken = default) => NotYet<List<Group>>("Listing groups", 5);
        public Task<Group> GetGroupAsync(string identifier, CancellationToken cancellationToken = default) => NotYet<Group>("Reading a group", 5);
        public Task<bool> CreateGroupAsync(Group group, CancellationToken cancellationToken = default) => NotYet<bool>("Creating a group", 5);
        public Task<bool> UpdateGroupAsync(Group group, CancellationToken cancellationToken = default) => NotYet<bool>("Updating a group", 5);
        public Task<bool> DeleteGroupAsync(Group group, CancellationToken cancellationToken = default) => NotYet<bool>("Deleting a group", 5);

        // ---- Organizational Units (not supported on Entra ID) ----
        public Task<List<OU>> GetOrganizationalUnitsAsync(CancellationToken cancellationToken = default) => OuUnsupported<List<OU>>();
        public Task<OU> GetOrganizationalUnitAsync(string dn, CancellationToken cancellationToken = default) => OuUnsupported<OU>();
        public Task<bool> CreateOrganizationalUnitAsync(OU ou, CancellationToken cancellationToken = default) => OuUnsupported<bool>();
        public Task<bool> UpdateOrganizationalUnitAsync(OU ou, CancellationToken cancellationToken = default) => OuUnsupported<bool>();
        public Task<bool> DeleteOrganizationalUnitAsync(OU ou, CancellationToken cancellationToken = default) => OuUnsupported<bool>();
    }
}
