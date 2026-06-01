# Microsoft Entra ID Backend — Setup & Usage Guide

ADRAPI can serve **Microsoft Entra ID** (via Microsoft Graph) as an additional
directory backend, alongside on-premises LDAP/AD, using the same multi-domain
routing. This guide covers app registration, configuration, capabilities, usage,
and migration.

> **Integration status.** The Entra building blocks are implemented and tested:
> token acquisition (MSAL client-credentials), the Graph client (retry/throttling/
> paging), the user and group/membership operations, response normalization,
> identifier translation, security/error mapping, and audit logging — all behind
> the `IDirectoryProvider` abstraction (`DirectoryProviderFactory`). **Startup
> validation, secret handling, and OU-on-Entra rejection are live on the REST
> surface today.** Wiring the user/group v2 controllers to *dispatch* to the Graph
> provider is the final integration step; the routes and payloads documented below
> are the stable target contract and are unchanged by that wiring.

---

## 1. App registration and admin consent

In the [Entra admin center](https://entra.microsoft.com) → **App registrations** →
**New registration**:

1. **Name** it (e.g. `adrapi-graph`).
2. **Supported account types:** *Single tenant* for one organization; *Multitenant*
   if ADRAPI will serve multiple customer tenants (see §4).
3. No redirect URI is needed (service-to-service, no user sign-in).

Then grant **application** (not delegated) Microsoft Graph permissions under
**API permissions → Add a permission → Microsoft Graph → Application permissions**:

| ADRAPI policy | Required Graph application permissions |
|---|---|
| `Reading` (read-only deployments) | `User.Read.All`, `Group.Read.All`, `GroupMember.Read.All` |
| `Writting` (full lifecycle) | `User.ReadWrite.All`, `Group.ReadWrite.All`, `GroupMember.ReadWrite.All` |

> Password operations require a more privileged role (e.g. `User-PasswordProfile.ReadWrite.All`
> or a directory role); grant it only if you use `SetUserPassword`.

Click **Grant admin consent for &lt;tenant&gt;** — application permissions do not
work until consented. Record the granted permissions in `entra.grantedPermissions`
(below) so ADRAPI can warn at startup if the read baseline is missing.

Finally create a credential under **Certificates & secrets**:

- **Client secret** (simplest), or
- **Certificate** (recommended) — upload a public cert; ADRAPI loads the matching
  PKCS#12/`.p12`.

Collect: **Directory (tenant) ID**, **Application (client) ID**, and the **secret**
or **certificate**.

## 2. Configuration reference — enabling an Entra ID domain

An Entra-backed directory is a domain with `kind: entraid` and an `entra` block
under `ldap:domains:<name>`:

```jsonc
"ldap": {
  "defaultDomain": "corp",
  "servers": [ "dc-corp:636" ], "ssl": true, "poolSize": 10,
  "bindDn": "...", "searchBase": "DC=corp,DC=example", "maxResults": 999,
  "domains": {
    "cloud": {
      "kind": "entraid",
      "entra": {
        "tenantId": "<tenant-guid-or-verified-domain>",
        "clientId": "<application-client-id>",
        // clientSecret comes from the encrypted secret store — NOT plaintext here.
        "grantedPermissions": [ "User.ReadWrite.All", "Group.ReadWrite.All", "GroupMember.ReadWrite.All" ],

        // optional overrides (defaults shown):
        "authorityHost": "https://login.microsoftonline.com",
        "graphBaseUrl": "https://graph.microsoft.com/v1.0",
        "scopes": [ "https://graph.microsoft.com/.default" ]
        // certificate alternative to a secret:
        // "certificatePath": "/run/secrets/adrapi-graph.p12",
        // "certificatePassword": "<from secret store>"
      }
    }
  }
}
```

| Key | Required | Notes |
|---|---|---|
| `kind` | yes | must be `entraid` |
| `entra.tenantId` | yes | tenant GUID or verified domain — **not** `common`/`organizations`/`consumers` |
| `entra.clientId` | yes | application (client) id |
| `entra.clientSecret` **or** `entra.certificatePath` | yes | exactly one |
| `entra.grantedPermissions` | recommended | drives the startup least-privilege warning |
| `entra.authorityHost` / `graphBaseUrl` / `scopes` | no | sovereign-cloud / advanced overrides |

### Secret storage (never plaintext)

Store the secret/cert password in the encrypted SQLite secret store — the same
pipeline that protects `ldap:bindCredentials`. The secret name is the verbatim
config path:

```bash
adrapi-api-keys secret set ldap:domains:cloud:entra:clientSecret '<the-secret>'
# or, for a certificate:
adrapi-api-keys secret set ldap:domains:cloud:entra:certificatePassword '<pfx-password>'
```

Startup fails fast with a clear message if a `kind: entraid` domain is missing
`tenantId`/`clientId`/credential, and **warns** if `grantedPermissions` don't cover
the `Reading` baseline.

## 3. Capabilities

| Capability | LDAP/AD | Entra ID |
|---|---|---|
| Users: get / list / search / exists | ✅ | ✅ |
| Users: create / update / disable / delete | ✅ | ✅ |
| User set-password | ✅ (LDAPS) | ✅ (privileged grant) |
| Groups: CRUD (security & **Microsoft 365**) | ✅ (security) | ✅ |
| Membership: add/remove (delta), replace, list | ✅ | ✅ |
| **Organizational Units** | ✅ | ❌ — no OU object (use administrative units) |

### Addressing & identifiers

- **LDAP:** distinguished name (or resolvable account/CN).
- **Entra:** objectId (GUID) or userPrincipalName for users; objectId or
  displayName for groups; members by objectId or UPN. Passing an LDAP **DN** to an
  Entra domain is rejected with a clear `400`.
- Responses are **normalized to a consistent shape** across backends: Entra
  objects have `DN = null` (objectId in `ID` is the durable identifier); every
  group reports a `GroupType` (`Security` / `Microsoft365`).

## 4. Single-tenant vs multi-tenant

The client-credentials flow always targets a **specific tenant**. For a
multi-tenant app registration, configure **one ADRAPI domain per customer tenant**,
each with that tenant's GUID/verified domain and its own admin consent. There is
no `common` authority for app-only flows (ADRAPI rejects it at startup).

## 5. Usage (target REST contract)

Entra-backed directories use the same domain-scoped v2 routes as LDAP; the
`{domain}` segment selects the directory:

```
GET    /api/{domain}/users
GET    /api/{domain}/users/{idOrUpn}
GET    /api/{domain}/users/{idOrUpn}/exists
PUT    /api/{domain}/users/{idOrUpn}          # create/update
DELETE /api/{domain}/users/{idOrUpn}

GET    /api/{domain}/groups
POST   /api/{domain}/groups                   # body GroupType: Security | Microsoft365
GET    /api/{domain}/groups/{idOrName}/members
PATCH  /api/{domain}/groups/{idOrName}/members # add/remove delta
PUT    /api/{domain}/groups/{idOrName}/members # replace full set
```

OU endpoints (`/api/{domain}/ous`) return **400** for an Entra domain.

See the [Curl Collection](CURL_COLLECTION.md#microsoft-entra-id-backed-endpoints)
for runnable examples.

## 6. Troubleshooting & error mapping

Graph failures are mapped to deterministic responses (`ProblemDetails`), never
leaking the upstream message on 5xx, and echo the Graph `request-id` for support:

| Symptom | Likely cause | Response |
|---|---|---|
| `400` "distinguished name … objectId or userPrincipalName" | sent an LDAP DN to Entra | fix the identifier |
| `400` "Organizational unit operations are not supported…" | OU op on an Entra domain | use an LDAP domain / administrative units |
| `502` upstream auth | bad/expired secret, wrong tenant | check secret store, `tenantId` |
| `502` with missing-permission startup warning | admin consent not granted | grant consent; update `grantedPermissions` |
| `503` | Graph throttling (retries exhausted) | retry later |

Audit logs (`GraphAudit` logger) record `op`, target `path`, `status`,
`graphRequestId`, and the `requester`/`correlationId`/`clientIp`.

## 7. Migration / coexistence

See [Migration Notes → LDAP to Entra ID](MIGRATION_NOTES.md#ldap-to-entra-id-coexistence).

## Related reference

- Auth internals: [`ENTRA_STAGE2_AUTH.md`](ENTRA_STAGE2_AUTH.md)
- Graph client + abstraction: [`ENTRA_STAGE3_GRAPH_CLIENT.md`](ENTRA_STAGE3_GRAPH_CLIENT.md)
- Mapping & abstraction: [`ENTRA_STAGE6_MAPPING_ABSTRACTION.md`](ENTRA_STAGE6_MAPPING_ABSTRACTION.md)
- Security checklist: [`ENTRA_STAGE7_SECURITY.md`](ENTRA_STAGE7_SECURITY.md)
