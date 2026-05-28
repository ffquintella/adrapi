using System;
using System.IO;
using System.Linq;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using adrapi.domain.Security;

namespace adrapi.Tools.LdapCertPin
{
    /// <summary>
    /// Interactive helper that fetches the certificate presented by an LDAPS endpoint,
    /// prints its details, and (with operator confirmation) records its SHA-256
    /// thumbprint in the pin store used by the API at runtime.
    ///
    /// Usage:
    ///   dotnet run --project tools/AdrapiLdapCertPin -- &lt;host:port&gt; [--store &lt;path&gt;] [--note "..."] [--yes]
    ///   dotnet run --project tools/AdrapiLdapCertPin -- --list [--store &lt;path&gt;]
    ///   dotnet run --project tools/AdrapiLdapCertPin -- --remove &lt;host&gt; &lt;sha256&gt; [--store &lt;path&gt;]
    /// </summary>
    public static class Program
    {
        private const string DefaultStorePath = "cfg/ldap-trusted-certs.json";

        public static int Main(string[] args)
        {
            try
            {
                var opts = Args.Parse(args);

                if (opts.ShowHelp || opts.Target == null && !opts.List && opts.RemoveHost == null)
                {
                    PrintHelp();
                    return opts.ShowHelp ? 0 : 1;
                }

                var store = new LdapCertificatePinStore(opts.StorePath ?? DefaultStorePath);
                Console.WriteLine($"Pin store: {Path.GetFullPath(store.FilePath)}");

                if (opts.List) return ListPins(store);
                if (opts.RemoveHost != null) return RemovePin(store, opts.RemoveHost, opts.RemoveSha);

                return AddPin(store, opts);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"error: {ex.Message}");
                return 2;
            }
        }

