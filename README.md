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

### API Key Store (`security.json`)

`security.json` holds the API keys the server accepts. It is **gitignored** —
never commit it. Each environment provides its own file.

To bootstrap a new environment, copy the template and replace every
`REPLACE_ME_...` placeholder with a freshly generated random string (e.g.
`openssl rand -base64 32`):

```bash
cp adrapi/security.json.example adrapi/security.json
# Edit adrapi/security.json and replace the placeholder secretKey values.

# For container deployments using the docker/ tree:
cp docker/Settings/security.json.example docker/Settings/security.json
```

For production, mount the file from a secret manager (Docker secrets, Kubernetes
secrets backed by Vault/External Secrets, etc.) instead of baking it into the
image. Also keep `authorizedIP` as narrow as possible — `0.0.0.0` effectively
disables the IP allow-list.

Test keys live in `tests/security-tests.json` and are intentionally tracked
(they're dummies used only by the test project).

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
- Migration notes: `adrapi/docs/MIGRATION_NOTES.md`
- Curl collection: `adrapi/docs/CURL_COLLECTION.md`

## Development Notes

- Use a local `net10.0` runtime/SDK for build and tests.
- The repository includes legacy code paths and compatibility behaviors for older LDAP contracts; keep changes backward-compatible unless intentionally versioned.

## License

Apache License v2.0
