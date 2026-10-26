using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using adrapi;
using adrapi.domain.Security;
using adrapi.Security;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace tests;

public sealed class LdapCertificatePinStoreSecurityCoverageTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), $"adrapi-pin-coverage-{Guid.NewGuid():N}");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_rejects_missing_path(string path)
    {
        Assert.Throws<ArgumentException>(() => new LdapCertificatePinStore(path));
    }

    [Fact]
    public void Load_returns_empty_for_missing_blank_and_null_documents()
    {
        var path = Path.Combine(_directory, "pins.json");
        var store = new LdapCertificatePinStore(path);
        Assert.Empty(store.Load());

        Directory.CreateDirectory(_directory);
        File.WriteAllText(path, "  \n");
        Assert.Empty(store.Load());

        File.WriteAllText(path, "null");
        Assert.Empty(store.Load());

        File.WriteAllText(path, "{\"pins\":null}");
        Assert.Empty(store.Load());
    }

    [Fact]
    public void Save_and_load_round_trip_pin_metadata_and_create_parent_directory()
    {
        var path = Path.Combine(_directory, "nested", "pins.json");
        var store = new LdapCertificatePinStore(path);
        var added = new DateTime(2025, 4, 5, 6, 7, 8, DateTimeKind.Utc);

        store.Save(new[]
        {
            new LdapCertificatePin
            {
                Host = "ldap.example.test",
                Sha256 = "aa:bb",
                Subject = "CN=ldap.example.test",
                Issuer = "CN=Test CA",
                NotBefore = added.AddDays(-1),
                NotAfter = added.AddDays(30),
                AddedAt = added,
                Note = "rotation candidate",
            },
        });

        var pin = Assert.Single(store.Load());
        Assert.Equal("ldap.example.test", pin.Host);
        Assert.Equal("aa:bb", pin.Sha256);
        Assert.Equal("CN=ldap.example.test", pin.Subject);
        Assert.Equal("CN=Test CA", pin.Issuer);
        Assert.Equal(added, pin.AddedAt);
        Assert.Equal("rotation candidate", pin.Note);
    }

    [Fact]
    public void Trust_matching_normalizes_thumbprint_and_ignores_host_case()
    {
        var store = Store();
        store.Save(new[] { new LdapCertificatePin { Host = "LDAP.EXAMPLE.TEST", Sha256 = "AA-BB CC" } });

        Assert.True(store.IsTrusted("ldap.example.test", "aa:bbcc"));
        Assert.False(store.IsTrusted("other.example.test", "aa:bbcc"));
        Assert.False(store.IsTrusted(null, "aa:bb"));
        Assert.False(store.IsTrusted("ldap.example.test", null));
    }

    [Fact]
    public void Add_normalizes_deduplicates_and_preserves_explicit_timestamp()
    {
        var store = Store();
        store.Add(new LdapCertificatePin { Host = "ldap", Sha256 = "AA-BB" });

        var first = Assert.Single(store.Load());
        Assert.Equal("aa:bb", first.Sha256);
        Assert.NotEqual(default, first.AddedAt);

        var explicitTime = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        store.Add(new LdapCertificatePin
        {
            Host = "LDAP",
            Sha256 = "aa:bb",
            AddedAt = explicitTime,
            Note = "replacement",
        });

        var replacement = Assert.Single(store.Load());
        Assert.Equal(explicitTime, replacement.AddedAt);
        Assert.Equal("replacement", replacement.Note);
        Assert.Throws<ArgumentNullException>(() => store.Add(null));
    }

    [Fact]
    public void Remove_persists_only_matching_pin()
    {
        var store = Store();
        store.Save(new[]
        {
            new LdapCertificatePin { Host = "one", Sha256 = "aa-bb" },
            new LdapCertificatePin { Host = "two", Sha256 = "cc:dd" },
        });

        Assert.False(store.Remove("missing", "aa:bb"));
        Assert.True(store.Remove("ONE", "AA-BB"));
        Assert.Equal("two", Assert.Single(store.Load()).Host);
    }

    [Fact]
    public void ComputeSha256_returns_colon_delimited_certificate_digest()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=coverage.example.test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));

        var expected = string.Join(":", SHA256.HashData(certificate.RawData).Select(b => b.ToString("x2")));
        Assert.Equal(expected, LdapCertificatePinStore.ComputeSha256(certificate));
        Assert.Throws<ArgumentNullException>(() => LdapCertificatePinStore.ComputeSha256(null));
    }

    private LdapCertificatePinStore Store() =>
        new(Path.Combine(_directory, "pins.json"));

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}

