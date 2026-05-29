using System.Collections.Generic;
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

        // ---- Users ----
        public async Task<List<User>> GetUsersAsync(CancellationToken cancellationToken = default)
            => (await UserManager.Instance.GetUsersAsync(config)).Users;

        public Task<User> GetUserAsync(string identifier, CancellationToken cancellationToken = default)
            => UserManager.Instance.GetUserAsync(identifier, "", config);

        public async Task<bool> UserExistsAsync(string identifier, CancellationToken cancellationToken = default)
            => await UserManager.Instance.GetUserAsync(identifier, "", config) != null;

        public async Task<List<User>> SearchUsersAsync(string query, CancellationToken cancellationToken = default)
            => (await UserManager.Instance.GetListAsync("", query, "", config)).Users ?? new List<User>();

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
        public Task<List<Group>> GetGroupsAsync(CancellationToken cancellationToken = default)
            => GroupManager.Instance.GetGroupsAsync(config);

        public Task<Group> GetGroupAsync(string identifier, CancellationToken cancellationToken = default)
            => GroupManager.Instance.GetGroupAsync(identifier, config: config);

        public async Task<bool> CreateGroupAsync(Group group, CancellationToken cancellationToken = default)
            => await GroupManager.Instance.CreateGroupAsync(group, config) == 0;

        public async Task<bool> UpdateGroupAsync(Group group, CancellationToken cancellationToken = default)
            => await GroupManager.Instance.SaveGroupAsync(group, config) == 0;

        public async Task<bool> DeleteGroupAsync(Group group, CancellationToken cancellationToken = default)
            => await GroupManager.Instance.DeleteGroup(group, config) == 0;

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
