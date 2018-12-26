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

        /// <summary>
        /// Gets the OU.
        /// </summary>
        /// <returns>The OU.</returns>
        /// <param name="DN">The Disitnguesh name of the OU</param>
        public OU GetOU(string DN)
        {
            var sMgmt = LdapSearchManager.Instance;

            var entry = sMgmt.GetRegister(DN);

            var ou = ConvertfromLdap(entry);
            return ou;
        }

        /// <summary>
        /// Converts the data from the LDAP result
        /// </summary>
        /// <returns>The LDAP.</returns>
        /// <param name="entry">Entry.</param>
        private OU ConvertfromLdap(LdapEntry entry)
        {
            var ou = new OU();

            ou.Name = entry.GetAttribute("name").StringValue;

            if (entry.GetAttribute("description") != null) ou.Description = entry.GetAttribute("description").StringValue;

            ou.DN = entry.GetAttribute("distinguishedName").StringValue;


            return ou;
        }

    }
}
