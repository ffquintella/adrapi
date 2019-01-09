using System;
using System.Collections.Generic;
using adrapi.Ldap;
using adrapi.domain;
using Novell.Directory.Ldap;
using NLog;
using adrapi.Tools;

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

            var sMgmt = LdapQueryManager.Instance;

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
            var sMgmt = LdapQueryManager.Instance;

            try
            {
                var entry = sMgmt.GetRegister(DN);
                var ou = ConvertfromLdap(entry); ;
                return ou;
            }
            catch (LdapException ex)
            {
                logger.Debug("User not found {0} Ex: {1}", DN, ex.Message);
                return null;
            }


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

        private LdapAttributeSet GetAttributeSet(OU ou)
        {
            LdapAttributeSet attributeSet = new LdapAttributeSet();

            attributeSet.Add(new LdapAttribute("objectclass", new string[] { "top", "organizationalUnit" }));
            attributeSet.Add(new LdapAttribute("name", ou.Name));
            attributeSet.Add(new LdapAttribute("ou", ou.Name));
            attributeSet.Add(new LdapAttribute("description", ou.Description));
            attributeSet.Add(new LdapAttribute("distinguishedName", ou.DN));


            return attributeSet;
        }

        public int CreateOU(OU ou)
        {

            //Creates the List attributes of the entry and add them to attributeset

            LdapAttributeSet attributeSet = GetAttributeSet(ou);

            // DN of the entry to be added
            string dn = ou.DN;

            LdapEntry newEntry = new LdapEntry(dn, attributeSet);


            var qMgmt = LdapQueryManager.Instance;

            try
            {
                logger.Info("Saving ou={OU}", ou.DN);
                qMgmt.AddEntry(newEntry);
                return 0;

            }
            catch (Exception ex)
            {
                logger.Error("Error saving ou={DN}", ou.DN);
                logger.Log(LogLevel.Error, ex);
                return -1;
            }

        }

        /// <summary>
        /// Saves the OU.
        /// </summary>
        /// <returns>The OU. Must have DN set</returns>
        /// <param name="ou">OU.</param>
        public int SaveOU(OU ou)
        {

            var qMgmt = LdapQueryManager.Instance;

            var modList = new List<LdapModification>();

            var atributes = GetAttributeSet(ou);

            //Get user from the Directory
            try
            {
                var dou = GetOU(ou.DN);

                var dattrs = GetAttributeSet(dou);


                foreach (LdapAttribute attr in atributes)
                {
                    if (
                        attr.Name != "ou"
                        && attr.Name != "objectclass"
                      )
                    {

                        var b1 = attr.ByteValue;

                        var attribute = dattrs.GetAttribute(attr.Name);

                        bool equal = true;

                        if (attribute != null)
                        {
                            var b2 = attribute.ByteValue;

                            equal = ByteTools.Equality(b1, b2);
                        }


                        if (!equal)
                            modList.Add(new LdapModification(LdapModification.Replace, attr));
                    }
 

                }


                try
                {
                    qMgmt.SaveEntry(ou.DN, modList.ToArray());
                    return 0;

                }
                catch (Exception ex)
                {
                    logger.Error("Error updating OU={DN}", ou.DN);
                    logger.Log(LogLevel.Error, ex);
                    return -1;
                }

            }
            catch (Exception ex)
            {
                logger.Error("Error group not found");
                logger.Log(LogLevel.Error, ex);
                return -1;
            }


        }

    }
}
