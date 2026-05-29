using System;
using Microsoft.Extensions.Configuration;
using adrapi.domain.Security;

namespace adrapi
{
    /// <summary>
    /// IConfigurationSource that reads encrypted secrets from the SQLite
    /// app_secrets table and decrypts them into the configuration tree. Secret
    /// names are taken verbatim (so "ldap:bindCredentials" overlays
    /// ldap.bindCredentials).
    /// </summary>
    public class SqliteSecretsConfigurationSource : IConfigurationSource
    {
        public string DatabasePath { get; set; }
        public string SeedPath { get; set; }
        public bool Optional { get; set; } = true;

        public IConfigurationProvider Build(IConfigurationBuilder builder)
            => new SqliteSecretsConfigurationProvider(this);
    }

    public class SqliteSecretsConfigurationProvider : ConfigurationProvider
    {
        private readonly SqliteSecretsConfigurationSource _source;

        public SqliteSecretsConfigurationProvider(SqliteSecretsConfigurationSource source)
        {
            _source = source;
        }

        public override void Load()
        {
            try
            {
                if (!System.IO.File.Exists(_source.DatabasePath))
                {
                    if (_source.Optional) return;
                    throw new System.IO.FileNotFoundException(
                        $"Secrets database not found: {_source.DatabasePath}");
                }

                var keyProvider = new MachineKeyProvider(_source.SeedPath);
                if (!keyProvider.SeedExists())
                {
                    // Nothing to load yet — the management tool hasn't created
                    // any secrets. Don't generate a seed here; let the CLI do
                    // that explicitly on first `secret set`.
                    if (_source.Optional) return;
                    throw new InvalidOperationException(
                        $"Seed file {_source.SeedPath} is missing. Initialize via the CLI tool.");
                }

                var key = keyProvider.GetOrCreateKey();
                var store = new AppSecretsStore(_source.DatabasePath, key);
                var all = store.LoadAll();

                var data = new System.Collections.Generic.Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase);
                foreach (var kv in all) data[kv.Key] = kv.Value;
                Data = data;
            }
            catch when (_source.Optional)
            {
                // Swallow under optional mode; an unset secret simply falls back
                // to whatever earlier configuration source provided.
            }
        }
    }

    public static class SqliteSecretsConfigurationExtensions
    {
        public static IConfigurationBuilder AddSqliteSecrets(
            this IConfigurationBuilder builder,
            string databasePath,
            string seedPath,
            bool optional = true)
        {
            builder.Add(new SqliteSecretsConfigurationSource
            {
                DatabasePath = databasePath,
                SeedPath = seedPath,
                Optional = optional,
            });
            return builder;
        }
    }
}
