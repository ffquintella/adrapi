using System;
using System.Collections.Generic;
using System.IO;
using Xunit;
using adrapi.Security;
using adrapi.domain.Security;

namespace tests
{
    public class Security : IDisposable
    {
        private readonly string _dbPath;

        public Security()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"adrapi-test-{Guid.NewGuid():N}.db");
            var store = new ApiKeyStore(_dbPath);
            store.Insert(new ApiKey
            {
                keyID = "dev-local",
                authorizedIP = "127.0.0.1",
                claims = new List<string> { "isAdministrator" },
            }, plaintextSecret: "abc1234");
            ApiKeyManager.SetTestStore(store);
        }

        public void Dispose()
        {
            ApiKeyManager.SetTestStore(null);
            try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { /* best-effort */ }
        }

        [Fact]
        public void Authenticate_validKeyAndSecret_returnsRecord()
        {
            var key = ApiKeyManager.Authenticate("dev-local", "abc1234", true);

            Assert.NotNull(key);
            Assert.Equal("dev-local", key.keyID);
            Assert.Equal("127.0.0.1", key.authorizedIP);
            Assert.Contains("isAdministrator", key.claims);
            Assert.Null(key.secretKey); // store never returns the plaintext
        }

        [Fact]
        public void Authenticate_wrongSecret_returnsNull()
        {
            Assert.Null(ApiKeyManager.Authenticate("dev-local", "wrong", true));
        }

        [Fact]
        public void Authenticate_unknownKeyID_returnsNull()
        {
            Assert.Null(ApiKeyManager.Authenticate("nope", "abc1234", true));
        }
    }
}
