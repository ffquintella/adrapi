# adrapi

Active Directory REST API for querying and managing users, groups, and OUs over LDAP.

## Current Runtime/Tooling

- SDK used in this workspace: .NET `10.0.101`
- Application target framework: `net10.0`
- Test target framework: `net10.0`

## What This Service Does

- Exposes versioned REST endpoints for:
- Users
- Groups
- Organizational Units (OUs)
- Performs LDAP-backed read/write operations
- Uses API-key based auth + claims authorization (`isAdministrator`, `isMonitor`)
- Publishes OpenAPI/Swagger docs

## Project Structure

- `adrapi/`: Web API host, controllers, LDAP integration, security middleware, managers
- `domain/`: Domain models and custom exceptions
- `tests/`: Test project and environment-specific test settings
- `build/`: NUKE build tooling

## Configuration

Main runtime configuration lives in:

- `adrapi/appsettings.json`
- `adrapi/appsettings.Development.json` (gitignored — local overrides only, **no secrets**)
- `adrapi/security.json`

Configuration sources are layered in this order (later wins):

1. `appsettings.json`
2. `appsettings.{Environment}.json`
3. **User Secrets** (Development environment only — see below)
4. Environment variables
5. Command-line arguments

Important settings:

- `ldap`: LDAP servers and connection details
- `certificate:file` / `certificate:password`: HTTPS certificate for Kestrel
- `AllowedHosts`: Host binding behavior (`*` maps to `0.0.0.0` in startup)

### Local Secrets (Development)

