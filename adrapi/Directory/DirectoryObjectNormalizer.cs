using adrapi.domain;
using adrapi.Entra;

namespace adrapi.Directory
{
    /// <summary>
    /// Normalizes <see cref="User"/> and <see cref="Group"/> instances to a
    /// consistent shape so v2 responses look the same regardless of the backend
    /// that produced them. Providers run their results through this before
    /// returning, giving consumers backend-agnostic objects.
    ///
    /// Rules:
    /// <list type="bullet">
    /// <item>String fields are trimmed; empty/whitespace becomes <c>null</c> (so
    /// "absent" is represented identically across backends).</item>
    /// <item>Entra ID objects carry no distinguished name, so <see cref="User.DN"/>/
    /// <see cref="Group.DN"/> are forced to <c>null</c> for that backend (the
    /// objectId in <c>ID</c> is the durable identifier).</item>
    /// <item>Every group reports a <see cref="Group.GroupType"/> — defaulting to
    /// <c>Security</c> when the backend did not specify one (e.g. LDAP/AD groups).</item>
    /// </list>
    /// </summary>
    public static class DirectoryObjectNormalizer
    {
        public static User Normalize(User user, DirectoryBackend backend)
        {
            if (user == null)
            {
                return null;
            }

            user.Name = Clean(user.Name);
            user.GivenName = Clean(user.GivenName);
            user.Surname = Clean(user.Surname);
            user.Login = Clean(user.Login);
            user.Account = Clean(user.Account);
            user.Description = Clean(user.Description);
            user.Mail = Clean(user.Mail);
            user.Mobile = Clean(user.Mobile);
            user.ID = Clean(user.ID);
            user.DN = Clean(user.DN);

            if (backend == DirectoryBackend.EntraId)
            {
                user.DN = null; // Entra ID has no distinguished name.
            }

            return user;
        }

        public static Group Normalize(Group group, DirectoryBackend backend)
        {
            if (group == null)
            {
                return null;
            }

            group.Name = Clean(group.Name);
            group.Description = Clean(group.Description);
            group.ID = Clean(group.ID);
            group.DN = Clean(group.DN);

            if (backend == DirectoryBackend.EntraId)
            {
                group.DN = null;
            }

            if (string.IsNullOrWhiteSpace(group.GroupType))
            {
                group.GroupType = GraphGroupMapper.KindSecurity;
            }

            return group;
        }

        private static string Clean(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return value.Trim();
        }
    }
}
