using System.Collections.Generic;
using System.Threading.Tasks;
using Novell.Directory.Ldap;

namespace adrapi.Ldap
{
    /// <summary>
    /// The directory operations the object managers need from
    /// <see cref="LdapQueryManager"/>. Extracted so the managers can be exercised
    /// without a live directory: tests substitute an implementation through the
    /// <see cref="ObjectManager"/> seam instead of opening a connection.
    /// </summary>
    public interface ILdapQueryManager
    {
        Task<List<LdapEntry>> ExecuteSearchAsync(string searchBase, LdapSearchType type, string filter = "", LdapConfig config = null);

        Task<List<LdapEntry>> ExecuteSearchAsync(string searchBase, string filter = "", LdapConfig config = null);

        Task<List<LdapEntry>> ExecuteLimitedSearchAsync(string searchBase, LdapSearchType type, int start, int end, string filter = "", LdapConfig config = null);

        Task<List<LdapEntry>> ExecuteLimitedSearchAsync(string searchBase, string filter, int start, int end, LdapConfig config = null);

        Task<LdapPagedResponse> ExecutePagedSearchAsync(string searchBase, LdapSearchType type, string filter = "", string cookie = "", LdapConfig config = null);

        Task<LdapPagedResponse> ExecutePagedSearchAsync(string searchBase, string filter, string cookie = "", LdapConfig config = null);

        Task<LdapEntry> GetRegister(string DN, string[] attrs = null, LdapConfig config = null);

        Task AddEntryAsync(LdapEntry entry, LdapConfig config = null);

        Task DeleteEntry(string dn, LdapConfig config = null);

        Task SaveEntry(string dn, LdapModification[] modList, LdapConfig config = null);
    }

    /// <summary>
    /// Routes <see cref="ILdapQueryManager"/> to the production
    /// <see cref="LdapQueryManager"/> singleton. A thin adapter rather than an
    /// interface on the singleton itself, so the singleton stays untouched.
    /// </summary>
    public sealed class LdapQueryManagerAdapter : ILdapQueryManager
    {
        public static readonly LdapQueryManagerAdapter Instance = new LdapQueryManagerAdapter();

        private static LdapQueryManager Manager => LdapQueryManager.Instance;

        public Task<List<LdapEntry>> ExecuteSearchAsync(string searchBase, LdapSearchType type, string filter = "", LdapConfig config = null)
            => Manager.ExecuteSearchAsync(searchBase, type, filter, config);

        public Task<List<LdapEntry>> ExecuteSearchAsync(string searchBase, string filter = "", LdapConfig config = null)
            => Manager.ExecuteSearchAsync(searchBase, filter, config);

        public Task<List<LdapEntry>> ExecuteLimitedSearchAsync(string searchBase, LdapSearchType type, int start, int end, string filter = "", LdapConfig config = null)
            => Manager.ExecuteLimitedSearchAsync(searchBase, type, start, end, filter, config);

        public Task<List<LdapEntry>> ExecuteLimitedSearchAsync(string searchBase, string filter, int start, int end, LdapConfig config = null)
            => Manager.ExecuteLimitedSearchAsync(searchBase, filter, start, end, config);

        public Task<LdapPagedResponse> ExecutePagedSearchAsync(string searchBase, LdapSearchType type, string filter = "", string cookie = "", LdapConfig config = null)
            => Manager.ExecutePagedSearchAsync(searchBase, type, filter, cookie, config);

        public Task<LdapPagedResponse> ExecutePagedSearchAsync(string searchBase, string filter, string cookie = "", LdapConfig config = null)
            => Manager.ExecutePagedSearchAsync(searchBase, filter, cookie, config);

        public Task<LdapEntry> GetRegister(string DN, string[] attrs = null, LdapConfig config = null)
            => Manager.GetRegister(DN, attrs, config);

        public Task AddEntryAsync(LdapEntry entry, LdapConfig config = null)
            => Manager.AddEntryAsync(entry, config);

        public Task DeleteEntry(string dn, LdapConfig config = null)
            => Manager.DeleteEntry(dn, config);

        public Task SaveEntry(string dn, LdapModification[] modList, LdapConfig config = null)
            => Manager.SaveEntry(dn, modList, config);
    }

    /// <summary>
    /// Credential validation against a directory, as used by
    /// <see cref="UserManager.ValidateAuthenticationAsync"/>. Implemented by
    /// <see cref="LdapConnectionManager"/>; substitutable for tests.
    /// </summary>
    public interface ILdapAuthenticator
    {
        Task<bool> ValidateAuthenticationAsync(string login, string password, LdapConfig config = null);
    }

    /// <summary>Routes <see cref="ILdapAuthenticator"/> to <see cref="LdapConnectionManager"/>.</summary>
    public sealed class LdapConnectionAuthenticator : ILdapAuthenticator
    {
        public static readonly LdapConnectionAuthenticator Instance = new LdapConnectionAuthenticator();

        public Task<bool> ValidateAuthenticationAsync(string login, string password, LdapConfig config = null)
            => LdapConnectionManager.Instance.ValidateAuthenticationAsync(login, password, config);
    }
}
