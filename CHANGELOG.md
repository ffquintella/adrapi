# RELEASE NOTES

## Unreleased

### Added
- **SQLite-backed API key store with Argon2id hashing.** API keys are now read
  from `cfg/api-keys.db` (configurable via `security:databaseFile`). Only
  Argon2id PHC-encoded hashes (OWASP 2024 params: 19 MiB / t=2 / p=1) are
  persisted — plaintext secrets are never stored.
- **`tools/AdrapiApiKeys` CLI** for managing the store, redesigned for
  interactive use: grouped `key` and `secret` commands, prompts with hidden
  input for secrets, confirmation prompts for destructive ops, colorized
  success/error markers, and a boxed banner highlighting the printed secret
  on `key add` / `key rotate` (the only moments a plaintext secret is shown).
  Backward-compatible flat aliases (`add`, `list`, `rotate`, etc.) preserved.
- **Encrypted application secrets** (`app_secrets` table in the same SQLite DB):
  LDAP bind credentials and the HTTPS certificate password move out of
  `IConfiguration` JSON/user-secrets into AEAD-encrypted rows. Algorithm:
  ChaCha20-Poly1305 (RFC 8439, 256-bit key, quantum-safe at 128-bit Grover
  margin). The key is derived via HKDF-SHA256 from the machine ID blended
  with a 32-byte seed generated on first run of the CLI tool (path
  configurable via `security:seedFile`, default `cfg/.seed`, mode 0600 on Unix).
- **`SqliteSecretsConfigurationProvider`** wires the encrypted secrets into the
  standard `IConfiguration` pipeline (between user-secrets and env vars), so
  consumers like `LdapConfig` keep reading values via `IConfiguration["ldap:bindDn"]`
  without changes.
- **`secret import-ldap`** CLI subcommand pulls `ldap:bindDn`,
  `ldap:bindCredentials`, and `certificate:password` out of a chosen JSON file
  (appsettings, user-secrets, etc.) into the encrypted store in one shot.
- **One-shot migration** from legacy `security.json` runs on startup: keys are
  imported (hashed), then the source file is renamed to
  `security.json.imported.<timestamp>`.
- **Test coverage** for SecretBox round-trip, tampered/wrong-key failure modes,
  AppSecretsStore CRUD, and seed stability across MachineKeyProvider instances.

### Changed
- `BasicAuthenticationHandler` now splits `api-key: keyID:secret`, looks up the
  record by `keyID`, and verifies the secret against the stored Argon2id hash
  in constant time. Unauthorized IP returns 401 with a structured warning log.
- Test suite no longer reads `tests/security-tests.json` — each test creates an
  ephemeral SQLite database and seeds a known key via the new store API.

### Removed
- `adrapi/Security/HttpSecurity.cs` (only consumer was the auth handler, which
  now reads claims directly from the verified record).
- `adrapi/Security/KeyAuthenticationMiddleware.cs` (dead code; it was already
  commented out of the pipeline and not callable from anywhere).
- `ApiKeyManager.FindBySecretKey` (no longer makes sense with hashed storage).
- `<Content Update="security.json">` from `adrapi.csproj` (file no longer
  exists or needs copying to the build output).

## V1.5.0 — Security hardening

### Added
- **LDAPS certificate pinning.** New `LdapCertificateValidator` accepts a server
  certificate only if it chains to a trusted root in the system CA store, or if
  its SHA-256 thumbprint is present in the pin store for that host. Replaces the
  previous "accept any certificate" callback. Configurable via
  `ldap:trustedCertificatesFile` (default `cfg/ldap-trusted-certs.json`).
- **`tools/AdrapiLdapCertPin`** — interactive console helper that fetches an
  LDAPS server's certificate, prints subject/issuer/validity/SHA-256, prompts
  the operator, and writes the pin to the store. Subcommands: pin, `--list`,
  `--remove`, `--yes` (non-interactive), `--note`, `--store`.
- **User Secrets (`dotnet user-secrets`) wired into the config pipeline** for
  Development. Secrets layer on top of `appsettings.{Env}.json`, below env vars
  and command-line. New `UserSecretsId` registered on `adrapi.csproj`.
- **`.example` templates** for `adrapi/security.json` and
  `docker/Settings/security.json` so new environments can bootstrap without a
  tracked secrets file.
- **Rate-limiting on authentication endpoints.** ASP.NET Core sliding-window
  limiter (`AuthEndpoint` policy) partitioned by `client-ip + authenticated
  keyID`. Default 5 requests / 60 s. Returns `429 Too Many Requests` with
  `Retry-After` and emits a structured warning. Configurable via
  `rateLimit:auth` in `appsettings.json`.

### Changed
- `Program.cs` configuration builder now layers `appsettings.json` →
  `appsettings.{Environment}.json` → User Secrets (Dev only) → environment
  variables → command line. Environment detected via `ASPNETCORE_ENVIRONMENT` /
  `DOTNET_ENVIRONMENT` with DEBUG/Release fallback.
- `[Authorize(Policy = "Reading")]` is now explicit per-method on the four
  authenticate endpoints (V1 and V2). Behavior unchanged — class-level
  `[Authorize]` already covered them — but explicit annotation prevents silent
  regression.
- LDAP bind credentials and the HTTPS certificate password no longer live in
  `appsettings.Development.json`; expected from User Secrets (Dev) or
  environment variables / secret manager (Prod).

### Removed
- `adrapi/Ldap/Security/LdapSSLHelper.cs` (insecure
  `HandleRemoteCertificateValidationCallback => true` and `LocalSSLHandler`
  that imported any presented cert into the trust store).
- `adrapi/security.json` and `docker/Settings/security.json` from version
  control. **History was rewritten** with `git filter-repo` to scrub these
  files and the leaked secrets from all 189 prior commits (including
  `adrapi/api/devapi.paw` which also contained an API key). Force-pushed to
  `origin/develop` and `origin/master`.
- Debug log line in `ApiKeyManager` that dumped the entire API-key file
  contents.

### Security
- **Action required:** rotate all API keys that were previously committed
  (`NSdjfWiK238b94`, `d6a0f2b1-dc8e-4216-91bd-ba941d99848f`,
  `86d3c3f3-c0c9-4abc-a8f0-4f2f9ed39b59`) and the LDAP `adreader` bind
  credential. They were present in the local file or git history and must be
  considered compromised even though the repo is now clean.
- After rotation, recreate `security.json` from `security.json.example` with
  fresh values; mount via secret manager in production.
- Forks, existing clones and any pre-existing PRs still hold the old history
  and need to be reset / re-cloned.

## V1.0 TBD

## V0.6 - Rewrite to use DirectoryServices DLL (planed)

## V0.5.1 - Automatic build with nuke.build e new api contract

