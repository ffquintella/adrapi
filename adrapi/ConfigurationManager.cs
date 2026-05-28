using System;
using Microsoft.Extensions.Configuration;

namespace adrapi
{
    /// <summary>
    /// Singleton holder for application configuration shared across legacy singleton managers.
    /// </summary>
    public class ConfigurationManager
    {

        #region SINGLETON

        private static readonly Lazy<ConfigurationManager> lazy = new Lazy<ConfigurationManager>(() => new ConfigurationManager());

        public static ConfigurationManager Instance { get { return lazy.Value; } }

        private ConfigurationManager()
        {
        }

        #endregion

        /// <summary>
        /// Runtime configuration loaded at startup.
        /// </summary>
        public IConfiguration Config { get; set; }

    }
}
