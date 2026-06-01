using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace adrapi.Models
{
    public class GroupCreateRequest
    {
        // Required for LDAP/AD (validated explicitly in the controller); not used
        // by the Entra ID backend, which addresses groups by name/objectId.
        public string DN { get; set; }

        [Required]
        public string Name { get; set; }

        public string Description { get; set; }

        /// <summary>Entra ID only: "Security" (default) or "Microsoft365". Ignored by LDAP/AD.</summary>
        public string GroupType { get; set; }

        public List<string> Members { get; set; } = new List<string>();
    }
}
