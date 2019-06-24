using System.Collections.Generic;
using Novell.Directory.Ldap;

namespace adrapi.Ldap
{
    public class LdapPagedResponse
    {
        public string Cookie;
        public List<LdapEntry> Entries = new List<LdapEntry>();
    }
}