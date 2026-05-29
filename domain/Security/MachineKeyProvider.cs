using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace adrapi.domain.Security
{
    /// <summary>
    /// Derives a stable 256-bit symmetric key from a machine-bound identifier
    /// (best-effort per OS) blended with a random seed generated on first run of
    /// the management tool. The seed is stored at <c>SeedPath</c> with 0600
    /// permissions on Unix; together with the machine id it means a copy of the
    /// SQLite store alone is not enough to decrypt the data — an attacker also
    /// needs the seed file AND the original host.
    /// </summary>
    public class MachineKeyProvider
    {
        private const string HkdfInfo = "adrapi-app-secrets-v1";
        public const int SeedLength = 32;
        public const int KeyLength = 32;

        public string SeedPath { get; }

        public MachineKeyProvider(string seedPath)
        {
            if (string.IsNullOrWhiteSpace(seedPath))
                throw new ArgumentException("seedPath required", nameof(seedPath));
            SeedPath = seedPath;
        }

        /// <summary>
        /// Returns the AES-256 key, generating the seed file the first time
        /// it's called. Subsequent calls reuse the existing seed.
        /// </summary>
        public byte[] GetOrCreateKey()
        {
            var seed = LoadOrCreateSeed();
            var machineId = Encoding.UTF8.GetBytes(GetMachineId());
            return HKDF.DeriveKey(
                HashAlgorithmName.SHA256,
                ikm: machineId,
                outputLength: KeyLength,
                salt: seed,
                info: Encoding.UTF8.GetBytes(HkdfInfo));
        }

        /// <summary>
        /// True when a seed file has already been generated. The management tool
        /// checks this on startup to decide whether it's a first-run scenario.
        /// </summary>
        public bool SeedExists() => File.Exists(SeedPath);

        private byte[] LoadOrCreateSeed()
        {
            if (File.Exists(SeedPath))
            {
                var data = File.ReadAllBytes(SeedPath);
                if (data.Length != SeedLength)
                    throw new InvalidOperationException(
                        $"Seed file {SeedPath} is corrupt (expected {SeedLength} bytes, got {data.Length}).");
                return data;
            }

            var dir = Path.GetDirectoryName(SeedPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var seed = RandomNumberGenerator.GetBytes(SeedLength);
            File.WriteAllBytes(SeedPath, seed);

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                try { File.SetUnixFileMode(SeedPath, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
                catch { /* best-effort; not all filesystems support it */ }
            }
            return seed;
        }

        /// <summary>
        /// Best-effort cross-platform machine identifier. Falls back to a blend of
        /// hostname + OS string when no OS-native source is available.
        /// </summary>
        public static string GetMachineId()
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    foreach (var p in new[] { "/etc/machine-id", "/var/lib/dbus/machine-id" })
                        if (File.Exists(p)) return File.ReadAllText(p).Trim();
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    var psi = new ProcessStartInfo("ioreg", "-rd1 -c IOPlatformExpertDevice")
                    {
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                    };
                    using var proc = Process.Start(psi);
                    var output = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit(2000);
                    var m = Regex.Match(output, "IOPlatformUUID.*=.*\"([^\"]+)\"");
                    if (m.Success) return m.Groups[1].Value;
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    var psi = new ProcessStartInfo("reg",
                        @"query HKLM\SOFTWARE\Microsoft\Cryptography /v MachineGuid")
                    {
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    };
                    using var proc = Process.Start(psi);
                    var output = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit(2000);
                    var m = Regex.Match(output, @"MachineGuid\s+REG_SZ\s+([0-9a-fA-F-]+)");
                    if (m.Success) return m.Groups[1].Value;
                }
            }
            catch { /* fall through to default */ }

            return $"{Environment.MachineName}|{Environment.OSVersion}";
        }
    }
}
