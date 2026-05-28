using System.Collections.Generic;

namespace adrapi.Models
{
    public class UserAttributeInspectionResponse
    {
        public string LookupValue { get; set; }
        public string LookupAttribute { get; set; }
        public string DistinguishedName { get; set; }
        public Dictionary<string, List<string>> Attributes { get; set; } = new Dictionary<string, List<string>>();
        public List<string> MemberOfDns { get; set; } = new List<string>();
        public List<string> MemberOfCns { get; set; } = new List<string>();
    }
}
