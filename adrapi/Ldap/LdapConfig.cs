using System;
using System.Configuration;
using Microsoft.Extensions.Configuration;

namespace adrapi.Ldap
{
    public class LdapConfig
    {
        public bool ssl { get; set; }
        public string bindDn { get; set; }
        public string bindCredentials { get; set; }
        public string searchBase { get; set; }
        public string searchFilter { get; set; }
        public string adminCn { get; set; }
        public short poolSize { get; set; }
        public string[] servers { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="T:adrapi.Ldap.LdapConfig"/> class.
        /// </summary>
        /// <param name="config">Config. (must be the Iconfigurarion based on the system)</param>
        public LdapConfig()
        {
            var cm = ConfigurationManager.Instance;

            var config = cm.Config;

            servers = config.GetSection("ldap").GetSection("servers").Get<string[]>();
            ssl = config.GetSection("ldap").GetValue<bool>("ssl");
            poolSize = config.GetSection("ldap").GetValue<short>("poolSize");
            bindDn = config.GetSection("ldap").GetValue<string>("bindDn");
            bindCredentials = config.GetSection("ldap").GetValue<string>("bindCredentials");
            searchBase = config.GetSection("ldap").GetValue<string>("searchBase");
            searchFilter = config.GetSection("ldap").GetValue<string>("searchFilter");
            adminCn = config.GetSection("ldap").GetValue<string>("adminCn");
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="T:adrapi.Ldap.LdapConfig"/> class.
        /// </summary>
        /// <param name="servers">Servers.</param>
        /// <param name="poolSize">Pool size.</param>
        /// <param name="bindDn">Bind dn.</param>
        /// <param name="bindCredentials">Bind credentials.</param>
        /// <param name="searchBase">Search base.</param>
        /// <param name="searchFilter">Search filter.</param>
        /// <param name="adminCn">Admin cn.</param>
        public LdapConfig(String[] servers, bool ssl, short poolSize, string bindDn, string bindCredentials, string searchBase, string searchFilter, string adminCn)
        {

            this.servers = servers;
            this.ssl = ssl;
            this.poolSize = poolSize;
            this.bindDn = bindDn;
            this.bindCredentials = bindCredentials;
            this.searchBase = searchBase;
            this.searchFilter = searchFilter;
            this.adminCn = adminCn;


        }
    }
}
