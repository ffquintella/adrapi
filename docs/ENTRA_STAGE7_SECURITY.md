# Entra ID Integration — Stage 7: Security, Secrets, and Tenant Configuration

Status: **complete**. Deliverable: a security review checklist for the Entra ID
path, plus the hardening that backs each item. Builds on Stages 2–6.

---

## What was implemented (this stage)

| Concern | Where |
|---|---|
| Tenant-mode validation (single vs multi-tenant) | `adrapi/Entra/EntraConfig.cs` (`Validate`) |
| Least-privilege admin-consent warning at startup | `adrapi/Startup.cs` (`ValidateLdapConfiguration`) |
| Clear 4xx/5xx mapping for Graph/provider errors | `adrapi/Directory/DirectoryErrorMapper.cs` |
| OData input hardening (injection-safe literals) | `adrapi/Entra/GraphQuery.cs` |
| Tests | `tests/EntraSecurityTests.cs` (21 unit) |

Carried over from earlier stages: encrypted secret store (Stage 2), policy→app-role
mapping `EntraScopeMap` (Stage 2), `GraphException` with `request-id` (Stage 3),
identifier edge validation (Stage 6).

---

## Security review checklist

### 1. Secrets and certificates — no plaintext

- [x] Client secret is read from configuration **overlaid by the encrypted SQLite
  secret store** (`SqliteSecretsConfigurationSource`) — the same pipeline that
  protects `ldap:bindCredentials`. Set it with:
  ```bash
  adrapi-api-keys secret set directories:domains:<name>:entra:clientSecret '<secret>'
  ```
- [x] A client **certificate** (`certificatePath` to a PKCS#12/.p12 +
  `certificatePassword`) is supported as a stronger alternative; exactly one of
  secret/certificate must be configured (enforced by `EntraConfig.Validate`).
- [x] `appsettings.*`, `security.json`, `cfg/*.db` are gitignored (see `AGENTS.md`).
- [ ] **Reviewer action:** confirm no secret/cert password is committed in
  `appsettings.json`/`appsettings.Development.json` or a Dockerfile; mount from a
  secret manager in production. Prefer certificate auth over a shared secret.

### 2. Least-privilege Graph permissions + admin consent

- [x] `EntraScopeMap` declares the **application** roles each policy needs:
  | adrapi policy | Required Graph app roles |
  |---|---|
  | `Reading` | `User.Read.All`, `Group.Read.All`, `GroupMember.Read.All` |
  | `Writting` | `User.ReadWrite.All`, `Group.ReadWrite.All`, `GroupMember.ReadWrite.All` |
  (`*.ReadWrite.All` implies the matching `*.Read.All`.)
- [x] Startup logs a **warning** when a domain's declared `grantedPermissions`
  don't cover the read baseline — surfacing a missing admin-consent early instead
  of as an opaque request-time `403`.
- [ ] **Reviewer action:** grant only the roles actually needed (read-only
  deployments should grant only the `*.Read.All` trio), then **grant admin
  consent** in the Azure portal and record them in `entra.grantedPermissions`.
  Avoid `Directory.ReadWrite.All` / `User-PasswordProfile.ReadWrite.All` unless
  password operations are required.

### 3. Single-tenant vs multi-tenant

- [x] Client-credentials (app-only) requires a **tenant-specific** authority.
  `EntraConfig.Validate` rejects `common`/`organizations`/`consumers` as
  `tenantId` with a clear message.
- **Single-tenant:** set `tenantId` to the tenant GUID (or verified domain).
- **Multi-tenant:** register the app as multi-tenant, then configure **one adrapi
  domain per customer tenant**, each with that tenant's GUID/verified domain and
  its own admin consent. There is no `common` authority for app-only flows.
- [ ] **Reviewer action:** confirm each Entra domain's `tenantId` is a concrete
  tenant and that admin consent has been granted in that tenant.

### 4. Input hardening and error mapping

- [x] **Identifier edge validation** (`DirectoryIdentifiers.EnsureGraphAddressable`)
  rejects LDAP DNs sent to the Graph backend before any call.
- [x] **OData injection** is prevented by `GraphQuery.EscapeODataLiteral` (doubles
  single quotes) for every user-supplied `$filter` value.
- [x] **Error mapping** (`DirectoryErrorMapper.ToProblem`) returns a
  `ProblemDetails` with a deterministic status:
  | Source | adrapi response |
  |---|---|
  | Graph `400/409/422` | same (client error) |
  | Graph `404` | `404` |
  | Graph `429` (throttled, retries exhausted) | `503` |
  | Graph `401/403` (our token/permissions) | `502` |
  | Graph `5xx` / transport failure | `502` / `503` |
  | `WrongParameterException` / `NotSupportedException` | `400` |
  | `InvalidCredentialsException` (token acquisition) | `502` |
  | unmapped | `500` |
  Server-side (5xx) responses **never leak the upstream message**; the Graph
  `request-id`/`client-request-id` are echoed in problem extensions for support
  correlation.

### 5. Transport & tokens (carried over)

- [x] Tokens acquired via MSAL client-credentials, cached in-memory per domain,
  auto-refreshed (Stage 2). No tokens are logged.
- [x] Graph client retries `429`/`5xx` honouring `Retry-After`, capped (Stage 3).
- [ ] **Reviewer action:** ensure egress to `login.microsoftonline.com` and
  `graph.microsoft.com` is over TLS only and outbound-restricted as appropriate.

## Testing

- `dotnet test --filter FullyQualifiedName~EntraSecurityTests` — 21 unit tests
  (Graph status mapping, no-leak on 5xx, provider-exception mapping, tenant
  validation, OData escaping).

## Out of scope (later stages)

- Wiring `DirectoryErrorMapper` into the user/group controllers (alongside the
  provider dispatch deferred in Stage 6).
- Integration tests against a test tenant (Stage 9).
