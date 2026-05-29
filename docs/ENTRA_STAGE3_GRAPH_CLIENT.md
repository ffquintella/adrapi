# Entra ID Integration — Stage 3: Microsoft Graph Client Foundation

Status: **complete**. Deliverable: a reusable Microsoft Graph client (retry,
throttling, paging) and a backend-agnostic directory-provider abstraction with a
per-request / per-deployment backend switch. Builds on the auth plumbing from
`docs/ENTRA_STAGE2_AUTH.md`.

This stage delivers the **foundation** — the HTTP client every Graph call will go
through and the contract that lets controllers target either backend. Actual user
operations are Stage 4, group/membership operations are Stage 5, and response
mapping is Stage 6; those provider methods throw a clear `NotSupportedException`
until then.

---

## What was implemented

| Concern | Where |
|---|---|
| Graph HTTP client (retry / 429 throttling / 5xx / paging) | `adrapi/Entra/GraphClient.cs` |
| Graph error type carrying status + `request-id` | `domain/Exceptions/GraphException.cs` |
| Backend-agnostic directory contract | `adrapi/Directory/IDirectoryProvider.cs` |
| LDAP/AD provider (delegates to existing managers) | `adrapi/Directory/LdapDirectoryProvider.cs` |
| Entra ID / Graph provider (wraps `GraphClient`) | `adrapi/Directory/GraphDirectoryProvider.cs` |
| Backend selection switch | `adrapi/Directory/DirectoryProviderFactory.cs` |
| Tests | `tests/GraphClientTests.cs`, `tests/DirectoryProviderTests.cs` (13 unit) |

No new dependencies — the client uses `System.Net.Http` + `System.Text.Json`.

## 1. Graph client wrapper

`GraphClient` is bound to one Entra-backed domain (`EntraConfig`) and reuses a
shared `HttpClient`. It injects the bearer token from `IEntraTokenProvider` per
request (MSAL caches it, so this is cheap) and exposes:

```csharp
Task<GraphResult>        GetAsync(string relativeUrl, ct);
Task<List<JsonElement>>  GetPagedAsync(string relativeUrl, ct);   // follows @odata.nextLink
Task<GraphResult>        PostAsync(string relativeUrl, object body, ct);
Task<GraphResult>        PatchAsync(string relativeUrl, object body, ct);
Task<GraphResult>        DeleteAsync(string relativeUrl, ct);
```

`GraphResult` carries the status, the parsed JSON body (nullable), and the Graph
`request-id` / `client-request-id` for cross-correlation.

### Retry & throttling

`GraphClientOptions` tunes the behaviour (defaults shown):

- **MaxRetries = 5** — applies to `429` (throttled), transient `5xx`
  (500/502/503/504), and transport faults (`HttpRequestException`).
- **Retry-After honoured** — when Graph returns a `Retry-After` header (delta or
  HTTP-date) the client waits exactly that long; otherwise it uses exponential
  backoff (`BaseDelay * 2^attempt`).
- **MaxDelay = 60s** caps any single wait. **MaxPages = 1000** caps paging.
- Non-retryable errors (e.g. `404`, `403`) throw `GraphException` immediately;
  retries exhausted throws `GraphException` with the last status.

The `Delay` primitive is injectable, so the retry path is unit-tested with no
real waits and no network (see `tests/GraphClientTests.cs`).

### Paging

`GetPagedAsync` follows `@odata.nextLink` (an absolute URL, passed through
verbatim) and flattens every `value` array into one list, stopping at `MaxPages`
with a warning if a result set is unexpectedly large.

## 2. Directory provider abstraction

`IDirectoryProvider` is the backend-agnostic contract for user, group, and OU
operations. A provider is bound to a single domain and reports its
`Backend` (`Ldap` / `EntraId`) and `SupportsOrganizationalUnits`.

- **`LdapDirectoryProvider`** — the reference implementation; a thin adapter over
  the existing `UserManager` / `GroupManager` / `OUManager` singletons, threading
  the domain's `LdapConfig` into every call (per the multi-domain contract in
  `AGENTS.md`). Proves the abstraction end-to-end against a real backend.
- **`GraphDirectoryProvider`** — holds a `GraphClient`. User ops throw
  `NotSupportedException` ("arrives in Stage 4"), group ops "Stage 5". OU ops
  throw permanently: Entra ID has **no OU object** (administrative units are a
  separate Graph concept — see Stage 6). `SupportsOrganizationalUnits` is `false`.

## 3. Backend selection switch

`DirectoryProviderFactory` is the configuration switch:

```csharp
var provider = DirectoryProviderFactory.ForDomain(domain);   // per request ({domain} route segment)
var provider = DirectoryProviderFactory.ForLdapConfig(cfg);  // already-resolved LDAP domain
```

Selection is driven by the domain's configured `kind`
(`LdapDomainRegistry.GetDomainKind`):

- **Per request** — the `{domain}` route segment picks the domain, and thus its
  backend, for that call.
- **Per deployment** — the default domain's `kind` (and `ldap:defaultDomain`)
  decides the backend used by the legacy domain-less routes.

An `entraid` domain yields a `GraphDirectoryProvider` (with a `GraphClient` wired
to `EntraTokenProvider`); anything else yields an `LdapDirectoryProvider`.

## Testing

- `dotnet test --filter FullyQualifiedName~GraphClientTests` — token injection,
  429/5xx retry honouring `Retry-After`, non-retryable error surfacing, paging,
  body serialization. No network (scripted `HttpMessageHandler`, no-op delay).
- `dotnet test --filter FullyQualifiedName~DirectoryProviderTests` — factory
  backend selection, Graph capability flags, deferral/refusal semantics.

## Out of scope (later stages)

- Graph-backed user lifecycle (Stage 4) and group/membership ops (Stage 5).
- Normalizing response shapes and DN↔objectId/UPN translation (Stage 6).
- Wiring providers into the controllers and per-operation `IsPolicySatisfied`
  enforcement (Stage 5/7).
