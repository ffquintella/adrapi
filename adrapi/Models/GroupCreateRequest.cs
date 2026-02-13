using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace adrapi.Models
{
    public class GroupCreateRequest
    {
        [Required]
        public string DN { get; set; }

        [Required]
        public string Name { get; set; }

        public string Description { get; set; }

        public List<string> Members { get; set; } = new List<string>();
    }
}