LDAP bind credentials, the HTTPS certificate password, and any other secret value
**must not** be written to `appsettings.Development.json`. Use the
[.NET Secret Manager](https://learn.microsoft.com/aspnet/core/security/app-secrets)
instead. Secrets are stored outside the repo:

- Linux/macOS: `~/.microsoft/usersecrets/<UserSecretsId>/secrets.json`
- Windows: `%APPDATA%\Microsoft\UserSecrets\<UserSecretsId>\secrets.json`

The project is already initialized (`UserSecretsId` in `adrapi/adrapi.csproj`). To
populate your local store, from `adrapi/`:

```bash
dotnet user-secrets set "ldap:bindDn" "cn=<service-account>,...,dc=fgv,dc=br"
dotnet user-secrets set "ldap:bindCredentials" "<password>"
dotnet user-secrets set "certificate:password" "<cert-password>"

# Inspect / clear:
dotnet user-secrets list
dotnet user-secrets remove "ldap:bindCredentials"
dotnet user-secrets clear
```

User Secrets are loaded automatically when `ASPNETCORE_ENVIRONMENT=Development`
(the default for `DEBUG` builds). They override values in the JSON files.

For staging/production, supply the same keys via environment variables (e.g.
`LDAP__BINDCREDENTIALS`) or a secret manager (Azure Key Vault, AWS Secrets
Manager, HashiCorp Vault) — never via committed JSON.

### LDAPS Certificate Pinning

The API rejects any LDAPS server certificate that does **not** chain to a trusted
root in the OS store, unless its SHA-256 thumbprint has been explicitly added to
the **pin store** for that host. There is no insecure "accept any certificate"
fallback.

- **Pin store file** — defaults to `cfg/ldap-trusted-certs.json` (override with the
  config key `ldap:trustedCertificatesFile`). The file is gitignored.
- **What gets pinned** — the SHA-256 of the DER-encoded server certificate,
  scoped to a hostname. Multiple pins per host are allowed (use during rotation:
  add the new pin before the old cert is replaced, remove the old one afterwards).
- **What happens on rejection** — the LDAP bind fails and NLog records a single
  error line with `SslPolicyErrors`, `sha256`, `subject`, `issuer`, and the exact
  command to authorize the certificate.

To authorize a new LDAPS server (one-time per certificate), use the helper tool:

```bash
# Inspect the certificate and add it to the pin store after operator confirmation
dotnet run --project tools/AdrapiLdapCertPin -- sdcdc1vpr0006.fgv.br:636

# Non-interactive (e.g. for provisioning scripts)
dotnet run --project tools/AdrapiLdapCertPin -- sdcdc1vpr0006.fgv.br:636 --yes \
    --note "Initial pin after DC certificate renewal 2026-05"

# List currently trusted pins
dotnet run --project tools/AdrapiLdapCertPin -- --list

# Remove a pin (e.g. after rotation)
dotnet run --project tools/AdrapiLdapCertPin -- --remove sdcdc1vpr0006.fgv.br ab:cd:ef:...

# Use a different store file (e.g. for a container deployment)
dotnet run --project tools/AdrapiLdapCertPin -- dc.example.com:636 --store /etc/adrapi/ldap-pins.json
```

The tool connects to the LDAPS endpoint, prints the certificate's subject /
issuer / validity / SHA-256 thumbprint, and writes the pin only after you
confirm. Always verify the thumbprint out-of-band (e.g. against the value
reported by the AD server admin) before answering `y`.

## Security Model

The API expects these headers:

- `api-key`: `keyID:secretKey`
- `api-version`: API version selector

Claims are loaded from `security.json`.

- `isAdministrator`: read/write access
- `isMonitor`: read-oriented access

### API Key Store (SQLite + Argon2id)

API keys live in a local SQLite database (default: `cfg/api-keys.db`,
configurable via `security:databaseFile`). Only an Argon2id hash of each secret
is persisted — plaintext is **never** stored. The database file is gitignored.

The store is managed with the `adrapi-api-keys` CLI tool. From the repo root:

```bash
# Create a new key (the plaintext secret is printed ONCE — record it now)
dotnet run --project tools/AdrapiApiKeys -- add \
    --keyID admin-prod --ip 10.0.0.5 --claims isAdministrator

# List keys (metadata only — never the secret)
dotnet run --project tools/AdrapiApiKeys -- list

# Rotate a secret (issues a new plaintext; the old one stops working immediately)
dotnet run --project tools/AdrapiApiKeys -- rotate --keyID admin-prod

# Delete a key
dotnet run --project tools/AdrapiApiKeys -- remove --keyID admin-prod

# Debug: confirm a secret matches the stored hash
dotnet run --project tools/AdrapiApiKeys -- verify --keyID admin-prod --secret '...'

# Import an older plaintext security.json into the store (one-shot)
dotnet run --project tools/AdrapiApiKeys -- import --from /path/to/security.json

# All commands accept --db <path> to target a non-default store location.
```

**Migration from `security.json`:** on first boot the API auto-imports any
`security.json` it finds next to the working directory, hashes each secret with
Argon2id, writes them into the SQLite store, and renames the source to
`security.json.imported.<timestamp>` so it isn't re-read. Review and delete the
archived file once you've verified the new store works.

**Hash parameters** (Argon2id, OWASP 2024 recommendation): memory 19 MiB,
iterations 2, parallelism 1, salt 16 B, output 32 B. Each verify costs roughly
20–50 ms on commodity hardware — acceptable per-request for admin APIs, and a
strong barrier against offline cracking if the DB ever leaks.

**Production deployment:** keep the `.db` file on encrypted storage, restrict
filesystem permissions to the API process user, and back it up out-of-band. For
container deployments mount it from a persistent volume (or generate it at
deploy time from a secret-manager-sourced seed via the CLI).

Test keys live in an ephemeral SQLite file created by each test fixture
(`tests/Security.cs`); nothing about test data is persisted.

### Encrypted Application Secrets (LDAP credentials etc.)

Sensitive config values — `ldap:bindDn`, `ldap:bindCredentials`,
`certificate:password` — are stored encrypted in the same SQLite database as
the API keys (table `app_secrets`). On startup the API reads and decrypts them
into the standard `IConfiguration` tree, so existing code (`LdapConfig`,
`certificate:*`) keeps working without changes.

**Encryption.** ChaCha20-Poly1305 (256-bit key, AEAD, RFC 8439 — quantum-safe
in practice: Grover's algorithm only halves effective key strength to 128 bits,
still secure). The 256-bit key is derived via HKDF-SHA256 from:

- the **machine ID** (`/etc/machine-id` on Linux, `ioreg IOPlatformUUID` on
  macOS, `HKLM\…\MachineGuid` on Windows; falls back to hostname+OS), and
- a **random 32-byte seed** generated on first run of the CLI tool and stored
  at `cfg/.seed` (0600 on Unix), configurable via `security:seedFile`.

Both factors are required to decrypt: an attacker with the SQLite file alone
sees nothing useful; with seed + DB but on a different host, decryption still
fails. Conversely, **if you migrate hosts or lose the seed, the data is
unrecoverable** — back up the seed file out-of-band.

**Managing secrets** (same CLI tool, `secret` subcommand group):

```bash
# Set a value interactively (input hidden) or with --value
dotnet run --project tools/AdrapiApiKeys -- secret set --name "ldap:bindCredentials"

# List secret names (values stay encrypted)
dotnet run --project tools/AdrapiApiKeys -- secret list

# Decrypt and print
dotnet run --project tools/AdrapiApiKeys -- secret get --name "ldap:bindCredentials"

# Delete
dotnet run --project tools/AdrapiApiKeys -- secret remove --name "ldap:bindCredentials"

# One-shot migration from appsettings/user-secrets JSON
dotnet run --project tools/AdrapiApiKeys -- secret import-ldap \
    --from ~/.microsoft/usersecrets/<UserSecretsId>/secrets.json
```

On the very first `secret` command the tool generates and announces the seed
file with a prominent banner. Back it up.

### Rate Limiting on Authentication Endpoints

`POST /api/users/{userId}/authenticate` and `POST /api/users/authenticate` are
protected by the `Reading` authorization policy (a valid `api-key` header is
required) **and** throttled by an ASP.NET Core rate limiter to blunt brute force:

- Policy name: `AuthEndpoint`
- Algorithm: sliding window
- Partition key: `ip + authenticated keyID` — each unique (client IP,
  API keyID) pair gets its own bucket
- Default: 5 requests / 60 seconds per partition
- Response on overflow: `429 Too Many Requests` with `Retry-After` header; also
  logged as a structured warning (`path`, `ip`, `keyID`)

Tune via `rateLimit:auth` in `appsettings.json`:

```json
"rateLimit": {
  "auth": {
    "permitLimit": 5,
    "windowSeconds": 60,
    "segmentsPerWindow": 6
  }
}
```

**Behind a reverse proxy:** the limiter uses
`HttpContext.Connection.RemoteIpAddress`. If the API is fronted by nginx/HAProxy,
enable forwarded-headers middleware (`UseForwardedHeaders`) so the real client
IP is used as the partition key — otherwise every request appears to come from
the proxy and shares one bucket.

## Run Locally

```bash
dotnet restore adrapi.sln
dotnet run --project adrapi/adrapi.csproj
```

Default Kestrel bindings are configured in code:

- HTTP: `:6000`
- HTTPS: `:6001`

## API Documentation

- Interactive docs: `/swagger`
- Detailed reference: `adrapi/docs/API_REFERENCE.md`
- Usage guide: `adrapi/docs/USAGE_GUIDE.md`
- CLI tools (`adrapi-api-keys`): `adrapi/docs/CLI_TOOLS.md`
- Migration notes: `adrapi/docs/MIGRATION_NOTES.md`
- Curl collection: `adrapi/docs/CURL_COLLECTION.md`

The `docs/` folder is also a [docsify](https://docsify.js.org) site. Browse it
locally with:

```bash
./serve-docs.sh           # macOS/Linux  -> http://localhost:3000
serve-docs.bat            # Windows
```

## Development Notes

- Use a local `net10.0` runtime/SDK for build and tests.
- The repository includes legacy code paths and compatibility behaviors for older LDAP contracts; keep changes backward-compatible unless intentionally versioned.

## License

Apache License v2.0
