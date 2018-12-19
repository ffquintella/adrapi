using System;
using Novell.Directory.Ldap;
using System.Collections.Generic;
using System.Linq;

namespace adrapi.Ldap
{
    public class LdapSearchManager
    {
        public NLog.Logger logger;

        private LdapConfig config;

        #region SINGLETON

        private static readonly Lazy<LdapSearchManager> lazy = new Lazy<LdapSearchManager>(() => new LdapSearchManager());

        public static LdapSearchManager Instance { get { return lazy.Value; } }

        private LdapSearchManager()
        {
            logger = NLog.LogManager.GetCurrentClassLogger();
            config = new Ldap.LdapConfig();
        }
        #endregion


        public LdapMessageQueue SendSearch(string searchBase, LdapSearchType type)
        {
            switch (type) {
                case LdapSearchType.User:
                    logger.Debug("Serching all users");
                    return SendSearch(searchBase, $"(&(objectClass=user)(objectCategory=person))");
                case LdapSearchType.Group:
                    logger.Debug("Serching all groups");
                    return SendSearch(searchBase, $"(objectClass=group)");
                case LdapSearchType.OU:
                    logger.Debug("Serching all OUs");
                    return SendSearch(searchBase, $"(&(ou=*)(objectClass=organizationalunit))");
                case LdapSearchType.Machine:
                    logger.Debug("Serching all computers");
                    return SendSearch(searchBase, $"(objectClass=computer)");
                default:
                    logger.Error("Search type not specified.");
                    throw new domain.Exceptions.WrongParameterException("Search type not specified");
            }
        }

        public LdapMessageQueue SendSearch(string searchBase, string filter)
        {
            var lcm = LdapConnectionManager.Instance;
            var con = lcm.GetConnection();

            var sb = searchBase + config.searchBase;

            var req = new LdapSearchRequest(sb, LdapConnection.ScopeSub, filter, null, 0, config.maxResults, 0, false, null);
            var queue = con.SendRequest(req, null);

            return queue;

        }


    }
}