public sealed class MachineKeyProviderSecurityCoverageTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), $"adrapi-key-coverage-{Guid.NewGuid():N}");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Constructor_rejects_missing_seed_path(string path)
    {
        Assert.Throws<ArgumentException>(() => new MachineKeyProvider(path));
    }

    [Fact]
    public void GetOrCreateKey_creates_private_seed_and_reuses_it()
    {
        var path = Path.Combine(_directory, "nested", ".seed");
        var firstProvider = new MachineKeyProvider(path);
        Assert.False(firstProvider.SeedExists());

        var first = firstProvider.GetOrCreateKey();
        var second = new MachineKeyProvider(path).GetOrCreateKey();

        Assert.Equal(MachineKeyProvider.KeyLength, first.Length);
        Assert.Equal(first, second);
        Assert.Equal(MachineKeyProvider.SeedLength, File.ReadAllBytes(path).Length);
        Assert.True(firstProvider.SeedExists());
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(
                UnixFileMode.UserRead | UnixFileMode.UserWrite,
                File.GetUnixFileMode(path) & (UnixFileMode.UserRead | UnixFileMode.UserWrite |
                    UnixFileMode.GroupRead | UnixFileMode.OtherRead));
        }
    }

    [Fact]
    public void GetOrCreateKey_rejects_corrupt_seed()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, ".seed");
        File.WriteAllBytes(path, new byte[] { 1, 2, 3 });

        var error = Assert.Throws<InvalidOperationException>(
            () => new MachineKeyProvider(path).GetOrCreateKey());
        Assert.Contains("expected 32 bytes, got 3", error.Message);
    }

    [Fact]
    public void Machine_id_is_nonempty_on_the_current_platform()
    {
        Assert.False(string.IsNullOrWhiteSpace(MachineKeyProvider.GetMachineId()));
    }

    [Fact]
    public void Machine_id_uses_linux_sources_in_order_and_trims_content()
    {
        var machineId = MachineKeyProvider.GetMachineId(
            platform => platform == OSPlatform.Linux,
            path => path == "/var/lib/dbus/machine-id",
            _ => "  linux-id-2\n",
            (_, _) => throw new InvalidOperationException("process should not run"),
            () => "fallback");

        Assert.Equal("linux-id-2", machineId);
    }

    [Fact]
    public void Machine_id_parses_macos_and_windows_outputs()
    {
        var mac = MachineKeyProvider.GetMachineId(
            platform => platform == OSPlatform.OSX,
            _ => false,
            _ => throw new InvalidOperationException(),
            (file, args) => file == "ioreg" && args.Contains("IOPlatformExpertDevice")
                ? "    \"IOPlatformUUID\" = \"ABCD-1234\""
                : "",
            () => "fallback");
        var windows = MachineKeyProvider.GetMachineId(
            platform => platform == OSPlatform.Windows,
            _ => false,
            _ => throw new InvalidOperationException(),
            (file, args) => file == "reg" && args.Contains("MachineGuid")
                ? "MachineGuid    REG_SZ    01234567-89ab-cdef-0123-456789abcdef"
                : "",
            () => "fallback");

        Assert.Equal("ABCD-1234", mac);
        Assert.Equal("01234567-89ab-cdef-0123-456789abcdef", windows);
    }

    [Fact]
    public void Machine_id_falls_back_for_unparseable_output_or_probe_failure()
    {
        var unparseable = MachineKeyProvider.GetMachineId(
            platform => platform == OSPlatform.OSX,
            _ => false,
            _ => "",
            (_, _) => "no uuid here",
            () => "host|os");
        var failedProbe = MachineKeyProvider.GetMachineId(
            _ => throw new IOException("probe failed"),
            _ => false,
            _ => "",
            (_, _) => "",
            () => "host|os");

        Assert.Equal("host|os", unparseable);
        Assert.Equal("host|os", failedProbe);
    }

    [Fact]
    public void Process_probe_captures_standard_output()
    {
        var output = OperatingSystem.IsWindows()
            ? MachineKeyProvider.RunProcess("cmd", "/c echo machine-output")
            : MachineKeyProvider.RunProcess("/bin/echo", "machine-output");

        Assert.Equal("machine-output", output.Trim());
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}

