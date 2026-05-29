# AGENTS.md — Conventions for AI coding agents working on adrapi

This file tells AI coding agents (Claude, Copilot, Cursor, etc.) what the
non-negotiable rules of this codebase are. Human contributors should read it
too. `CLAUDE.md` is a stub pointing here.

---

## Authentication contract — **required** when touching controllers

adrapi authenticates every request with the `api-key: <keyID>:<secret>` header.
Keys live in the SQLite store (`cfg/api-keys.db`) with Argon2id-hashed secrets;
authorization is policy-based with two policies declared in `Startup.cs`:

- `Reading` → claim `isAdministrator` **or** `isMonitor`
- `Writting` → claim `isAdministrator` only

### Rules for any change that adds, removes, or moves a controller action

1. The action **must** carry an explicit `[Authorize(Policy = "Reading")]` or
   `[Authorize(Policy = "Writting")]` attribute, even when the controller
   class already has one. Class-level attributes are easy to delete by
   accident; per-method is the source of truth.
2. The action **must** appear in
   [`tests/Authentication/EndpointCatalog.cs`](tests/Authentication/EndpointCatalog.cs).
   The catalog is the single source of truth driving the auth test matrix.
3. Authentication endpoints (`POST /api/users/.../authenticate`) **must** also
   carry `[EnableRateLimiting("AuthEndpoint")]`.
4. Run `dotnet test --filter FullyQualifiedName~Authentication` before opening
   a PR. Every endpoint × every auth scenario must pass.

The test suite in `tests/Authentication/EndpointAuthenticationTests.cs`
enforces, for every endpoint in the catalog:

| Scenario                                             | Expected response |
| ---------------------------------------------------- | ----------------- |
| No `api-key` header                                  | 401               |
| Unknown keyID                                        | 401               |
| Known keyID with wrong secret                        | 401               |
| Valid key from an unauthorized source IP             | 401               |
| Monitor claim against a `Writting`-policy endpoint   | 403               |
| Admin claim against any endpoint                     | NOT 401, NOT 403  |

If you delete an endpoint, delete its entry from the catalog. If you add one,
add it. If you renumber a route, update the catalog. The compiler doesn't
enforce this — you do.

---

## Other conventions

### Secrets never enter the repository

- `adrapi/security.json`, `docker/Settings/security.json`,
  `appsettings.Development.json`, `cfg/api-keys.db`, `cfg/.seed` — all
  gitignored. Templates with placeholder values (`*.example`) are fine.
- For local dev, use `dotnet user-secrets` (project has a `UserSecretsId`) or
  the `adrapi-api-keys secret set` CLI tool.
- For deployments, mount secrets from a secret manager.

### LDAPS certificates are pinned

The `LdapCertificateValidator` rejects any cert that doesn't chain to a
trusted root **or** match a SHA-256 thumbprint in
`cfg/ldap-trusted-certs.json`. Adding a new directory server requires running
`tools/AdrapiLdapCertPin` once against it. Don't loosen the validator.

### Rate limiting on auth endpoints

The `AuthEndpoint` policy in `Startup.ConfigureRateLimiting` partitions by
`(client-ip, keyID)`. When you add a new authentication-style endpoint
(anything that takes credentials in a body), apply the same attribute.

### Backwards compatibility

V1 controllers are marked `[ApiVersion("1.0", Deprecated = true)]`. Keep them
working until a planned removal. New features go on V2.

### Multi-domain (per-directory) routing

adrapi can serve multiple LDAP directories. V2 controllers carry **two**
class-level routes — the legacy domain-less one and a domain-prefixed one, e.g.
`[Route("api/users")]` + `[Route("api/{domain}/users")]`. A missing `{domain}`
segment means the **default** domain, so existing clients are unaffected.

Rules when adding/most touching a V2 controller action:

1. Add `[FromRoute] string domain = null` as the **last** action parameter, and
   resolve it first:
   ```csharp
   if (!TryResolveDomain(domain, out var ldapConfig, out var domainError)) return domainError;
   ```
   `TryResolveDomain` (on `BaseController`) returns the default config for a null
   segment, a 404 for an unknown domain, and a 400 for reserved/invalid names.
