using System;
using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace adrapi.domain.Security
{
    /// <summary>
    /// Argon2id password hasher using a PHC-style serialization
    ///   $argon2id$v=19$m=<memKiB>,t=<iters>,p=<paral>$<saltB64>$<hashB64>
    /// so that parameters travel with the hash and can be tuned per record
    /// (allowing seamless upgrades).
    ///
    /// Defaults follow the OWASP 2024 recommendation for Argon2id:
    ///   memory = 19456 KiB (~19 MiB), iterations = 2, parallelism = 1, hash = 32 B
    /// </summary>
    public static class ApiKeyHasher
    {
        public const int DefaultMemoryKiB = 19456;
        public const int DefaultIterations = 2;
        public const int DefaultParallelism = 1;
        public const int DefaultHashLength = 32;
        public const int SaltLength = 16;

        public static string Hash(
            string secret,
            int memoryKiB = DefaultMemoryKiB,
            int iterations = DefaultIterations,
            int parallelism = DefaultParallelism,
            int hashLength = DefaultHashLength)
        {
            if (string.IsNullOrEmpty(secret)) throw new ArgumentException("secret required", nameof(secret));

            var salt = RandomNumberGenerator.GetBytes(SaltLength);
            var hash = ComputeHash(secret, salt, memoryKiB, iterations, parallelism, hashLength);
            return $"$argon2id$v=19$m={memoryKiB},t={iterations},p={parallelism}${B64(salt)}${B64(hash)}";
        }

        /// <summary>
        /// Constant-time verification of a secret against a PHC-encoded Argon2id hash.
        /// Returns false on any parse error (never throws) to avoid distinguishing
        /// "malformed hash" from "wrong secret" via side channels.
        /// </summary>
        public static bool Verify(string secret, string phc)
        {
            if (string.IsNullOrEmpty(secret) || string.IsNullOrEmpty(phc)) return false;
            try
            {
                var parts = phc.Split('$');
                // parts: ["", "argon2id", "v=19", "m=...,t=...,p=...", salt, hash]
                if (parts.Length != 6 || parts[1] != "argon2id") return false;

                var paramKvps = parts[3].Split(',');
                int m = 0, t = 0, p = 0;
                foreach (var kv in paramKvps)
                {
                    var eq = kv.IndexOf('=');
                    if (eq < 0) return false;
                    var key = kv.Substring(0, eq);
                    var val = int.Parse(kv.Substring(eq + 1));
                    if (key == "m") m = val;
                    else if (key == "t") t = val;
                    else if (key == "p") p = val;
                }
                if (m <= 0 || t <= 0 || p <= 0) return false;

                var salt = FromB64(parts[4]);
                var expected = FromB64(parts[5]);
                var actual = ComputeHash(secret, salt, m, t, p, expected.Length);

                return CryptographicOperations.FixedTimeEquals(actual, expected);
            }
            catch
            {
                return false;
            }
        }

        private static byte[] ComputeHash(string secret, byte[] salt, int memoryKiB, int iterations, int parallelism, int hashLength)
        {
            using var argon = new Argon2id(Encoding.UTF8.GetBytes(secret))
            {
                Salt = salt,
                MemorySize = memoryKiB,
                Iterations = iterations,
                DegreeOfParallelism = parallelism,
            };
            return argon.GetBytes(hashLength);
        }

        private static string B64(byte[] data) => Convert.ToBase64String(data);
        private static byte[] FromB64(string s) => Convert.FromBase64String(s);

        /// <summary>
        /// Cryptographically secure random secret string (URL-safe Base64, ~32 bytes entropy).
        /// </summary>
        public static string GenerateSecret(int bytes = 32)
        {
            var raw = RandomNumberGenerator.GetBytes(bytes);
            return Convert.ToBase64String(raw)
                .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        }
    }
}
