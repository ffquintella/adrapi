using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace adrapi.domain.Security
{
    /// <summary>
    /// SQLite-backed store of API keys. Stores Argon2id hashes only — plaintext
    /// secrets are never persisted. Schema is created on demand if the database
    /// file is missing.
    /// </summary>
    public class ApiKeyStore
    {
        public string DatabasePath { get; }
        private readonly string _connectionString;

        public ApiKeyStore(string databasePath)
        {
            if (string.IsNullOrWhiteSpace(databasePath))
                throw new ArgumentException("databasePath required", nameof(databasePath));
            DatabasePath = databasePath;
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
                CREATE TABLE IF NOT EXISTS api_keys (
                    key_id        TEXT PRIMARY KEY,
                    secret_hash   TEXT NOT NULL,
                    authorized_ip TEXT NOT NULL,
                    claims_json   TEXT NOT NULL DEFAULT '[]',
                    created_at    TEXT NOT NULL,
                    last_used_at  TEXT
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

        public ApiKey FindByKeyId(string keyId)
        {
            if (string.IsNullOrEmpty(keyId)) return null;
            using var cn = Open();
            using var cmd = cn.CreateCommand();
            cmd.CommandText = "SELECT key_id, secret_hash, authorized_ip, claims_json, created_at, last_used_at FROM api_keys WHERE key_id = $id";
            cmd.Parameters.AddWithValue("$id", keyId);
            using var r = cmd.ExecuteReader();
            return r.Read() ? ReadRow(r) : null;
        }

        public IReadOnlyList<ApiKey> List()
        {
            var list = new List<ApiKey>();
            using var cn = Open();
            using var cmd = cn.CreateCommand();
            cmd.CommandText = "SELECT key_id, secret_hash, authorized_ip, claims_json, created_at, last_used_at FROM api_keys ORDER BY key_id";
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add(ReadRow(r));
            return list;
        }

        /// <summary>
        /// Inserts a new key, hashing the plaintext secret with Argon2id. Pass an
        /// already-populated <c>secretHash</c> on <paramref name="key"/> to skip
        /// re-hashing (used by migration imports that already carry hashes).
        /// </summary>
        public void Insert(ApiKey key, string plaintextSecret = null)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (string.IsNullOrEmpty(key.keyID)) throw new ArgumentException("keyID required", nameof(key));
            if (string.IsNullOrEmpty(key.secretHash))
            {
                if (string.IsNullOrEmpty(plaintextSecret))
                    throw new ArgumentException("either secretHash or plaintextSecret must be supplied");
                key.secretHash = ApiKeyHasher.Hash(plaintextSecret);
            }

            using var cn = Open();
            using var cmd = cn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO api_keys (key_id, secret_hash, authorized_ip, claims_json, created_at)
                VALUES ($id, $hash, $ip, $claims, $created);
            ";
            cmd.Parameters.AddWithValue("$id", key.keyID);
            cmd.Parameters.AddWithValue("$hash", key.secretHash);
            cmd.Parameters.AddWithValue("$ip", key.authorizedIP ?? "");
            cmd.Parameters.AddWithValue("$claims", JsonSerializer.Serialize(key.claims ?? new List<string>()));
            cmd.Parameters.AddWithValue("$created", (key.createdAt ?? DateTime.UtcNow).ToString("o"));
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// Replaces the secret hash of an existing key. Returns true if the keyID
        /// existed and was updated.
        /// </summary>
        public bool Rotate(string keyId, string newPlaintextSecret)
        {
            if (string.IsNullOrEmpty(newPlaintextSecret)) throw new ArgumentException("secret required", nameof(newPlaintextSecret));
            var hash = ApiKeyHasher.Hash(newPlaintextSecret);
            using var cn = Open();
            using var cmd = cn.CreateCommand();
            cmd.CommandText = "UPDATE api_keys SET secret_hash = $hash WHERE key_id = $id";
            cmd.Parameters.AddWithValue("$hash", hash);
            cmd.Parameters.AddWithValue("$id", keyId);
            return cmd.ExecuteNonQuery() > 0;
        }

        public bool Remove(string keyId)
        {
            using var cn = Open();
            using var cmd = cn.CreateCommand();
            cmd.CommandText = "DELETE FROM api_keys WHERE key_id = $id";
            cmd.Parameters.AddWithValue("$id", keyId);
            return cmd.ExecuteNonQuery() > 0;
        }

        /// <summary>
        /// Looks up the key by id and verifies the supplied plaintext secret against
        /// the stored Argon2id hash. Returns the full record on success (so the
        /// caller can read claims/authorizedIp without a second query), or null on
        /// any failure. Updates last_used_at on success.
        /// </summary>
        public ApiKey VerifyAndLoad(string keyId, string plaintextSecret)
        {
            var record = FindByKeyId(keyId);
            if (record == null) return null;
            if (!ApiKeyHasher.Verify(plaintextSecret, record.secretHash)) return null;
            TouchLastUsed(keyId);
            return record;
        }

        private void TouchLastUsed(string keyId)
        {
            using var cn = Open();
            using var cmd = cn.CreateCommand();
            cmd.CommandText = "UPDATE api_keys SET last_used_at = $now WHERE key_id = $id";
            cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
            cmd.Parameters.AddWithValue("$id", keyId);
            cmd.ExecuteNonQuery();
        }

        private static ApiKey ReadRow(SqliteDataReader r)
        {
            var claimsJson = r.GetString(3);
            return new ApiKey
            {
                keyID = r.GetString(0),
                secretHash = r.GetString(1),
                authorizedIP = r.GetString(2),
                claims = string.IsNullOrWhiteSpace(claimsJson)
                    ? new List<string>()
                    : JsonSerializer.Deserialize<List<string>>(claimsJson),
                createdAt = DateTime.TryParse(r.GetString(4), out var c) ? c : (DateTime?)null,
                lastUsedAt = r.IsDBNull(5) ? (DateTime?)null
                    : (DateTime.TryParse(r.GetString(5), out var u) ? u : (DateTime?)null),
            };
        }
    }
}
