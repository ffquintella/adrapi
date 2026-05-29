using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Data.Sqlite;

namespace adrapi.domain.Security
{
    /// <summary>
    /// SQLite-backed store for arbitrary application secrets (LDAP bind credentials,
    /// certificate passwords, etc.) encrypted at rest with AES-256-GCM. Shares its
    /// database file with <see cref="ApiKeyStore"/>; the table is created on demand.
    ///
    /// Keys use IConfiguration-style colon paths (e.g. "ldap:bindCredentials") so
    /// they overlay the host configuration cleanly.
    /// </summary>
    public class AppSecretsStore
    {
        public string DatabasePath { get; }
        private readonly string _connectionString;
        private readonly byte[] _key;

        public AppSecretsStore(string databasePath, byte[] encryptionKey)
        {
            if (string.IsNullOrWhiteSpace(databasePath))
                throw new ArgumentException("databasePath required", nameof(databasePath));
            if (encryptionKey == null || encryptionKey.Length != 32)
                throw new ArgumentException("encryptionKey must be 32 bytes", nameof(encryptionKey));

            DatabasePath = databasePath;
            _key = encryptionKey;
            _connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Shared,
            }.ToString();
            EnsureSchema();
        }

        private void EnsureSchema()
        {
            var dir = Path.GetDirectoryName(DatabasePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            using var cn = Open();
            using var cmd = cn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS app_secrets (
                    name        TEXT PRIMARY KEY,
                    ciphertext  BLOB NOT NULL,
                    created_at  TEXT NOT NULL,
                    updated_at  TEXT NOT NULL
                );
                PRAGMA journal_mode = WAL;
            ";
            cmd.ExecuteNonQuery();
        }

        private SqliteConnection Open()
        {
            var cn = new SqliteConnection(_connectionString);
            cn.Open();
            return cn;
        }

        public void Set(string name, string value)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("name required", nameof(name));
            var envelope = SecretBox.Encrypt(_key, Encoding.UTF8.GetBytes(value ?? ""));
            var now = DateTime.UtcNow.ToString("o");
            using var cn = Open();
            using var cmd = cn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO app_secrets (name, ciphertext, created_at, updated_at)
                VALUES ($name, $ct, $now, $now)
                ON CONFLICT(name) DO UPDATE SET ciphertext = excluded.ciphertext, updated_at = excluded.updated_at;
            ";
            cmd.Parameters.AddWithValue("$name", name);
            cmd.Parameters.AddWithValue("$ct", envelope);
            cmd.Parameters.AddWithValue("$now", now);
            cmd.ExecuteNonQuery();
        }

        public string Get(string name)
        {
            using var cn = Open();
            using var cmd = cn.CreateCommand();
            cmd.CommandText = "SELECT ciphertext FROM app_secrets WHERE name = $name";
            cmd.Parameters.AddWithValue("$name", name);
            var result = cmd.ExecuteScalar();
            if (result == null || result is DBNull) return null;
            var envelope = (byte[])result;
            return Encoding.UTF8.GetString(SecretBox.Decrypt(_key, envelope));
        }

        public bool Remove(string name)
        {
            using var cn = Open();
            using var cmd = cn.CreateCommand();
            cmd.CommandText = "DELETE FROM app_secrets WHERE name = $name";
            cmd.Parameters.AddWithValue("$name", name);
            return cmd.ExecuteNonQuery() > 0;
        }

        public IReadOnlyList<SecretMetadata> List()
        {
            var list = new List<SecretMetadata>();
            using var cn = Open();
            using var cmd = cn.CreateCommand();
            cmd.CommandText = "SELECT name, created_at, updated_at FROM app_secrets ORDER BY name";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new SecretMetadata
                {
                    Name = r.GetString(0),
                    CreatedAt = DateTime.TryParse(r.GetString(1), out var c) ? c : (DateTime?)null,
                    UpdatedAt = DateTime.TryParse(r.GetString(2), out var u) ? u : (DateTime?)null,
                });
            }
            return list;
        }

        /// <summary>
        /// Returns every secret decrypted, as a flat dictionary. Used by the
        /// configuration provider on startup.
        /// </summary>
        public IReadOnlyDictionary<string, string> LoadAll()
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            using var cn = Open();
            using var cmd = cn.CreateCommand();
            cmd.CommandText = "SELECT name, ciphertext FROM app_secrets";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var name = r.GetString(0);
                var envelope = (byte[])r.GetValue(1);
                dict[name] = Encoding.UTF8.GetString(SecretBox.Decrypt(_key, envelope));
            }
            return dict;
        }

        public class SecretMetadata
        {
            public string Name { get; set; }
            public DateTime? CreatedAt { get; set; }
            public DateTime? UpdatedAt { get; set; }
        }
    }
}