2. Thread the resolved `LdapConfig ldapConfig` into **every** manager call. The
   manager → `LdapQueryManager` → `LdapConnectionManager` chain takes an
   `LdapConfig config = null` parameter end-to-end; a null falls back to the
   default domain (this is how V1 keeps working). **Never** reintroduce the
   instance `config` field that `LdapQueryManager` used to hold — config is
   passed explicitly per call so it is domain-correct.
3. Add **both** the domain-less and a `/api/{domain}/...` (sample domain `lab`)
   row to `tests/Authentication/EndpointCatalog.cs`. V1 stays domain-less — no
   domain rows for `1.0`.

Configuration: the top-level `ldap` section is the default domain; additional
domains live under `ldap:domains:{name}` (same shape), and `ldap:defaultDomain`
names the default. Domain names may not be `users`/`groups`/`ous`/`infos`
(reserved to avoid route ambiguity). Connection pools are bucketed per-domain by
`LdapConfig.DomainKey`. Header-based API versioning (`api-version` header) is
unchanged and orthogonal to the domain segment.

### Test discipline

- Don't reach into production singletons (`ApiKeyManager`, `LdapConnectionManager`)
  from tests — use the fixture pattern (`Authentication/AuthEndpointFixture`)
  with isolated temp SQLite files instead.
- Tests must clean up temp files in `Dispose` / `DisposeAsync`.
- A flaky test is a broken test. If you can't fix it, mark it `Skip = "..."`
  with an issue link, don't silently delete.

### Code style

- No emojis in source code or commit messages unless the user explicitly asks.
- Comments answer *why*, not *what*. Don't narrate what the code obviously does.
- Keep methods small enough that you don't need section banner comments inside
  them.

### Build & test commands

```bash
dotnet build adrapi.sln            # full solution
dotnet test  tests/tests.csproj    # full test run
dotnet test  tests/tests.csproj --filter FullyQualifiedName~Authentication   # just the auth gate
```

The CI / build pipeline lives under `build/` (NUKE). Local builds should
match what CI does — if you're tempted to add a "skip in CI" condition,
that's usually wrong.

### Commits

- Don't commit unless the user asks.
- Use a HEREDOC for multi-line commit messages.
- One concern per commit when reasonable.
- Co-author trailer for AI-assisted commits.

### Dependency hygiene

When in doubt about a dependency version, run:

```bash
dotnet list package --outdated
```

Across all four projects (`adrapi`, `domain`, `tests`, plus the two tools in
`tools/`). Don't pin to a major version older than what's listed unless
there's a written reason in the PR description.

---

## Quick reference: where things live

| Concern                            | File                                                              |
| ---------------------------------- | ----------------------------------------------------------------- |
| Authentication scheme              | `adrapi/Security/BasicAuthenticationHandler.cs`                   |
| Authorization policies             | `adrapi/Startup.cs` (`ConfigureServices`)                         |
| Rate limit policy                  | `adrapi/Startup.cs` (`ConfigureRateLimiting`)                     |
| API key store + Argon2id hasher    | `domain/Security/{ApiKeyStore,ApiKeyHasher}.cs`                   |
| Encrypted app secrets              | `domain/Security/{SecretBox,AppSecretsStore,MachineKeyProvider}.cs` |
| Config pipeline / SQLite secrets   | `adrapi/SqliteSecretsConfigurationSource.cs` + `adrapi/Program.cs` |
| LDAPS cert pinning                 | `adrapi/Ldap/Security/LdapCertificateValidator.cs`                |
| Endpoint auth test catalog         | `tests/Authentication/EndpointCatalog.cs`                         |
| Endpoint auth test suite           | `tests/Authentication/EndpointAuthenticationTests.cs`             |
| Management CLI                     | `tools/AdrapiApiKeys/Program.cs`                                  |
| LDAPS pin CLI                      | `tools/AdrapiLdapCertPin/Program.cs`                              |
