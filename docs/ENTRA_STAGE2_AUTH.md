# Entra ID Integration — Stage 2: Authentication and Authorization

Status: **complete**. Deliverable: working OAuth2 client-credentials token
acquisition for Microsoft Graph, with secure secret handling and a policy→app-role
mapping. Builds on the additive per-domain backend decision from
`docs/ENTRA_STAGE1_SCOPE.md`.

This stage delivers the **auth plumbing only** — acquiring/caching Graph tokens
and validating permissions. Wiring tokens into actual Graph calls is Stage 3+.

---

## What was implemented

| Concern | Where |
|---|---|
| App-registration config (tenant/client/secret/cert) | `adrapi/Entra/EntraConfig.cs` |
| Client-credentials flow + per-domain token cache (MSAL) | `adrapi/Entra/EntraTokenProvider.cs` |
| `Reading`/`Writting` → Graph app-role mapping | `adrapi/Entra/EntraScopeMap.cs` |
| Domain backend kind (`ldap` vs `entraid`) | `adrapi/Ldap/LdapDomainRegistry.cs` (`GetDomainKind`, `IsEntraDomain`) |
| Startup validation of Entra domains | `adrapi/Startup.cs` (`ValidateLdapConfiguration`) |
| Tests | `tests/EntraAuthTests.cs` (13 unit + 1 gated integration) |

Dependency added: `Microsoft.Identity.Client` (MSAL.NET) 4.84.1.

## 1. App-registration onboarding

An Entra ID-backed domain is just a domain with `kind: entraid` and an `entra`
block under `directories:domains:{name}` (`ldap:domains:{name}` since 1.10.0 —
deprecated, removed in 2.0.0):

```jsonc
"directories": {
  "defaultDomain": "corp",
  "domains": {
    "corp": {
      "kind": "ldap",
      "ldap": {
        "servers": [ "dc-corp:636" ], "ssl": true, "poolSize": 10,
        "bindDn": "...", "searchBase": "DC=corp,DC=example", "maxResults": 999
      }
    },
    "cloud": {
      "kind": "entraid",
      "entra": {
        "tenantId": "<tenant-guid>",
        "clientId": "<app-registration-client-id>",
        // clientSecret comes from the encrypted secret store (see below) —
        // do NOT put it in plaintext appsettings.
        "grantedPermissions": [ "User.Read.All", "Group.ReadWrite.All", "GroupMember.ReadWrite.All" ],
        // optional overrides:
        "authorityHost": "https://login.microsoftonline.com",
        "graphBaseUrl": "https://graph.microsoft.com/v1.0",
        "scopes": [ "https://graph.microsoft.com/.default" ]
      }
    }
  }
}
```

Required: `tenantId`, `clientId`, and **exactly one** credential — `clientSecret`
or `certificatePath` (+ optional `certificatePassword`). Startup fails fast with a
clear message if a `kind: entraid` domain is missing these.

In the Azure portal: register an application, grant it **application** Graph
permissions (e.g. `User.Read.All`, `Group.ReadWrite.All`, `GroupMember.ReadWrite.All`),
and **grant admin consent**. Record those granted permissions in
`entra.grantedPermissions` so adrapi can validate policy coverage locally.

### Secret handling (never plaintext)

The client secret is read from configuration, which is overlaid by the encrypted
SQLite secret store (`SqliteSecretsConfigurationSource`) — the same pipeline that
protects `ldap:bindCredentials`. Store it with the management CLI:

```bash
adrapi-api-keys secret set directories:domains:cloud:entra:clientSecret '<the-secret>'
```

The secret name is the verbatim config path, so it overlays
`directories:domains:cloud:entra:clientSecret` at runtime without ever appearing in
appsettings. A client **certificate** (`certificatePath` to a PKCS#12/.p12 +
`certificatePassword`) is supported as a stronger alternative.

## 2. Token acquisition (client-credentials)

`EntraTokenProvider` (singleton) builds one MSAL `IConfidentialClientApplication`
per domain and calls `AcquireTokenForClient(scopes)`:

```csharp
var cfg = EntraConfig.ForDomain("cloud");
var token = await EntraTokenProvider.Instance.AcquireTokenAsync(cfg);
// token.AccessToken, token.ExpiresOn
```

MSAL keeps an **in-memory app token cache** per confidential client and refreshes
the token automatically when it nears expiry, so callers can request a token per
operation cheaply. Invalid config throws `WrongParameterException` before any
network call; AADSTS failures surface as `InvalidCredentialsException`.

## 3. Policy → Graph app-role mapping

Client-credentials always requests the `.default` scope, so effective rights come
from the app's *granted application permissions*. `EntraScopeMap` declares which
app roles each adrapi policy needs and validates coverage:

| adrapi policy | Required Graph app roles |
|---|---|
| `Reading` | `User.Read.All`, `Group.Read.All`, `GroupMember.Read.All` |
| `Writting` | `User.ReadWrite.All`, `Group.ReadWrite.All`, `GroupMember.ReadWrite.All` |

A `*.ReadWrite.All` grant implicitly satisfies the matching `*.Read.All`. Use:

```csharp
EntraScopeMap.IsPolicySatisfied(cfg.GrantedPermissions, "Writting"); // bool
EntraScopeMap.MissingRoles(cfg.GrantedPermissions, "Reading");        // what's not granted
```

Stage 3 will call this before issuing Graph writes so a misconfigured app
registration fails with a clear permissions error rather than an opaque 403 from
Graph.

## Testing

- `dotnet test --filter FullyQualifiedName~EntraAuthTests` — 13 unit tests
  (config binding, validation, policy mapping, domain-kind detection, provider
  input validation). No network.
- Real token acquisition is gated: set `ADRAPI_RUN_ENTRA_INTEGRATION=1` plus
  `ENTRA_TENANT_ID` / `ENTRA_CLIENT_ID` / `ENTRA_CLIENT_SECRET`.

## Out of scope (later stages)

- Issuing actual Graph requests with the token (Stage 3: Graph client foundation).
- DN↔objectId/UPN identifier translation (Stage 6).
- Per-operation enforcement of `IsPolicySatisfied` in controllers (Stage 3/5).
