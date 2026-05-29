using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Xunit;
using adrapi;
using adrapi.domain.Security;
using adrapi.Security;

namespace tests.Authentication
{
    /// <summary>
    /// Boots the real adrapi <see cref="Program"/> in-process via
    /// <see cref="WebApplicationFactory{TEntryPoint}"/>, with:
    ///   • an isolated temp SQLite store pre-seeded with known API keys
    ///   • a permissive rate-limit so auth tests don't trip the limiter
    ///   • a format-valid but unreachable LDAP target so the host starts
    ///     (LDAP isn't needed — auth tests only care about 401/403 vs anything else)
    ///
    /// Three test keys are seeded:
    ///   admin-test    isAdministrator   authorizedIP=0.0.0.0  (wildcard)
    ///   monitor-test  isMonitor         authorizedIP=0.0.0.0
    ///   wrong-ip-test isAdministrator   authorizedIP=192.0.2.99 (TEST-NET-1, never matches)
    /// </summary>
    public class AuthEndpointFixture : WebApplicationFactory<Program>, IAsyncLifetime
    {
        public string TempDir { get; }
        public string DbPath => Path.Combine(TempDir, "api-keys.db");
        public string SeedPath => Path.Combine(TempDir, ".seed");

        public const string AdminKeyId = "admin-test";
        public const string AdminSecret = "secret-admin-1234567890";
        public const string MonitorKeyId = "monitor-test";
        public const string MonitorSecret = "secret-monitor-1234567890";
        public const string WrongIpKeyId = "wrong-ip-test";
        public const string WrongIpSecret = "secret-wip-1234567890";

        public AuthEndpointFixture()
        {
            TempDir = Path.Combine(Path.GetTempPath(), "adrapi-authtest-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(TempDir);
        }

        public Task InitializeAsync()
        {
            // Seed the store BEFORE the host boots so ApiKeyManager can read it.
            var store = new ApiKeyStore(DbPath);
            store.Insert(new ApiKey
            {
                keyID = AdminKeyId,
                authorizedIP = "0.0.0.0",
                claims = new List<string> { "isAdministrator" },
            }, plaintextSecret: AdminSecret);
            store.Insert(new ApiKey
            {
                keyID = MonitorKeyId,
                authorizedIP = "0.0.0.0",
                claims = new List<string> { "isMonitor" },
            }, plaintextSecret: MonitorSecret);
            store.Insert(new ApiKey
            {
                keyID = WrongIpKeyId,
                authorizedIP = "192.0.2.99",
                claims = new List<string> { "isAdministrator" },
            }, plaintextSecret: WrongIpSecret);
            return Task.CompletedTask;
        }

        public new Task DisposeAsync()
        {
            base.Dispose();
            try { Directory.Delete(TempDir, recursive: true); } catch { }
            return Task.CompletedTask;
        }

        protected override IHost CreateHost(IHostBuilder builder)
        {
            // "Development" so Startup wires up UseDeveloperExceptionPage and
            // unhandled exceptions from the controller layer become 500s
            // instead of being re-thrown by TestServer back into the test.
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((ctx, b) =>
            {
                b.AddInMemoryCollection(new Dictionary<string, string>
                {
                    ["security:databaseFile"] = DbPath,
                    ["security:seedFile"] = SeedPath,
                    ["security:legacyJsonFile"] = Path.Combine(TempDir, "no-such-legacy.json"),
                    ["ldap:servers:0"] = "127.0.0.1:1",
                    ["ldap:ssl"] = "false",
                    ["ldap:poolSize"] = "1",
                    ["ldap:bindDn"] = "cn=fake,dc=test",
                    ["ldap:bindCredentials"] = "fake",
                    ["ldap:searchBase"] = "dc=test",
                    ["ldap:searchFilter"] = "",
                    ["ldap:maxResults"] = "10",
                    ["ldap:adminCn"] = "",
                    // A second directory so domain-prefixed routes (/api/lab/...) resolve.
                    ["ldap:domains:lab:servers:0"] = "127.0.0.1:1",
                    ["ldap:domains:lab:ssl"] = "false",
                    ["ldap:domains:lab:poolSize"] = "1",
                    ["ldap:domains:lab:bindDn"] = "cn=fake,dc=lab",
                    ["ldap:domains:lab:bindCredentials"] = "fake",
                    ["ldap:domains:lab:searchBase"] = "dc=lab",
                    ["ldap:domains:lab:searchFilter"] = "",
                    ["ldap:domains:lab:maxResults"] = "10",
                    ["ldap:domains:lab:adminCn"] = "",
                    ["rateLimit:auth:permitLimit"] = "9999",
                    ["rateLimit:auth:windowSeconds"] = "60",
                });
            });
            return base.CreateHost(builder);
        }
    }
}
