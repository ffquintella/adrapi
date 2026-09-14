using System.Collections.Generic;
using adrapi.domain;

namespace adrapi.Directory
{
    /// <summary>
    /// One page of users plus the opaque token that fetches the next one. The
    /// token's content is backend-specific (an LDAP paged-results cookie, a Graph
    /// <c>$skiptoken</c>); callers treat it as opaque and echo it back. An empty
    /// token means there is no further page.
    /// </summary>
    public class UserPage
    {
        public List<User> Users { get; set; } = new List<User>();

        public string Cookie { get; set; } = "";
    }
}
