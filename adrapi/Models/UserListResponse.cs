using System.Collections.Generic;
using adrapi.domain;

namespace adrapi.Models
{
    public class UserListResponse
    {
        public string Cookie
        {
            get;
            set;
        }

        public List<string> UserNames { get; set; }

        public List<User> Users { get; set; }

        public string SearchType { get; set; }
        public string SearchMethod { get; set; }
    }
}