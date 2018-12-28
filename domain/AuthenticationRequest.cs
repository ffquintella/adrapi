using System;
using System.ComponentModel.DataAnnotations;

namespace adrapi.domain
{
    public class AuthenticationRequest
    {

        public string Login { get; set; }
        [Required]
        public string Password { get; set; }
    }
}
