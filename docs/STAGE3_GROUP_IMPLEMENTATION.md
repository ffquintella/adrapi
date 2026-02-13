# Stage 3 Group Management Implementation

Date: 2026-02-13

## Scope

This stage implements the core group-management behavior for v2:

- create flow validation and duplicate checks
- idempotent membership delta operations
- full membership replacement
- strict member identifier resolution
- request-level all-or-fail behavior for member batch updates

## Implemented Changes

Primary implementation file:

- `/Users/felipe/Dev/adrapi/adrapi/Controllers/V2/GroupsController.cs`

### 1. Group Create Flow Hardening

- `POST /api/groups` now validates:
- DN format (`cn=...`)
- CN from DN equals request `name`
- no duplicate group by DN or by CN mapped to another DN

### 2. Membership Delta Operations (Idempotent)

- `PATCH /api/groups/{dn}/members` remains delta-based.
- Add/remove operations are applied with case-insensitive set semantics (`HashSet`), ensuring idempotency.

### 3. Full Membership Replace

- `PUT /api/groups/{dn}/members` resolves the entire incoming list first and only saves after full validation.
- Invalid members abort the request with `422`, and no LDAP update is sent.

### 4. Strict Identifier Resolution

Member identifiers now resolve with explicit rules:

- If value looks like DN (`attr=value,...`):
- must resolve as an existing group DN or user DN
- If value is not DN:
- resolve group by CN
- resolve user by `sAMAccountName`
- fallback resolve user by `cn` (when `_listCN=false`)

If a member cannot be resolved, the request fails with `422`.

### 5. Request-Level All-or-Fail Safeguards

For `POST /api/groups`, `PUT /api/groups/{dn}`, `PUT /api/groups/{dn}/members`, and `PATCH /api/groups/{dn}/members`:

- all member identifiers are resolved before persistence
- no partial batch persistence is executed when resolution fails

## Build Verification

- `dotnet build adrapi.sln`: success
- `dotnet test adrapi.sln`: success
