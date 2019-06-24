using System.Collections.Generic;
using adrapi.domain;

namespace adrapi.Models
{
    public class UserListResponse
    {
        public string Cookie;

        public List<string> UserNames;

        public List<User> Users;

        public string SearchType;
        public string SearchMethod;
    }
}