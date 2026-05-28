using System;
using System.IO;
using System.Security.Cryptography;
using Xunit;
using adrapi.domain.Security;

namespace tests
{
    public class AppSecrets : IDisposable
    {
        private readonly string _dbPath;
        private readonly string _seedPath;

        public AppSecrets()
        {
            var dir = Path.Combine(Path.GetTempPath(), $"adrapi-test-{Guid.NewGuid():N}");
            Directory.CreateDirectory(dir);
            _dbPath = Path.Combine(dir, "secrets.db");
            _seedPath = Path.Combine(dir, ".seed");
        }

        public void Dispose()
        {
            try { Directory.Delete(Path.GetDirectoryName(_dbPath), recursive: true); } catch { }
        }

        [Fact]
        public void SecretBox_roundtrip_recoversPlaintext()
        {
            var key = RandomNumberGenerator.GetBytes(SecretBox.KeySize);
            var pt = System.Text.Encoding.UTF8.GetBytes("the quick brown fox");
            var envelope = SecretBox.Encrypt(key, pt);
            var back = SecretBox.Decrypt(key, envelope);
            Assert.Equal(pt, back);
        }

        [Fact]
        public void SecretBox_wrongKey_fails()
        {
            var key1 = RandomNumberGenerator.GetBytes(SecretBox.KeySize);
            var key2 = RandomNumberGenerator.GetBytes(SecretBox.KeySize);
            var envelope = SecretBox.Encrypt(key1, new byte[] { 1, 2, 3 });
            Assert.ThrowsAny<CryptographicException>(() => SecretBox.Decrypt(key2, envelope));
        }

        [Fact]
        public void SecretBox_tamperedEnvelope_fails()
        {
            var key = RandomNumberGenerator.GetBytes(SecretBox.KeySize);
            var envelope = SecretBox.Encrypt(key, new byte[] { 1, 2, 3 });
            envelope[envelope.Length - 1] ^= 0xFF; // corrupt the auth tag
            Assert.ThrowsAny<CryptographicException>(() => SecretBox.Decrypt(key, envelope));
        }

        [Fact]
        public void AppSecretsStore_storesAndReadsBackEncrypted()
        {
            var key = new MachineKeyProvider(_seedPath).GetOrCreateKey();
            var store = new AppSecretsStore(_dbPath, key);

            store.Set("ldap:bindCredentials", "p4ssw0rd!");
            Assert.Equal("p4ssw0rd!", store.Get("ldap:bindCredentials"));

            store.Set("ldap:bindCredentials", "rotated");
            Assert.Equal("rotated", store.Get("ldap:bindCredentials"));

            Assert.True(store.Remove("ldap:bindCredentials"));
            Assert.Null(store.Get("ldap:bindCredentials"));
        }

        [Fact]
        public void AppSecretsStore_listReturnsMetadataNotValues()
        {
            var key = new MachineKeyProvider(_seedPath).GetOrCreateKey();
            var store = new AppSecretsStore(_dbPath, key);
            store.Set("a", "alpha");
            store.Set("b", "bravo");
            var items = store.List();
            Assert.Equal(2, items.Count);
            Assert.Contains(items, i => i.Name == "a");
            Assert.Contains(items, i => i.Name == "b");
        }

        [Fact]
        public void MachineKeyProvider_seedIsStableAcrossInstances()
        {
            var p1 = new MachineKeyProvider(_seedPath);
            var k1 = p1.GetOrCreateKey();
            var p2 = new MachineKeyProvider(_seedPath);
            var k2 = p2.GetOrCreateKey();
            Assert.Equal(k1, k2);
        }
    }
}