        private static int AddPin(LdapCertificatePinStore store, Args opts)
        {
            var (host, port) = ParseHostPort(opts.Target);
            Console.WriteLine($"Connecting to {host}:{port} ...");

            X509Certificate2 cert = FetchCertificate(host, port);
            if (cert == null)
            {
                Console.Error.WriteLine("Could not retrieve a certificate from the server.");
                return 3;
            }

            var sha = LdapCertificatePinStore.ComputeSha256(cert);
            PrintCertificate(cert, sha);

            if (store.IsTrusted(host, sha))
            {
                Console.WriteLine();
                Console.WriteLine("This certificate is already pinned for this host. Nothing to do.");
                return 0;
            }

            if (!opts.AssumeYes)
            {
                Console.WriteLine();
                Console.Write("Add this certificate to the pin store? [y/N] ");
                var answer = Console.ReadLine();
                if (!string.Equals((answer ?? "").Trim(), "y", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("Aborted. No changes written.");
                    return 1;
                }
            }

            store.Add(new LdapCertificatePin
            {
                Host = host,
                Sha256 = sha,
                Subject = cert.Subject,
                Issuer = cert.Issuer,
                NotBefore = cert.NotBefore.ToUniversalTime(),
                NotAfter = cert.NotAfter.ToUniversalTime(),
                AddedAt = DateTime.UtcNow,
                Note = opts.Note,
            });

            Console.WriteLine();
            Console.WriteLine($"Pinned. The API will now trust this certificate for host \"{host}\".");
            return 0;
        }

        private static int ListPins(LdapCertificatePinStore store)
        {
            var pins = store.Load();
            if (pins.Count == 0)
            {
                Console.WriteLine("(no pins)");
                return 0;
            }
            foreach (var p in pins.OrderBy(x => x.Host).ThenBy(x => x.AddedAt))
            {
                Console.WriteLine();
                Console.WriteLine($"host:      {p.Host}");
                Console.WriteLine($"sha256:    {p.Sha256}");
                Console.WriteLine($"subject:   {p.Subject}");
                Console.WriteLine($"issuer:    {p.Issuer}");
                Console.WriteLine($"notBefore: {p.NotBefore:u}");
                Console.WriteLine($"notAfter:  {p.NotAfter:u}");
                Console.WriteLine($"addedAt:   {p.AddedAt:u}");
                if (!string.IsNullOrWhiteSpace(p.Note)) Console.WriteLine($"note:      {p.Note}");
            }
            return 0;
        }

        private static int RemovePin(LdapCertificatePinStore store, string host, string sha)
        {
            if (string.IsNullOrWhiteSpace(sha))
            {
                Console.Error.WriteLine("--remove requires <host> <sha256>");
                return 1;
            }
            var ok = store.Remove(host, sha);
            Console.WriteLine(ok ? "Pin removed." : "No matching pin found.");
            return ok ? 0 : 1;
        }

        private static X509Certificate2 FetchCertificate(string host, int port)
        {
            X509Certificate2 captured = null;
            using var tcp = new TcpClient();
            tcp.Connect(host, port);
            using var ssl = new SslStream(tcp.GetStream(), leaveInnerStreamOpen: false,
                (sender, certificate, chain, errors) =>
                {
                    if (certificate != null)
                    {
                        // Clone so we keep the cert after the stream is disposed.
                        captured = new X509Certificate2(certificate.GetRawCertData());
                    }
                    return true;
                });

            var sslOptions = new SslClientAuthenticationOptions
            {
                TargetHost = host,
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
            };
            ssl.AuthenticateAsClient(sslOptions);
            return captured;
        }

        private static void PrintCertificate(X509Certificate2 cert, string sha)
        {
            Console.WriteLine();
            Console.WriteLine("Certificate presented by server:");
            Console.WriteLine($"  subject:    {cert.Subject}");
            Console.WriteLine($"  issuer:     {cert.Issuer}");
            Console.WriteLine($"  serial:     {cert.SerialNumber}");
            Console.WriteLine($"  notBefore:  {cert.NotBefore:u}");
            Console.WriteLine($"  notAfter:   {cert.NotAfter:u}");
            Console.WriteLine($"  sha1:       {cert.Thumbprint}");
            Console.WriteLine($"  sha256:     {sha}");
        }

        private static (string host, int port) ParseHostPort(string target)
        {
            var parts = target.Split(':');
            if (parts.Length != 2 || !int.TryParse(parts[1], out var port))
                throw new ArgumentException($"Invalid target \"{target}\". Expected host:port (e.g. dc.example.com:636).");
            return (parts[0], port);
        }

        private static void PrintHelp()
        {
            Console.WriteLine(@"adrapi-ldap-cert-pin — manage trusted LDAPS server certificate pins.

Usage:
  adrapi-ldap-cert-pin <host:port> [--store <path>] [--note ""text""] [--yes]
  adrapi-ldap-cert-pin --list [--store <path>]
  adrapi-ldap-cert-pin --remove <host> <sha256> [--store <path>]

Options:
  --store <path>   Pin store file (default: cfg/ldap-trusted-certs.json)
  --note ""text""    Free-text note attached to the new pin
  --yes            Skip the interactive confirmation prompt
  --list           List all pins in the store
  --remove h s     Remove the pin for host h with SHA-256 thumbprint s
  -h, --help       Show this help");
        }

        private class Args
        {
            public string Target;
            public string StorePath;
            public string Note;
            public bool AssumeYes;
            public bool List;
            public string RemoveHost;
            public string RemoveSha;
            public bool ShowHelp;

            public static Args Parse(string[] argv)
            {
                var a = new Args();
                for (int i = 0; i < argv.Length; i++)
                {
                    var arg = argv[i];
                    switch (arg)
                    {
                        case "-h":
                        case "--help":
                            a.ShowHelp = true; break;
                        case "--store":
                            a.StorePath = argv[++i]; break;
                        case "--note":
                            a.Note = argv[++i]; break;
                        case "--yes":
                        case "-y":
                            a.AssumeYes = true; break;
                        case "--list":
                            a.List = true; break;
                        case "--remove":
                            a.RemoveHost = argv[++i];
                            if (i + 1 < argv.Length && !argv[i + 1].StartsWith("--"))
                                a.RemoveSha = argv[++i];
                            break;
                        default:
                            if (arg.StartsWith("--"))
                                throw new ArgumentException($"Unknown option: {arg}");
                            if (a.Target == null) a.Target = arg;
                            else throw new ArgumentException($"Unexpected argument: {arg}");
                            break;
                    }
                }
                return a;
            }
        }
    }
}
