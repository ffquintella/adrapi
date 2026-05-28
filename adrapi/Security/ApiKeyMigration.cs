using System;
using System.Collections.Generic;
using System.IO;
using NLog;
using Newtonsoft.Json;
using Microsoft.Extensions.Configuration;
using adrapi.domain.Security;

namespace adrapi.Security
{
    /// <summary>
    /// One-shot migrator from the legacy plaintext `security.json` file to the
    /// SQLite store. Runs at startup; idempotent — once a key with the same
    /// keyID exists in the store, it is not re-imported.
    ///
    /// After a successful import the source file is renamed to
    /// `security.json.imported.<timestamp>` so the next boot doesn't touch it,
    /// and so the operator can review/delete the old plaintext file.
    /// </summary>
    public static class ApiKeyMigration
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        public static void ImportLegacyJsonIfPresent(IConfiguration configuration)
        {
            var path = configuration.GetSection("security").GetValue<string>("legacyJsonFile")
                ?? "security.json";

            if (!File.Exists(path)) return;

            try
            {
                var json = File.ReadAllText(path);
                var legacy = JsonConvert.DeserializeObject<List<ApiKey>>(json);
                if (legacy == null || legacy.Count == 0)
                {
                    logger.Info("Legacy {path} is empty; skipping import.", path);
                    return;
                }

                var store = ApiKeyManager.Store;
                int imported = 0, skipped = 0;
                foreach (var k in legacy)
                {
                    if (string.IsNullOrEmpty(k.keyID) || string.IsNullOrEmpty(k.secretKey))
                    {
                        skipped++;
                        continue;
                    }
                    if (store.FindByKeyId(k.keyID) != null)
                    {
                        skipped++;
                        continue;
                    }
                    store.Insert(new ApiKey
                    {
                        keyID = k.keyID,
                        authorizedIP = k.authorizedIP,
                        claims = k.claims,
                    }, plaintextSecret: k.secretKey);
                    imported++;
                }

                var archived = $"{path}.imported.{DateTime.UtcNow:yyyyMMddHHmmss}";
                File.Move(path, archived);
                logger.Warn(
                    "Imported {imported} key(s) from legacy {path} into the SQLite store (skipped {skipped}). " +
                    "The plaintext file has been renamed to {archived} — review and delete it.",
                    imported, path, skipped, archived);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to import legacy {path}; the file was left in place.", path);
            }
        }
    }
}
