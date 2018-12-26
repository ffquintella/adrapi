using System;
using System.Collections.Generic;
using adrapi.Ldap;
using adrapi.domain;
using Novell.Directory.Ldap;

namespace adrapi
{
    public class OUManager: ObjectManager
    {
        #region SINGLETON

        private static readonly Lazy<OUManager> lazy = new Lazy<OUManager>(() => new OUManager());

        public static OUManager Instance { get { return lazy.Value; } }

        private OUManager()
        {
            logger = NLog.LogManager.GetCurrentClassLogger();
        }

        #endregion

        /// <summary>
        /// Return a string list of the OUs DNs
        /// </summary>
        /// <returns>The list.</returns>
        public List<String> GetList()
        {
            var ous = new List<String>();

            var sMgmt = LdapSearchManager.Instance;

            int results = 0;


            var resps = sMgmt.ExecutePagedSearch("", LdapSearchType.OU);

            foreach (var entry in resps)
            {
                ous.Add(entry.GetAttribute("distinguishedName").StringValue);
                results++;
            }

            logger.Debug("OU search executed results:{result}", results);


            return ous;
        }

    }
}
