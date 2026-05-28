using System.IO;
using NLog;
using Microsoft.Extensions.Configuration;
using adrapi.domain.Security;

namespace adrapi.Security
{
    /// <summary>
    /// Facade over <see cref="ApiKeyStore"/>. Holds a singleton store initialized
    /// from configuration at startup, plus an independent store used by tests.
    /// </summary>
    public static class ApiKeyManager
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();
        private static readonly object _initLock = new object();
        private static ApiKeyStore _store;
        private static ApiKeyStore _testStore;

        public const string DefaultDatabasePath = "cfg/api-keys.db";

        public static ApiKeyStore Store
        {
            get
            {
                if (_store == null) Initialize(DefaultDatabasePath);
                return _store;
            }
        }

        public static void Initialize(string databasePath)
        {
            lock (_initLock)
            {
                _store = new ApiKeyStore(databasePath);
                logger.Info("API key store initialized: {path}", Path.GetFullPath(databasePath));
            }
        }

        public static void InitializeFromConfiguration(IConfiguration configuration)
        {
            var path = configuration.GetSection("security").GetValue<string>("databaseFile")
                ?? DefaultDatabasePath;
            Initialize(path);
        }

        public static void SetTestStore(ApiKeyStore store)
        {
            lock (_initLock) { _testStore = store; }
        }

        private static ApiKeyStore GetStore(bool isTest)
        {
            if (!isTest) return Store;
            if (_testStore == null)
            {
                lock (_initLock)
                {
                    _testStore ??= new ApiKeyStore("api-keys-tests.db");
                }
            }
            return _testStore;
        }

        /// <summary>
        /// Lookup by keyID + verify plaintext secret against the stored Argon2id
        /// hash. Returns the record on success (so claims/authorizedIP are usable
        /// without a second query), or null on any failure.
        /// </summary>
        public static ApiKey Authenticate(string keyId, string plaintextSecret, bool isTest = false)
            => GetStore(isTest).VerifyAndLoad(keyId, plaintextSecret);

        /// <summary>
        /// Lookup by keyID alone, no secret verification. The returned
        /// <see cref="ApiKey.secretKey"/> is always null — only the hash is stored.
        /// </summary>
        public static ApiKey Find(string keyId, bool isTest = false)
            => GetStore(isTest).FindByKeyId(keyId);
    }
}