public sealed class ApiKeyStoreSecurityCoverageTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), $"adrapi-api-key-coverage-{Guid.NewGuid():N}");
    private string DatabasePath => Path.Combine(_directory, "nested", "keys.db");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Constructor_rejects_missing_database_path(string path)
    {
        Assert.Throws<ArgumentException>(() => new ApiKeyStore(path));
    }

    [Fact]
    public void New_store_creates_parent_and_starts_empty()
    {
        var store = new ApiKeyStore(DatabasePath);

        Assert.True(File.Exists(DatabasePath));
        Assert.Empty(store.List());
        Assert.Null(store.FindByKeyId(null));
        Assert.Null(store.FindByKeyId("missing"));
    }

    [Fact]
    public void Insert_validates_required_values()
    {
        var store = new ApiKeyStore(DatabasePath);
        Assert.Throws<ArgumentNullException>(() => store.Insert(null));
        Assert.Throws<ArgumentException>(() => store.Insert(new ApiKey()));
        Assert.Throws<ArgumentException>(() => store.Insert(new ApiKey { keyID = "missing-secret" }));
    }

    [Fact]
    public void Insert_hashes_plaintext_and_round_trips_record_without_plaintext()
    {
        var store = new ApiKeyStore(DatabasePath);
        var created = new DateTime(2025, 2, 3, 4, 5, 6, DateTimeKind.Utc);
        store.Insert(new ApiKey
        {
            keyID = "admin",
            authorizedIP = "192.0.2.8",
            claims = new List<string> { "isAdministrator" },
            createdAt = created,
            secretKey = "must-not-persist",
        }, "correct horse battery staple");

        var stored = store.FindByKeyId("admin");
        Assert.Equal("admin", stored.keyID);
        Assert.Equal("192.0.2.8", stored.authorizedIP);
        Assert.Equal(new[] { "isAdministrator" }, stored.claims);
        Assert.Equal(created, stored.createdAt?.ToUniversalTime());
        Assert.Null(stored.secretKey);
        Assert.StartsWith("$argon2id$", stored.secretHash);
        Assert.True(ApiKeyHasher.Verify("correct horse battery staple", stored.secretHash));
    }

    [Fact]
    public void Insert_preserves_existing_hash_and_applies_safe_defaults()
    {
        var store = new ApiKeyStore(DatabasePath);
        var hash = ApiKeyHasher.Hash("already hashed");
        store.Insert(new ApiKey { keyID = "defaults", secretHash = hash });

        var stored = store.FindByKeyId("defaults");
        Assert.Equal(hash, stored.secretHash);
        Assert.Equal("", stored.authorizedIP);
        Assert.Empty(stored.claims);
        Assert.NotNull(stored.createdAt);
    }

    [Fact]
    public void Hasher_rejects_malformed_encoding_and_generates_url_safe_secrets()
    {
        Assert.False(ApiKeyHasher.Verify(
            "secret",
            "$argon2id$v=19$m=8,t=1,p=1$not-base64!$also-not-base64!"));

        var first = ApiKeyHasher.GenerateSecret(48);
        var second = ApiKeyHasher.GenerateSecret(48);
        Assert.Equal(64, first.Length);
        Assert.DoesNotContain('+', first);
        Assert.DoesNotContain('/', first);
        Assert.DoesNotContain('=', first);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void List_orders_records_and_handles_blank_claims_and_invalid_dates()
    {
        var store = new ApiKeyStore(DatabasePath);
        InsertRaw("z", "[]", "invalid-created", "invalid-last-used");
        InsertRaw("a", " ", "invalid-created", null);

        var records = store.List();
        Assert.Equal(new[] { "a", "z" }, records.Select(x => x.keyID));
        Assert.Empty(records[0].claims);
        Assert.Null(records[0].createdAt);
        Assert.Null(records[0].lastUsedAt);
        Assert.Null(records[1].createdAt);
        Assert.Null(records[1].lastUsedAt);
    }

    [Fact]
    public void VerifyAndLoad_rejects_unknown_and_wrong_secret_then_touches_success()
    {
        var store = new ApiKeyStore(DatabasePath);
        store.Insert(new ApiKey { keyID = "verify" }, "right");

        Assert.Null(store.VerifyAndLoad("missing", "right"));
        Assert.Null(store.VerifyAndLoad("verify", "wrong"));
        Assert.Null(store.FindByKeyId("verify").lastUsedAt);
        Assert.Equal("verify", store.VerifyAndLoad("verify", "right").keyID);
        Assert.NotNull(store.FindByKeyId("verify").lastUsedAt);
    }

    [Fact]
    public void Rotate_changes_existing_secret_and_reports_missing_key()
    {
        var store = new ApiKeyStore(DatabasePath);
        store.Insert(new ApiKey { keyID = "rotate" }, "before");

        Assert.Throws<ArgumentException>(() => store.Rotate("rotate", ""));
        Assert.True(store.Rotate("rotate", "after"));
        Assert.Null(store.VerifyAndLoad("rotate", "before"));
        Assert.NotNull(store.VerifyAndLoad("rotate", "after"));
        Assert.False(store.Rotate("missing", "valid"));
    }

    [Fact]
    public void Remove_reports_whether_a_record_was_deleted()
    {
        var store = new ApiKeyStore(DatabasePath);
        store.Insert(new ApiKey { keyID = "remove" }, "secret");

        Assert.True(store.Remove("remove"));
        Assert.False(store.Remove("remove"));
    }

    private void InsertRaw(string id, string claims, string created, string lastUsed)
    {
        using var connection = new SqliteConnection($"Data Source={DatabasePath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO api_keys
                (key_id, secret_hash, authorized_ip, claims_json, created_at, last_used_at)
            VALUES ($id, 'hash', '', $claims, $created, $lastUsed)
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$claims", claims);
        command.Parameters.AddWithValue("$created", created);
        command.Parameters.AddWithValue("$lastUsed", (object)lastUsed ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}

public sealed class SqliteSecretsConfigurationSecurityCoverageTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), $"adrapi-config-coverage-{Guid.NewGuid():N}");
    private string DatabasePath => Path.Combine(_directory, "secrets.db");
    private string SeedPath => Path.Combine(_directory, ".seed");

    [Fact]
    public void Provider_loads_encrypted_secrets_case_insensitively()
    {
        var key = new MachineKeyProvider(SeedPath).GetOrCreateKey();
        var store = new AppSecretsStore(DatabasePath, key);
        store.Set("Directories:Domains:Lab:Entra:ClientSecret", "not-plaintext-on-disk");

        var builder = new ConfigurationBuilder();
        Assert.Same(builder, builder.AddSqliteSecrets(DatabasePath, SeedPath, optional: false));
        var configuration = builder.Build();

        Assert.Equal("not-plaintext-on-disk",
            configuration["directories:domains:lab:entra:clientsecret"]);
        Assert.DoesNotContain(
            "not-plaintext-on-disk",
            System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(DatabasePath)));
    }

    [Fact]
    public void Missing_database_is_ignored_only_when_optional()
    {
        var optional = new ConfigurationBuilder()
            .AddSqliteSecrets(DatabasePath, SeedPath)
            .Build();
        Assert.Null(optional["anything"]);

        Assert.Throws<FileNotFoundException>(() => new ConfigurationBuilder()
            .AddSqliteSecrets(DatabasePath, SeedPath, optional: false)
            .Build());
    }

    [Fact]
    public void Missing_seed_is_ignored_only_when_optional()
    {
        Directory.CreateDirectory(_directory);
        _ = new AppSecretsStore(DatabasePath, RandomNumberGenerator.GetBytes(32));

        var optional = new ConfigurationBuilder()
            .AddSqliteSecrets(DatabasePath, SeedPath)
            .Build();
        Assert.Null(optional["anything"]);

        Assert.Throws<InvalidOperationException>(() => new ConfigurationBuilder()
            .AddSqliteSecrets(DatabasePath, SeedPath, optional: false)
            .Build());
    }

    [Fact]
    public void Corrupt_seed_or_database_is_swallowed_only_when_optional()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllBytes(DatabasePath, new byte[] { 1, 2, 3 });
        File.WriteAllBytes(SeedPath, new byte[] { 1, 2, 3 });

        var optional = new ConfigurationBuilder()
            .AddSqliteSecrets(DatabasePath, SeedPath)
            .Build();
        Assert.Null(optional["anything"]);

        Assert.Throws<InvalidOperationException>(() => new ConfigurationBuilder()
            .AddSqliteSecrets(DatabasePath, SeedPath, optional: false)
            .Build());
    }

    [Fact]
    public void Source_builds_provider_with_its_settings()
    {
        var source = new SqliteSecretsConfigurationSource
        {
            DatabasePath = DatabasePath,
            SeedPath = SeedPath,
            Optional = true,
        };

        var provider = Assert.IsType<SqliteSecretsConfigurationProvider>(
            source.Build(new ConfigurationBuilder()));
        provider.Load();
        Assert.False(provider.TryGet("missing", out _));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}

