# Entra ID Integration — Stage 6: Directory Object Mapping and Abstraction

Status: **complete**. Deliverable: a backend-agnostic API surface — consistent
response shapes, edge identifier translation, and explicit OU non-support on
Entra ID. Builds on the provider abstraction (Stages 3–5).

---

## What was implemented

| Concern | Where |
|---|---|
| Identifier classification + edge validation | `adrapi/Directory/DirectoryIdentifiers.cs` |
| Response normalization to a consistent shape | `adrapi/Directory/DirectoryObjectNormalizer.cs` |
| Normalization applied on provider reads | `Ldap`/`GraphDirectoryProvider` |
| OU non-support on Entra ID (API edge) | `BaseController.TryResolveLdapDomain` + `OUsController` |
| Group-kind on the shared model | `domain/Group.cs` (`GroupType`, from Stage 5) |
| Tests | `tests/DirectoryMappingTests.cs` (9 unit) |

## 1. Consistent response shapes

`DirectoryObjectNormalizer` runs every `User`/`Group` a provider returns through
one set of rules, so a v2 consumer sees the same shape regardless of backend:

- String fields are trimmed; blank/whitespace becomes `null` ("absent" is
  represented identically).
- **Entra ID objects carry no DN** — `DN` is forced to `null` for the Graph
  backend; the objectId in `ID` is the durable identifier.
- **Every group reports a `GroupType`** — defaulting to `Security` when the
  backend didn't specify one (e.g. LDAP/AD groups).

Both `LdapDirectoryProvider` and `GraphDirectoryProvider` normalize their read
results, so the abstraction (and anything built on it) is backend-agnostic.

### Canonical identifier rules (cross-backend contract)

| Field | LDAP/AD | Entra ID |
|---|---|---|
| `ID` | objectSid | objectId (GUID) |
| `DN` | distinguished name | `null` (no DN) |
| `Login` (user) | userPrincipalName | userPrincipalName |
| member list | member DNs | UPN (users) / objectId (other) |

Member lists are structurally identical (`List<string>` of the canonical
addressable identifier) but the identifier *form* is backend-relative — there is
no DN on Entra ID and no UPN-addressing on LDAP.

## 2. Identifier translation at the edges

`DirectoryIdentifiers` classifies an identifier as `ObjectId` (GUID),
`DistinguishedName`, `UserPrincipalName`, or `Name`, and validates it for the
target backend. The Graph backend addresses objects only by objectId or UPN, so
`GraphDirectoryProvider` calls `EnsureGraphAddressable` at every edge: passing an
LDAP **DN** to the Entra backend fails fast with a clear `WrongParameterException`
instead of an opaque Graph 404. Non-GUID group identifiers are resolved via
`displayName` filter; non-GUID member identifiers are resolved as user UPNs to
their objectId.

## 3. OU operations are LDAP/AD-only

Entra ID has **no OU object** — its closest concept, *administrative units*, is a
separate Graph resource with different semantics, so adrapi does not map OUs onto
it. OU endpoints now resolve the domain through `BaseController.TryResolveLdapDomain`,
which behaves like the normal resolver but **rejects Entra ID-backed domains**
with a `400 Bad Request` explaining the non-support and pointing at administrative
units as the alternative. `GraphDirectoryProvider`'s OU methods likewise throw
`NotSupportedException` (it reports `SupportsOrganizationalUnits = false`).

### Alternative for Entra ID

To group/scope directory objects on Entra ID, use **administrative units**
(`/directory/administrativeUnits` in Graph) or security/Microsoft 365 groups —
not OUs. Mapping administrative units is intentionally out of scope.

## Testing

- `dotnet test --filter FullyQualifiedName~DirectoryMappingTests` — 9 unit tests
  (identifier classification, Graph edge rejection of DNs, user/group
  normalization rules, and OU-on-Entra `400`).

## Out of scope (later stages)

- Wiring the provider abstraction into the user/group v2 controllers so the REST
  surface itself serves the Entra backend, and the integration tests for it —
  gated on a test tenant (Stage 9).
- Administrative-unit support.
