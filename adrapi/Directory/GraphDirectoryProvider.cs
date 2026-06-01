using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
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
    /// Implements the full user lifecycle (Stage 4) and group + membership
    /// management (Stage 5) over Graph via <see cref="GraphClient"/>,
    /// <see cref="GraphUserMapper"/>, and <see cref="GraphGroupMapper"/>.
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

        private static Task<T> OuUnsupported<T>()
            => throw new NotSupportedException(
                "Organizational units are not supported on Entra ID; it has no OU object. Use an LDAP/AD-backed domain for OU operations.");

        // ---- Users ----
        public async Task<List<User>> GetUsersAsync(CancellationToken cancellationToken = default)
        {
            var items = await Graph.GetPagedAsync($"users?$select={GraphUserMapper.SelectFields}", cancellationToken);
            return items.Select(GraphUserMapper.ToUser).Select(NormalizeUser).ToList();
        }

        public async Task<User> GetUserAsync(string identifier, CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await Graph.GetAsync(UserUrl(identifier, withSelect: true), cancellationToken);
                return result.Body is { } body ? NormalizeUser(GraphUserMapper.ToUser(body)) : null;
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
            return items.Select(GraphUserMapper.ToUser).Select(NormalizeUser).ToList();
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

            // Reject LDAP DNs at the edge — Graph addresses by objectId/UPN only.
            DirectoryIdentifiers.EnsureGraphAddressable(identifier);
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

        // ---- Groups ----
        public async Task<List<Group>> GetGroupsAsync(CancellationToken cancellationToken = default)
        {
            var items = await Graph.GetPagedAsync($"groups?$select={GraphGroupMapper.SelectFields}", cancellationToken);
            return items.Select(GraphGroupMapper.ToGroup).Select(NormalizeGroup).ToList();
        }

        public async Task<Group> GetGroupAsync(string identifier, CancellationToken cancellationToken = default)
        {
            try
            {
                var groupId = await ResolveGroupIdAsync(identifier, cancellationToken);
                if (groupId == null)
                {
                    return null;
                }

                var result = await Graph.GetAsync($"groups/{Escape(groupId)}?$select={GraphGroupMapper.SelectFields}", cancellationToken);
                return result.Body is { } body ? NormalizeGroup(GraphGroupMapper.ToGroup(body)) : null;
            }
            catch (GraphException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
        }

        public async Task<bool> GroupExistsAsync(string identifier, CancellationToken cancellationToken = default)
            => await ResolveGroupIdAsync(identifier, cancellationToken) != null;

        public async Task<bool> CreateGroupAsync(Group group, CancellationToken cancellationToken = default)
        {
            var result = await Graph.PostAsync("groups", GraphGroupMapper.ToCreateBody(group), cancellationToken);
            if (result.Body is { } body && body.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
            {
                group.ID = id.GetString();
            }

            // Initial members, if any, are added after creation (uniform with delta adds).
            if (group.Member is { Count: > 0 } && group.ID != null)
            {
                await AddGroupMembersAsync(group.ID, group.Member, cancellationToken);
            }

            return true;
        }

        public async Task<bool> UpdateGroupAsync(Group group, CancellationToken cancellationToken = default)
        {
            var groupId = ResolveGroupIdentifier(group);
            await Graph.PatchAsync($"groups/{Escape(groupId)}", GraphGroupMapper.ToUpdateBody(group), cancellationToken);
            return true;
        }

        public async Task<bool> DeleteGroupAsync(Group group, CancellationToken cancellationToken = default)
        {
            var groupId = ResolveGroupIdentifier(group);
            await Graph.DeleteAsync($"groups/{Escape(groupId)}", cancellationToken);
            return true;
        }

        // ---- Group membership ----
        public async Task<List<string>> GetGroupMembersAsync(string groupId, CancellationToken cancellationToken = default)
        {
            var id = await RequireGroupIdAsync(groupId, cancellationToken);
            var items = await Graph.GetPagedAsync(
                $"groups/{Escape(id)}/members?$select=id,userPrincipalName,displayName", cancellationToken);

            // Prefer the human-friendly UPN (users); fall back to objectId (groups, devices, ...).
            return items
                .Select(m =>
                    m.TryGetProperty("userPrincipalName", out var upn) && upn.ValueKind == JsonValueKind.String
                        ? upn.GetString()
                        : (m.TryGetProperty("id", out var oid) && oid.ValueKind == JsonValueKind.String ? oid.GetString() : null))
                .Where(v => !string.IsNullOrEmpty(v))
                .ToList();
        }

        public async Task<bool> AddGroupMembersAsync(string groupId, IEnumerable<string> memberIdentifiers, CancellationToken cancellationToken = default)
        {
            var id = await RequireGroupIdAsync(groupId, cancellationToken);
            foreach (var member in memberIdentifiers ?? Enumerable.Empty<string>())
            {
                var objectId = await ResolveMemberObjectIdAsync(member, cancellationToken);
                var body = new Dictionary<string, object>
                {
                    ["@odata.id"] = $"{GraphBaseUrl()}/directoryObjects/{objectId}",
                };

                try
                {
                    await Graph.PostAsync($"groups/{Escape(id)}/members/$ref", body, cancellationToken);
                }
                catch (GraphException ex) when (IsAlreadyMember(ex))
                {
                    // Idempotent: the member is already in the group.
                }
            }

            return true;
        }

        public async Task<bool> RemoveGroupMembersAsync(string groupId, IEnumerable<string> memberIdentifiers, CancellationToken cancellationToken = default)
        {
            var id = await RequireGroupIdAsync(groupId, cancellationToken);
            foreach (var member in memberIdentifiers ?? Enumerable.Empty<string>())
            {
                var objectId = await ResolveMemberObjectIdAsync(member, cancellationToken);
                try
                {
                    await Graph.DeleteAsync($"groups/{Escape(id)}/members/{Escape(objectId)}/$ref", cancellationToken);
                }
                catch (GraphException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
                {
                    // Idempotent: the member was not in the group.
                }
            }

            return true;
        }

        public async Task<bool> ReplaceGroupMembersAsync(string groupId, IEnumerable<string> memberIdentifiers, CancellationToken cancellationToken = default)
        {
            var id = await RequireGroupIdAsync(groupId, cancellationToken);

            // Desired set, resolved to durable objectIds.
            var desired = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var member in memberIdentifiers ?? Enumerable.Empty<string>())
            {
                desired.Add(await ResolveMemberObjectIdAsync(member, cancellationToken));
            }

            // Current set as objectIds.
            var currentItems = await Graph.GetPagedAsync($"groups/{Escape(id)}/members?$select=id", cancellationToken);
            var current = new HashSet<string>(
                currentItems
                    .Where(m => m.TryGetProperty("id", out var oid) && oid.ValueKind == JsonValueKind.String)
                    .Select(m => m.GetProperty("id").GetString()),
                StringComparer.OrdinalIgnoreCase);

            var toAdd = desired.Where(d => !current.Contains(d)).ToList();
            var toRemove = current.Where(c => !desired.Contains(c)).ToList();

            // Already objectIds — go straight to the $ref endpoints.
            if (toRemove.Count > 0) await RemoveGroupMembersAsync(id, toRemove, cancellationToken);
            if (toAdd.Count > 0) await AddGroupMembersAsync(id, toAdd, cancellationToken);

            return true;
        }

        private static User NormalizeUser(User user) => DirectoryObjectNormalizer.Normalize(user, DirectoryBackend.EntraId);
        private static Group NormalizeGroup(Group group) => DirectoryObjectNormalizer.Normalize(group, DirectoryBackend.EntraId);

        private string GraphBaseUrl() => (config.GraphBaseUrl ?? EntraConfig.DefaultGraphBaseUrl).TrimEnd('/');

        private static bool IsAlreadyMember(GraphException ex)
            => ex.StatusCode == HttpStatusCode.BadRequest
               && ex.ResponseBody != null
               && ex.ResponseBody.IndexOf("already exist", StringComparison.OrdinalIgnoreCase) >= 0;

        // A group key is its objectId; a non-GUID identifier is treated as a
        // displayName and resolved via $filter. Returns null when not found.
        private async Task<string> ResolveGroupIdAsync(string identifier, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(identifier))
            {
                throw new WrongParameterException("A group identifier (objectId or displayName) is required.");
            }

            DirectoryIdentifiers.EnsureGraphAddressable(identifier);

            if (DirectoryIdentifiers.IsObjectId(identifier))
            {
                return identifier;
            }

            var escaped = identifier.Replace("'", "''");
            var filter = $"displayName eq '{escaped}'";
            var url = $"groups?$filter={Uri.EscapeDataString(filter)}&$select=id";
            var matches = await Graph.GetPagedAsync(url, cancellationToken);
            var first = matches.FirstOrDefault();
            return first.ValueKind == JsonValueKind.Object && first.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String
                ? id.GetString()
                : null;
        }

        private async Task<string> RequireGroupIdAsync(string identifier, CancellationToken cancellationToken)
            => await ResolveGroupIdAsync(identifier, cancellationToken)
               ?? throw new GraphException($"Group '{identifier}' was not found.", HttpStatusCode.NotFound);

        private static string ResolveGroupIdentifier(Group group)
        {
            if (group == null)
            {
                throw new WrongParameterException("Group cannot be null.");
            }

            if (!string.IsNullOrWhiteSpace(group.ID)) return group.ID;
            if (!string.IsNullOrWhiteSpace(group.Name)) return group.Name;
            throw new WrongParameterException("Group must have an ID (objectId) or Name (displayName).");
        }

        // Members are added by objectId. A GUID is used as-is; anything else is
        // treated as a user UPN and resolved to its objectId.
        private async Task<string> ResolveMemberObjectIdAsync(string identifier, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(identifier))
            {
                throw new WrongParameterException("A member identifier (objectId or userPrincipalName) is required.");
            }

            DirectoryIdentifiers.EnsureGraphAddressable(identifier);

            if (DirectoryIdentifiers.IsObjectId(identifier))
            {
                return identifier;
            }

            var result = await Graph.GetAsync($"users/{Escape(identifier)}?$select=id", cancellationToken);
            if (result.Body is { } body && body.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
            {
                return id.GetString();
            }

            throw new GraphException($"Member '{identifier}' could not be resolved to an objectId.", HttpStatusCode.NotFound);
        }

        // ---- Organizational Units (not supported on Entra ID) ----
        public Task<List<OU>> GetOrganizationalUnitsAsync(CancellationToken cancellationToken = default) => OuUnsupported<List<OU>>();
        public Task<OU> GetOrganizationalUnitAsync(string dn, CancellationToken cancellationToken = default) => OuUnsupported<OU>();
        public Task<bool> CreateOrganizationalUnitAsync(OU ou, CancellationToken cancellationToken = default) => OuUnsupported<bool>();
        public Task<bool> UpdateOrganizationalUnitAsync(OU ou, CancellationToken cancellationToken = default) => OuUnsupported<bool>();
        public Task<bool> DeleteOrganizationalUnitAsync(OU ou, CancellationToken cancellationToken = default) => OuUnsupported<bool>();
    }
}
