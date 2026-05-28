using System;
using System.Collections.Generic;

namespace adrapi.domain.Security
{
    /// <summary>
    /// API key record. The <see cref="secretKey"/> field is populated only
    /// momentarily — when a key is first created or rotated — so the operator can
    /// hand it to the client. It is never persisted to the store (only the
    /// Argon2id hash is) and never returned by lookups.
    /// </summary>
    public class ApiKey
    {
        public string secretKey { get; set; }
        public string keyID { get; set; }
        public string authorizedIP { get; set; }
        public List<string> claims { get; set; }

        /// <summary>Argon2id PHC-encoded hash of the secret. Persisted in the store.</summary>
        public string secretHash { get; set; }

        public DateTime? createdAt { get; set; }
        public DateTime? lastUsedAt { get; set; }

        public ApiKey()
        {
        }
    }
}
