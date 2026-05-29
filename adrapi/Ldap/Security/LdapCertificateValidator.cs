using System;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using adrapi.domain.Security;
using NLog;

namespace adrapi.Ldap.Security
{
    /// <summary>
    /// Validates LDAPS server certificates.
    ///
    /// Acceptance order:
    ///   1. The certificate is valid against the system trust store (no SSL errors).
    ///   2. OR its SHA-256 thumbprint is present in the pin store for the target host.
    ///
    /// Otherwise the connection is rejected and an actionable log entry is written so
    /// the operator can run the `AdrapiLdapCertPin` helper tool to authorize the cert.
    /// </summary>
    public class LdapCertificateValidator
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private readonly LdapCertificatePinStore _store;
        private readonly string _targetHost;

        public LdapCertificateValidator(LdapCertificatePinStore store, string targetHost)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _targetHost = targetHost ?? throw new ArgumentNullException(nameof(targetHost));
        }

        public bool Validate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            if (sslPolicyErrors == SslPolicyErrors.None)
            {
                return true;
            }

            if (certificate == null)
            {
                Logger.Error("LDAPS handshake to {host} produced no certificate; rejecting.", _targetHost);
                return false;
            }

            var thumb = LdapCertificatePinStore.ComputeSha256(certificate);

            if (_store.IsTrusted(_targetHost, thumb))
            {
                Logger.Info("Accepted LDAPS certificate for {host} via pin (sha256={sha}).", _targetHost, thumb);
                return true;
            }

            Logger.Error(
                "Rejected LDAPS certificate for {host}: SslPolicyErrors={errors}, sha256={sha}, subject=\"{subject}\", issuer=\"{issuer}\". " +
                "To authorize, run: dotnet run --project tools/AdrapiLdapCertPin -- {host}:<port>",
                _targetHost, sslPolicyErrors, thumb, certificate.Subject, certificate.Issuer);

            return false;
        }
    }
}
