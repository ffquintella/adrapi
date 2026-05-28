using System.Collections.Generic;

namespace adrapi.Models
{
    public class GroupMembersPatchRequest
    {
        public List<string> Add { get; set; } = new List<string>();
        public List<string> Remove { get; set; } = new List<string>();
    }
}
