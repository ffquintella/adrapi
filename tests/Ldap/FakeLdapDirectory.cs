using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using adrapi;
using adrapi.Ldap;
using Novell.Directory.Ldap;

namespace tests.Ldap
{
    /// <summary>
    /// An in-memory directory standing in for a live LDAP server. Entries are
    /// plain <see cref="LdapEntry"/> objects; searches are matched by object
    /// class and a simple "attribute=value" filter, which is all the managers
    /// ever send. Writes are recorded so tests can assert on them.
    /// </summary>
    public sealed class FakeLdapQueryManager : ILdapQueryManager
    {
        public readonly List<LdapEntry> Entries = new();
        public readonly List<string> Calls = new();
        public readonly List<LdapEntry> Added = new();
        public readonly List<string> Deleted = new();
        public readonly List<(string dn, LdapModification[] mods)> Saved = new();

        /// <summary>Page size for <see cref="ExecutePagedSearchAsync(string,string,string,LdapConfig)"/>.</summary>
        public int PageSize = 2;

        /// <summary>When set, every operation throws it (error-path tests).</summary>
        public Exception Throw;

        /// <summary>Entry returned by <see cref="GetRegister"/> regardless of DN; null means look it up.</summary>
        public LdapEntry RegisterOverride;

        /// <summary>
        /// Entry returned by <see cref="GetRegister"/> only when the caller asks
        /// for a ranged attribute (<c>memberOf;range=...</c>), which is how the
        /// managers walk a large membership list.
        /// </summary>
        public LdapEntry RangedRegisterOverride;

        public void Add(LdapEntry entry) => Entries.Add(entry);

        private void Record(string call)
        {
            Calls.Add(call);
            if (Throw != null) throw Throw;
        }

        // ---- Searching ----

        public Task<List<LdapEntry>> ExecuteSearchAsync(string searchBase, LdapSearchType type, string filter = "", LdapConfig config = null)
        {
            Record($"Search:{type}:{filter}");
            return Task.FromResult(Match(type, filter));
        }

        public Task<List<LdapEntry>> ExecuteSearchAsync(string searchBase, string filter = "", LdapConfig config = null)
        {
            Record($"SearchRaw:{filter}");
            return Task.FromResult(MatchRaw(filter));
        }

        public Task<List<LdapEntry>> ExecuteLimitedSearchAsync(string searchBase, LdapSearchType type, int start, int end, string filter = "", LdapConfig config = null)
        {
            Record($"Limited:{type}:{start}-{end}:{filter}");
            var all = Match(type, filter);
            var skip = Math.Max(start - 1, 0);
            var take = end >= start ? end - start + 1 : all.Count;
            return Task.FromResult(all.Skip(skip).Take(take).ToList());
        }

        public Task<List<LdapEntry>> ExecuteLimitedSearchAsync(string searchBase, string filter, int start, int end, LdapConfig config = null)
        {
            Record($"LimitedRaw:{start}-{end}:{filter}");
            var all = MatchRaw(filter);
            var skip = Math.Max(start - 1, 0);
            var take = end >= start ? end - start + 1 : all.Count;
            return Task.FromResult(all.Skip(skip).Take(take).ToList());
        }

        public Task<LdapPagedResponse> ExecutePagedSearchAsync(string searchBase, LdapSearchType type, string filter = "", string cookie = "", LdapConfig config = null)
        {
            Record($"Paged:{type}:{filter}:{cookie}");
            return Task.FromResult(Page(Match(type, filter), cookie));
        }

        public Task<LdapPagedResponse> ExecutePagedSearchAsync(string searchBase, string filter, string cookie = "", LdapConfig config = null)
        {
            Record($"PagedRaw:{filter}:{cookie}");
            return Task.FromResult(Page(MatchRaw(filter), cookie));
        }

        /// <summary>Slices results into pages, using the offset as the cookie.</summary>
        private LdapPagedResponse Page(List<LdapEntry> all, string cookie)
        {
            var offset = int.TryParse(cookie, out var parsed) ? parsed : 0;
            var slice = all.Skip(offset).Take(PageSize).ToList();
            var next = offset + slice.Count;

            return new LdapPagedResponse
            {
                Entries = slice,
                Cookie = next >= all.Count ? "" : next.ToString()
            };
        }

