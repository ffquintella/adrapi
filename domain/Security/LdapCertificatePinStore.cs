using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace adrapi.domain.Security
{
    /// <summary>
    /// File-backed store of trusted LDAPS server certificate thumbprints (SHA-256).
    ///
    /// Format: JSON document with a `pins` array. The file is shared between the
    /// running API (read-only) and the AdrapiLdapCertPin helper tool (read/write).
    /// </summary>
    public class LdapCertificatePinStore
    {
        private class FileShape
        {
            public List<LdapCertificatePin> Pins { get; set; } = new List<LdapCertificatePin>();
        }

        private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
        };

        public string FilePath { get; }

        public LdapCertificatePinStore(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("Pin store path cannot be empty.", nameof(filePath));
            FilePath = filePath;
        }

        public List<LdapCertificatePin> Load()
        {
            if (!File.Exists(FilePath)) return new List<LdapCertificatePin>();
            var json = File.ReadAllText(FilePath);
            if (string.IsNullOrWhiteSpace(json)) return new List<LdapCertificatePin>();
            var shape = JsonSerializer.Deserialize<FileShape>(json, JsonOpts) ?? new FileShape();
            return shape.Pins ?? new List<LdapCertificatePin>();
        }

        public void Save(IEnumerable<LdapCertificatePin> pins)
        {
            var dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var shape = new FileShape { Pins = pins.ToList() };
            File.WriteAllText(FilePath, JsonSerializer.Serialize(shape, JsonOpts));
        }

        public bool IsTrusted(string host, string sha256)
        {
            if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(sha256)) return false;
            var pins = Load();
            return pins.Any(p =>
                string.Equals(p.Host, host, StringComparison.OrdinalIgnoreCase)
                && string.Equals(Normalize(p.Sha256), Normalize(sha256), StringComparison.OrdinalIgnoreCase));
        }

        public void Add(LdapCertificatePin pin)
        {
            if (pin == null) throw new ArgumentNullException(nameof(pin));
            var pins = Load();
            pins.RemoveAll(p =>
                string.Equals(p.Host, pin.Host, StringComparison.OrdinalIgnoreCase)
                && string.Equals(Normalize(p.Sha256), Normalize(pin.Sha256), StringComparison.OrdinalIgnoreCase));
            pin.Sha256 = Normalize(pin.Sha256);
            pin.AddedAt = pin.AddedAt == default ? DateTime.UtcNow : pin.AddedAt;
            pins.Add(pin);
            Save(pins);
        }

        public bool Remove(string host, string sha256)
        {
            var pins = Load();
            var removed = pins.RemoveAll(p =>
                string.Equals(p.Host, host, StringComparison.OrdinalIgnoreCase)
                && string.Equals(Normalize(p.Sha256), Normalize(sha256), StringComparison.OrdinalIgnoreCase));
            if (removed > 0) Save(pins);
            return removed > 0;
        }

        /// <summary>
        /// Lowercase hex SHA-256 of the DER-encoded certificate, formatted with colons
        /// between byte pairs (e.g. "ab:cd:ef:..."). Stable, human-comparable.
        /// </summary>
        public static string ComputeSha256(X509Certificate certificate)
        {
            if (certificate == null) throw new ArgumentNullException(nameof(certificate));
            var raw = certificate.GetRawCertData();
            var hash = SHA256.HashData(raw);
            var sb = new StringBuilder(hash.Length * 3);
            for (int i = 0; i < hash.Length; i++)
            {
                if (i > 0) sb.Append(':');
                sb.Append(hash[i].ToString("x2"));
            }
            return sb.ToString();
        }

        private static string Normalize(string sha256)
            => sha256?.Replace(" ", "").Replace("-", ":").ToLowerInvariant();
    }
}
