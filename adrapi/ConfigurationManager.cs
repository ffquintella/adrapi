using System;
using Microsoft.Extensions.Configuration;

namespace adrapi
{
    public class ConfigurationManager
    {

        #region SINGLETON

        private static readonly Lazy<ConfigurationManager> lazy = new Lazy<ConfigurationManager>(() => new ConfigurationManager());

        public static ConfigurationManager Instance { get { return lazy.Value; } }

        private ConfigurationManager()
        {
        }

        #endregion

        public IConfiguration Config { get; set; }

    }
}
