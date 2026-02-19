using System.Collections.Generic;

namespace adrapi.Models
{
    public class UserGroupsResponse
    {
        public string LookupValue { get; set; }
        public string DistinguishedName { get; set; }
        public List<string> MemberOfDns { get; set; } = new List<string>();
        public List<string> MemberOfCns { get; set; } = new List<string>();
    }
}
