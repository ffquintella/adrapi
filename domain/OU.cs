using System;
using System.ComponentModel.DataAnnotations;

namespace adrapi.domain
{
    public class OU
    {
        [Required]
        public string Name { get; set; }
        public string Description { get; set; }
        public string DN { get; set; }

        public OU()
        {
        }
    }
}
