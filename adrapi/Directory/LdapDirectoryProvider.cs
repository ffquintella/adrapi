using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using adrapi.domain;
using adrapi.Ldap;

namespace adrapi.Directory
{
    /// <summary>
    /// LDAP/AD-backed <see cref="IDirectoryProvider"/>. A thin adapter that
    /// delegates to the existing singleton managers, threading the domain's
    /// <see cref="LdapConfig"/> into every call (so it is domain-correct, per the
    /// multi-domain contract in AGENTS.md). This is the reference implementation
    /// proving the abstraction end-to-end against a real backend.
    /// </summary>
    public class LdapDirectoryProvider : IDirectoryProvider
    {
        private readonly LdapConfig config;

        public LdapDirectoryProvider(LdapConfig config)
        {
            this.config = config;
        }

        public string DomainKey => config?.DomainKey;
        public DirectoryBackend Backend => DirectoryBackend.Ldap;
        public bool SupportsOrganizationalUnits => true;

        private static User Normalize(User user) => DirectoryObjectNormalizer.Normalize(user, DirectoryBackend.Ldap);
        private static Group Normalize(Group group) => DirectoryObjectNormalizer.Normalize(group, DirectoryBackend.Ldap);

        // ---- Users ----
        public async Task<List<User>> GetUsersAsync(CancellationToken cancellationToken = default)
            => (await UserManager.Instance.GetUsersAsync(config)).Users.Select(Normalize).ToList();

        public async Task<User> GetUserAsync(string identifier, CancellationToken cancellationToken = default)
            => Normalize(await UserManager.Instance.GetUserAsync(identifier, "", config));

        public async Task<bool> UserExistsAsync(string identifier, CancellationToken cancellationToken = default)
            => await UserManager.Instance.GetUserAsync(identifier, "", config) != null;

        public async Task<List<User>> SearchUsersAsync(string query, CancellationToken cancellationToken = default)
            => ((await UserManager.Instance.GetListAsync("", query, "", config)).Users ?? new List<User>())
                .Select(Normalize).ToList();

        public async Task<bool> CreateUserAsync(User user, CancellationToken cancellationToken = default)
            => await UserManager.Instance.CreateUserAsync(user, config) == 0;

        public async Task<bool> UpdateUserAsync(User user, CancellationToken cancellationToken = default)
            => await UserManager.Instance.SaveUserAsync(user, config) == 0;

        public async Task<bool> DeleteUserAsync(User user, CancellationToken cancellationToken = default)
            => await UserManager.Instance.DeleteUser(user, config) == 0;

        public async Task<bool> SetUserEnabledAsync(string identifier, bool enabled, CancellationToken cancellationToken = default)
        {
            // Resolve to a DN first; SetAccountEnabledAsync operates on the entry in place.
            var user = await UserManager.Instance.GetUserAsync(identifier, "", config);
            if (user?.DN == null)
            {
                return false;
            }

            return await UserManager.Instance.SetAccountEnabledAsync(user.DN, enabled, config) == 0;
        }

        public async Task<bool> SetUserPasswordAsync(string identifier, string password, bool forceChangeAtNextLogin = true, CancellationToken cancellationToken = default)
        {
            var user = await UserManager.Instance.GetUserAsync(identifier, "", config);
            if (user?.DN == null)
            {
                return false;
            }

            // SaveUserAsync writes unicodePwd (requires LDAPS) when Password is set.
            user.Password = password;
            return await UserManager.Instance.SaveUserAsync(user, config) == 0;
        }

        // ---- Groups ----
        public async Task<List<Group>> GetGroupsAsync(CancellationToken cancellationToken = default)
            => (await GroupManager.Instance.GetGroupsAsync(config)).Select(Normalize).ToList();

        public async Task<Group> GetGroupAsync(string identifier, CancellationToken cancellationToken = default)
            => Normalize(await GroupManager.Instance.GetGroupAsync(identifier, config: config));

        public async Task<bool> GroupExistsAsync(string identifier, CancellationToken cancellationToken = default)
            => await GroupManager.Instance.GetGroupAsync(identifier, config: config) != null;

        public async Task<bool> CreateGroupAsync(Group group, CancellationToken cancellationToken = default)
            => await GroupManager.Instance.CreateGroupAsync(group, config) == 0;

        public async Task<bool> UpdateGroupAsync(Group group, CancellationToken cancellationToken = default)
            => await GroupManager.Instance.SaveGroupAsync(group, config) == 0;

        public async Task<bool> DeleteGroupAsync(Group group, CancellationToken cancellationToken = default)
            => await GroupManager.Instance.DeleteGroup(group, config) == 0;

        // ---- Group membership ----
        // Member identifiers are member DNs (the controller resolves account/CN→DN
        // up front). SaveGroupAsync rewrites the full member set, so the delta
        // operations read-modify-write the current membership.

        public async Task<List<string>> GetGroupMembersAsync(string groupId, CancellationToken cancellationToken = default)
        {
            var group = await GroupManager.Instance.GetGroupAsync(groupId, config: config);
            return group?.Member ?? new List<string>();
        }

        public Task<bool> AddGroupMembersAsync(string groupId, IEnumerable<string> memberIdentifiers, CancellationToken cancellationToken = default)
            => MutateMembersAsync(groupId, current =>
            {
                foreach (var m in memberIdentifiers ?? Enumerable.Empty<string>()) current.Add(m);
            });

        public Task<bool> RemoveGroupMembersAsync(string groupId, IEnumerable<string> memberIdentifiers, CancellationToken cancellationToken = default)
            => MutateMembersAsync(groupId, current =>
            {
                foreach (var m in memberIdentifiers ?? Enumerable.Empty<string>()) current.Remove(m);
            });

        public Task<bool> ReplaceGroupMembersAsync(string groupId, IEnumerable<string> memberIdentifiers, CancellationToken cancellationToken = default)
            => MutateMembersAsync(groupId, current =>
            {
                current.Clear();
                foreach (var m in memberIdentifiers ?? Enumerable.Empty<string>()) current.Add(m);
            });

        private async Task<bool> MutateMembersAsync(string groupId, Action<HashSet<string>> mutate)
        {
            var group = await GroupManager.Instance.GetGroupAsync(groupId, config: config);
            if (group == null)
            {
                return false;
            }

            var members = new HashSet<string>(group.Member, StringComparer.OrdinalIgnoreCase);
            mutate(members);
            group.Member = members.ToList();

            return await GroupManager.Instance.SaveGroupAsync(group, config) == 0;
        }

        // ---- Organizational Units ----
        public async Task<List<OU>> GetOrganizationalUnitsAsync(CancellationToken cancellationToken = default)
        {
            var dns = await OUManager.Instance.GetListAsync(config);
            var ous = new List<OU>(dns.Count);
            foreach (var dn in dns)
            {
                ous.Add(new OU { DN = dn });
            }

            return ous;
        }

        public Task<OU> GetOrganizationalUnitAsync(string dn, CancellationToken cancellationToken = default)
            => OUManager.Instance.GetOUAsync(dn, config);

        public async Task<bool> CreateOrganizationalUnitAsync(OU ou, CancellationToken cancellationToken = default)
            => await OUManager.Instance.CreateOUAsync(ou, config) == 0;

        public async Task<bool> UpdateOrganizationalUnitAsync(OU ou, CancellationToken cancellationToken = default)
            => await OUManager.Instance.SaveOUAsync(ou, config) == 0;

        public async Task<bool> DeleteOrganizationalUnitAsync(OU ou, CancellationToken cancellationToken = default)
            => await OUManager.Instance.DeleteOUAsync(ou, config) == 0;
    }
}
