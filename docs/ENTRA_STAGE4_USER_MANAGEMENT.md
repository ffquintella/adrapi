# Entra ID Integration — Stage 4: User Management via Graph

Status: **complete**. Deliverable: user lifecycle parity through Microsoft Graph
— read/list/search/exists, create/update/disable/delete, and password set. Builds
on the Graph client + provider abstraction from
`docs/ENTRA_STAGE3_GRAPH_CLIENT.md`.

---

## What was implemented

| Concern | Where |
|---|---|
| `User` ↔ Graph `user` mapping + write bodies | `adrapi/Entra/GraphUserMapper.cs` |
| Graph user lifecycle (read/list/search/exists/CRUD/disable/password) | `adrapi/Directory/GraphDirectoryProvider.cs` |
| New provider contract methods (exists/search/enable/password) | `adrapi/Directory/IDirectoryProvider.cs` |
| LDAP parity for the new methods | `adrapi/Directory/LdapDirectoryProvider.cs`, `adrapi/UserManager.cs` (`SetAccountEnabledAsync`) |
| Tests | `tests/GraphUserTests.cs` (16 unit) |

No new dependencies.

## 1. Contract additions

`IDirectoryProvider` gained the user operations needed for lifecycle parity, all
implemented by **both** backends:

```csharp
Task<bool>        UserExistsAsync(string identifier, ct);
Task<List<User>>  SearchUsersAsync(string query, ct);
Task<bool>        SetUserEnabledAsync(string identifier, bool enabled, ct);   // disable/enable
Task<bool>        SetUserPasswordAsync(string identifier, string password, bool forceChangeAtNextLogin, ct);
```

(The base `GetUsersAsync`/`GetUserAsync`/`Create`/`Update`/`Delete` were already
on the contract from Stage 3.)

## 2. Graph user operations

`GraphDirectoryProvider` now issues real Graph requests via the Stage 3
`GraphClient`:

| Operation | Graph request |
|---|---|
| Get user | `GET /users/{id-or-upn}?$select=…` (404 → `null`) |
| Exists | `GET /users/{id-or-upn}?$select=id` (404 → `false`) |
| List | `GET /users?$select=…` (paged via `@odata.nextLink`) |
| Search | `GET /users?$filter=startswith(displayName/upn/mailNickname,'…')` |
| Create | `POST /users` |
| Update | `PATCH /users/{id}` (partial — only set fields) |
| Disable/enable | `PATCH /users/{id}` `{ accountEnabled }` |
| Set password | `PATCH /users/{id}` `{ passwordProfile }` |
| Delete | `DELETE /users/{id}` |

### Mapping (`GraphUserMapper`)

| adrapi `User` | Graph `user` |
|---|---|
| `Name` | `displayName` |
| `Account` | `mailNickname` (read falls back to the UPN prefix) |
| `Login` | `userPrincipalName` |
| `GivenName` / `Surname` | `givenName` / `surname` |
| `Mobile` | `mobilePhone` |
| `Mail` | `mail` — **read-only** (derived from proxy addresses), never written |
| `IsDisabled` | inverse of `accountEnabled` |
| `ID` | `id` (objectId) — the durable identifier; `DN` is left null |
| `Password` | `passwordProfile.password` (create / set-password) |

Update/delete key off the objectId (`User.ID`) when present, falling back to the
UPN — both are valid `/users/{id-or-upn}` keys. Identifiers are URL-escaped; the
OData search term is single-quote-escaped (`'` → `''`).

### Error semantics

`GraphClient` throws `GraphException` (with status + `request-id`) on any
non-success response; the provider only special-cases `404` (→ `null`/`false` for
read/exists). Write operations return `true` on success and let `GraphException`
propagate for the controllers to map. **Set-password** and **disable** require
privileged app permissions (e.g. `User-PasswordProfile.ReadWrite.All` /
`User.EnableDisableAccount.All` or `User.ReadWrite.All`) — granting them is the
deployer's responsibility (Stage 7 documents required consent).

## 3. LDAP parity

The same contract methods work against LDAP via the existing managers:

- exists → `GetUserAsync != null`; search → `UserManager.GetListAsync(filter)`.
- password → `SaveUserAsync` writes `unicodePwd` (requires LDAPS, as before).
- enable/disable → new `UserManager.SetAccountEnabledAsync`, which flips the
  `ACCOUNTDISABLE` (0x2) bit of `userAccountControl` in place. The general
  `SaveUserAsync` path intentionally never touches `userAccountControl`, so this
  is a dedicated, correct operation rather than a silent no-op.

## Testing

- `dotnet test --filter FullyQualifiedName~GraphUserTests` — 16 unit tests:
  mapping (read/create/update), and every provider operation against a scriptable
  fake `IGraphClient` (request URLs/bodies asserted, 404 handling, OData escaping,
  server-assigned id flow-back). No network.

### Test isolation note

This stage also made the controller test suite deterministic: the tests share the
process-global `ConfigurationManager` singleton and `LdapDomainRegistry`'s
per-domain config cache. Parallelization is now disabled assembly-wide
(`tests/TestParallelization.cs`) and the controller test builders prime the global
config and call the new `LdapDomainRegistry.ClearCache()` so domain resolution is
order-independent.

## Out of scope (later stages)

- Group/membership operations over Graph (Stage 5).
- Normalizing response shapes across backends and DN↔objectId/UPN translation at
  the API edge (Stage 6).
- Wiring providers into the controllers + per-operation permission enforcement
  (Stage 5/7).