        public Task<LdapEntry> GetRegister(string DN, string[] attrs = null, LdapConfig config = null)
        {
            Record($"GetRegister:{DN}");

            var ranged = attrs?.Any(a => a != null && a.Contains(";range=")) == true;
            if (ranged && RangedRegisterOverride != null) return Task.FromResult(RangedRegisterOverride);
            if (RegisterOverride != null) return Task.FromResult(RegisterOverride);

            var found = Entries.FirstOrDefault(e => string.Equals(e.Dn, DN, StringComparison.OrdinalIgnoreCase));
            if (found == null) throw new LdapException("No such object", LdapException.NoSuchObject, null);

            return Task.FromResult(found);
        }

        // ---- Writing ----

        public Task AddEntryAsync(LdapEntry entry, LdapConfig config = null)
        {
            Record($"Add:{entry.Dn}");
            Added.Add(entry);
            Entries.Add(entry);
            return Task.CompletedTask;
        }

        public Task DeleteEntry(string dn, LdapConfig config = null)
        {
            Record($"Delete:{dn}");
            Deleted.Add(dn);
            Entries.RemoveAll(e => string.Equals(e.Dn, dn, StringComparison.OrdinalIgnoreCase));
            return Task.CompletedTask;
        }

        public Task SaveEntry(string dn, LdapModification[] modList, LdapConfig config = null)
        {
            Record($"Save:{dn}");
            Saved.Add((dn, modList));
            return Task.CompletedTask;
        }

        // ---- Matching ----

        private List<LdapEntry> Match(LdapSearchType type, string filter)
        {
            var objectClass = type switch
            {
                LdapSearchType.User => "user",
                LdapSearchType.Group => "group",
                LdapSearchType.OU => "organizationalUnit",
                LdapSearchType.Machine => "computer",
                _ => null
            };

            var candidates = Entries.Where(e => objectClass == null || HasObjectClass(e, objectClass));
            return ApplyFilter(candidates, filter).ToList();
        }

        /// <summary>Raw filters arrive as "(&amp;(objectClass=x)(attr=value))" or "attr=value".</summary>
        private List<LdapEntry> MatchRaw(string filter)
        {
            var candidates = Entries.AsEnumerable();

            foreach (var term in Terms(filter))
            {
                var separator = term.IndexOf('=');
                if (separator <= 0) continue;

                var name = term.Substring(0, separator);
                var value = term.Substring(separator + 1);

                candidates = name.Equals("objectClass", StringComparison.OrdinalIgnoreCase)
                    ? candidates.Where(e => HasObjectClass(e, value))
                    : candidates.Where(e => ValueMatches(e, name, value));
            }

            return candidates.ToList();
        }

