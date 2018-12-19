using System;
using System.Collections.Generic;
using adrapi.domain;
using adrapi.Ldap;

namespace adrapi
{
    public class UserManager
    {

        #region SINGLETON

        private static readonly Lazy<UserManager> lazy = new Lazy<UserManager>(() => new UserManager());

        public static UserManager Instance { get { return lazy.Value; } }

        private UserManager()
        {
        }

        #endregion

        public List<User> GetList()
        {

            var lcm = LdapConnectionManager.Instance;
            var con = lcm.GetConnection();



            // Nothing found 
            return new List<User>();
        }
    }
}
