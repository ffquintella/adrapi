# Entra ID Integration — Stage 5: Group and Membership Management via Graph

Status: **complete**. Deliverable: group lifecycle and membership parity through
Microsoft Graph. Builds on the Graph client + provider abstraction (Stage 3) and
the user lifecycle (Stage 4).

---

## What was implemented

| Concern | Where |
|---|---|
| Group↔Graph mapping (security & Microsoft 365) | `adrapi/Entra/GraphGroupMapper.cs` |
| Graph group CRUD + membership ops | `adrapi/Directory/GraphDirectoryProvider.cs` |
| Backend-agnostic group/membership contract | `adrapi/Directory/IDirectoryProvider.cs` |
| LDAP parity for the new contract methods | `adrapi/Directory/LdapDirectoryProvider.cs` |
| Group-kind field on the shared model | `domain/Group.cs` (`GroupType`) |
| Tests | `tests/GraphGroupTests.cs` (19 unit) |

No new dependencies.

## 1. Group create/update/delete (security and Microsoft 365)

The shared `Group` model gained an optional `GroupType`:

- `null` / `"Security"` → **security group**
- `"Microsoft365"` (aka `"Unified"`/`"Office365"`/`"M365"`) → **Microsoft 365 group**

`GraphGroupMapper` maps that onto the Graph `groupTypes`/`mailEnabled`/
`securityEnabled` trio (Graph requires a valid `mailNickname`, derived from the
display name when absent):

| Kind | groupTypes | securityEnabled | mailEnabled |
|---|---|---|---|
| Security | `[]` | `true` | `false` |
| Microsoft 365 | `["Unified"]` | `false` | `true` |

`groupTypes` is immutable in Graph, so it is sent on **create only**; updates
PATCH `displayName`/`description`. `GroupType` is ignored by the LDAP backend.

## 2. Membership: add/remove (delta) and replace

`IDirectoryProvider` gained `GroupExistsAsync` plus the membership operations:

```csharp
Task<List<string>> GetGroupMembersAsync(string groupId, ct);
Task<bool>         AddGroupMembersAsync(string groupId, IEnumerable<string> members, ct);    // delta
Task<bool>         RemoveGroupMembersAsync(string groupId, IEnumerable<string> members, ct); // delta
Task<bool>         ReplaceGroupMembersAsync(string groupId, IEnumerable<string> members, ct);
```

On Graph these use the `members/$ref` endpoints:

- **add** → `POST /groups/{id}/members/$ref` with `{"@odata.id": ".../directoryObjects/{objectId}"}`
- **remove** → `DELETE /groups/{id}/members/{objectId}/$ref`
- **replace** → list current members, diff against the desired set, then remove
  the extras and add the missing ones.

Both deltas are **idempotent**, matching the LDAP behaviour: adding an existing
member (`400` "already exist") and removing an absent one (`404`) are swallowed.

## 3. Identifier resolution (UPN / objectId / DN-equivalent)

- **Groups** are addressed by objectId; a non-GUID identifier is treated as a
  `displayName` and resolved via `$filter=displayName eq '...'`.
- **Members** are added by objectId; a GUID is used as-is, anything else is
  treated as a user **UPN** and resolved to its objectId via `/users/{upn}?$select=id`.
- `GetGroupMembersAsync` returns the human-friendly **UPN** for user members and
  falls back to the **objectId** for non-user members (groups, devices) — the
  Entra equivalent of the LDAP DN list.

OData single quotes are doubled and filter values URL-encoded.

## Testing

- `dotnet test --filter FullyQualifiedName~GraphGroupTests` — 19 unit tests
  (mapping, CRUD, membership list/add/remove/replace, idempotency, identifier
  resolution). No network (fake `IGraphClient` routing by URL).

## Out of scope (later stages)

- Normalizing response shapes / DN↔objectId translation at the API edge (Stage 6).
- Wiring the providers into the controllers (Stage 6/7).
- `$batch` optimization for large membership changes (membership ops currently
  issue one `$ref` request per member).