        private static IEnumerable<string> Terms(string filter)
        {
            if (string.IsNullOrWhiteSpace(filter)) yield break;

            foreach (var raw in filter.Split(new[] { '(', ')', '&' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var term = raw.Trim();
                if (term.Length > 0 && term.Contains('=')) yield return term;
            }
        }

        private static IEnumerable<LdapEntry> ApplyFilter(IEnumerable<LdapEntry> entries, string filter)
        {
            if (string.IsNullOrWhiteSpace(filter)) return entries;

            var separator = filter.IndexOf('=');
            if (separator <= 0) return entries;

            var name = filter.Substring(0, separator);
            var value = filter.Substring(separator + 1);
            return entries.Where(e => ValueMatches(e, name, value));
        }

        private static bool ValueMatches(LdapEntry entry, string attribute, string value)
        {
            var attr = entry.GetAttributeSet()
                .FirstOrDefault(a => string.Equals(a.Key, attribute, StringComparison.OrdinalIgnoreCase));
            if (attr.Value == null) return false;

            var pattern = value.Trim();
            if (pattern == "*") return true;

            return attr.Value.StringValueArray.Any(v =>
                pattern.EndsWith("*", StringComparison.Ordinal)
                    ? v.StartsWith(pattern.TrimEnd('*'), StringComparison.OrdinalIgnoreCase)
                    : string.Equals(v, pattern, StringComparison.OrdinalIgnoreCase));
        }

        private static bool HasObjectClass(LdapEntry entry, string objectClass)
            => ValueMatches(entry, "objectclass", objectClass);
    }

    /// <summary>Records credential checks and answers from a scripted table.</summary>
    public sealed class FakeLdapAuthenticator : ILdapAuthenticator
    {
        public readonly List<string> Calls = new();
        public readonly Dictionary<string, string> ValidCredentials = new(StringComparer.OrdinalIgnoreCase);
        public Exception Throw;

        public Task<bool> ValidateAuthenticationAsync(string login, string password, LdapConfig config = null)
        {
            Calls.Add($"Validate:{login}");
            if (Throw != null) throw Throw;

            return Task.FromResult(
                ValidCredentials.TryGetValue(login, out var expected) && expected == password);
        }
    }

    /// <summary>
    /// Installs the fakes into the <see cref="ObjectManager"/> seam and restores
    /// the production singletons on dispose, so no test leaks directory state
    /// into the next one.
    /// </summary>
    public sealed class LdapFakeScope : IDisposable
    {
        public FakeLdapQueryManager Query { get; }
        public FakeLdapAuthenticator Auth { get; }

        public LdapFakeScope(FakeLdapQueryManager query = null, FakeLdapAuthenticator auth = null)
        {
            Query = query ?? new FakeLdapQueryManager();
            Auth = auth ?? new FakeLdapAuthenticator();

            ObjectManager.QueryManagerOverride = Query;
            ObjectManager.AuthenticatorOverride = Auth;
        }

        public void Dispose()
        {
            ObjectManager.QueryManagerOverride = null;
            ObjectManager.AuthenticatorOverride = null;
        }
    }

    /// <summary>Terse builders for the entries the managers expect.</summary>
    public static class LdapEntries
    {
        public static LdapEntry User(
            string dn,
            string account = null,
            string name = null,
            string mail = null,
            string sid = "S-1-5-21-1",
            IEnumerable<string> memberOf = null,
            IDictionary<string, string> extra = null,
            string userPrincipalName = null,
            string givenName = null)
        {
            var attrs = new LdapAttributeSet
            {
                new LdapAttribute("objectclass", new[] { "top", "person", "user" }),
                new LdapAttribute("objectCategory", "person"),
                new LdapAttribute("distinguishedName", dn),
                new LdapAttribute("name", name ?? account ?? Cn(dn)),
                new LdapAttribute("cn", Cn(dn)),
                new LdapAttribute("sAMAccountName", account ?? Cn(dn)),
                new LdapAttribute("objectSid", sid),
                new LdapAttribute("userAccountControl", "512"),
            };

            if (mail != null) attrs.Add(new LdapAttribute("mail", mail));
            if (userPrincipalName != null) attrs.Add(new LdapAttribute("userPrincipalName", userPrincipalName));
            // Defaults to cn so existing callers keep the old (buggy-equivalent)
            // shape unless a test deliberately asks for a distinct givenName.
            attrs.Add(new LdapAttribute("givenName", givenName ?? Cn(dn)));

            if (memberOf != null)
            {
                var attr = new LdapAttribute("memberOf");
                foreach (var group in memberOf) attr.AddValue(group);
                attrs.Add(attr);
            }

            if (extra != null)
            {
                foreach (var pair in extra) attrs.Add(new LdapAttribute(pair.Key, pair.Value));
            }

            return new LdapEntry(dn, attrs);
        }

        /// <param name="memberAttributeName">
        /// Name the member attribute is stored under. Active Directory hands
        /// back windows such as <c>member;range=0-1499</c> for large groups.
        /// </param>
        public static LdapEntry Group(
            string dn,
            string description = null,
            IEnumerable<string> members = null,
            IEnumerable<string> memberOf = null,
            string memberAttributeName = "member")
        {
            var attrs = new LdapAttributeSet
            {
                new LdapAttribute("objectclass", new[] { "top", "group" }),
                new LdapAttribute("distinguishedName", dn),
                new LdapAttribute("name", Cn(dn)),
                new LdapAttribute("cn", Cn(dn)),
                new LdapAttribute("objectSid", "S-1-5-21-2"),
            };

            if (description != null) attrs.Add(new LdapAttribute("description", description));

            if (members != null)
            {
                var attr = new LdapAttribute(memberAttributeName);
                foreach (var member in members) attr.AddValue(member);
                attrs.Add(attr);
            }

            if (memberOf != null)
            {
                var attr = new LdapAttribute("memberOf");
                foreach (var parent in memberOf) attr.AddValue(parent);
                attrs.Add(attr);
            }

            return new LdapEntry(dn, attrs);
        }

        public static LdapEntry OrganizationalUnit(string dn, string description = null)
        {
            var attrs = new LdapAttributeSet
            {
                new LdapAttribute("objectclass", new[] { "top", "organizationalUnit" }),
                new LdapAttribute("distinguishedName", dn),
                new LdapAttribute("name", Cn(dn)),
                new LdapAttribute("ou", Cn(dn)),
            };

            if (description != null) attrs.Add(new LdapAttribute("description", description));

            return new LdapEntry(dn, attrs);
        }

        private static string Cn(string dn)
        {
            var first = dn.Split(',')[0];
            var separator = first.IndexOf('=');
            return separator < 0 ? first : first.Substring(separator + 1);
        }
    }
}
