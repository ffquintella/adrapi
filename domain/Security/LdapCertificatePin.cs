using System;

namespace adrapi.domain.Security
{
    /// <summary>
    /// A single trusted LDAPS server certificate, identified by its SHA-256 thumbprint.
    /// </summary>
    public class LdapCertificatePin
    {
        public string Host { get; set; }
        public string Sha256 { get; set; }
        public string Subject { get; set; }
        public string Issuer { get; set; }
        public DateTime? NotBefore { get; set; }
        public DateTime? NotAfter { get; set; }
        public DateTime AddedAt { get; set; }
        public string Note { get; set; }
    }
}