public sealed class ApiKeyMigrationSecurityCoverageTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), $"adrapi-migration-coverage-{Guid.NewGuid():N}");
    private string DatabasePath => Path.Combine(_directory, "keys.db");
    private string LegacyPath => Path.Combine(_directory, "security.json");

    [Fact]
    public void Missing_or_empty_legacy_file_does_not_open_store_or_archive_file()
    {
        Directory.CreateDirectory(_directory);
        var configuration = Configuration();
        var opened = false;
        Func<ApiKeyStore> store = () =>
        {
            opened = true;
            return new ApiKeyStore(DatabasePath);
        };

        ApiKeyMigration.ImportLegacyJsonIfPresent(configuration, store, () => DateTime.UtcNow);
        File.WriteAllText(LegacyPath, "[]");
        ApiKeyMigration.ImportLegacyJsonIfPresent(configuration, store, () => DateTime.UtcNow);

        Assert.False(opened);
        Assert.True(File.Exists(LegacyPath));
        Assert.Empty(Directory.GetFiles(_directory, "security.json.imported.*"));
    }

    [Fact]
    public void Public_import_entry_point_does_nothing_when_source_is_missing()
    {
        Directory.CreateDirectory(_directory);

        ApiKeyMigration.ImportLegacyJsonIfPresent(Configuration());

        Assert.False(File.Exists(DatabasePath));
    }

    [Fact]
    public void Import_hashes_valid_secrets_skips_invalid_and_existing_then_archives_source()
    {
        Directory.CreateDirectory(_directory);
        var store = new ApiKeyStore(DatabasePath);
        store.Insert(new ApiKey { keyID = "existing" }, "kept");
        File.WriteAllText(LegacyPath, """
            [
              { "keyID": "new", "secretKey": "migrate-me", "authorizedIP": "192.0.2.20",
                "claims": ["isMonitor"] },
              { "keyID": "existing", "secretKey": "replace-me" },
              { "keyID": "missing-secret" },
              { "secretKey": "missing-id" }
            ]
            """);
        var now = new DateTime(2025, 6, 7, 8, 9, 10, DateTimeKind.Utc);

        ApiKeyMigration.ImportLegacyJsonIfPresent(
            Configuration(), () => store, () => now);

        Assert.False(File.Exists(LegacyPath));
        Assert.True(File.Exists(LegacyPath + ".imported.20250607080910"));
        Assert.NotNull(store.VerifyAndLoad("new", "migrate-me"));
        Assert.Null(store.VerifyAndLoad("new", "wrong"));
        Assert.NotNull(store.VerifyAndLoad("existing", "kept"));
        Assert.Null(store.FindByKeyId("missing-secret"));
        Assert.Equal("192.0.2.20", store.FindByKeyId("new").authorizedIP);
        Assert.Equal(new[] { "isMonitor" }, store.FindByKeyId("new").claims);
    }

    [Fact]
    public void Invalid_json_leaves_plaintext_file_in_place_and_store_untouched()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(LegacyPath, "not json");
        var store = new ApiKeyStore(DatabasePath);

        ApiKeyMigration.ImportLegacyJsonIfPresent(
            Configuration(), () => store, () => DateTime.UtcNow);

        Assert.True(File.Exists(LegacyPath));
        Assert.Empty(store.List());
        Assert.Empty(Directory.GetFiles(_directory, "security.json.imported.*"));
    }

    private IConfiguration Configuration() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string>
        {
            ["security:legacyJsonFile"] = LegacyPath,
        })
        .Build();

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
