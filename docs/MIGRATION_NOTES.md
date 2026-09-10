# ADRAPI Migration Notes (v1 -> v2)

This document summarizes how to move existing clients to stable v2 contracts.

## Required Headers

Keep these headers in all requests:

- `api-key: <keyId>:<secretKey>`
- `api-version: 2.0`

Recommended:

- `X-Correlation-ID: <trace-id>`

## Group Endpoint Migration

Preferred v2 writes:

- create group: `POST /api/groups`
- member delta changes: `PATCH /api/groups/{dn}/members`
- member full replacement: `PUT /api/groups/{dn}/members`

Compatibility endpoint still available:

- `PUT /api/groups/{dn}` (create/update whole group object)

Migration guidance:

1. Move group creation flows from `PUT /api/groups/{dn}` to `POST /api/groups`.
2. Use `PATCH` for incremental add/remove syncs.
3. Use `PUT /members` for authoritative full membership sync.
4. Handle `422` explicitly for unresolved members (no partial writes).

## OU Endpoint Migration

Preferred v2 OU lifecycle:

- `POST /api/ous`
- `PUT /api/ous/{dn}`
- `DELETE /api/ous/{dn}`

Guardrails to account for:

- DN must be `OU=...` and under `ldap.searchBase`.
- root/protected/system OUs cannot be changed.
- DN OU name must match payload `name`.

## Authorization Expectations

- `Reading`: read/list/get endpoints
- `Writting`: create/update/delete/membership mutation endpoints

v1 remains deprecated but still policy-protected. Do not assume weaker security on v1.

## Error Handling Changes

Client handling should differentiate:

- `400`: malformed request/payload
- `404`: entity not found
- `409`: DN conflict/validation conflict/protected OU
- `422`: semantic member resolution errors
- `500`: server or LDAP runtime failure

## Logging and Traceability

Write operations emit audit records including:

- `requester` (api key id)
- `clientIp`
- `correlationId`
- `targetDn`
- `action` and `change`

Set `X-Correlation-ID` from client side to simplify support tracing.

## LDAP to Entra ID (coexistence)

ADRAPI treats Microsoft Entra ID as an **additive per-domain backend** — you do
not migrate off LDAP to adopt it. Both coexist behind the same v2 routes.

### Coexistence model

- Keep your existing LDAP/AD domain(s) exactly as they are (the default domain is
  unchanged; V1 routes keep working).
- Add an Entra ID domain under `directories:domains:<name>` with `kind: entraid` (see the
  [Entra ID Guide](ENTRA_ID_GUIDE.md)). Clients select it with the `{domain}`
  route segment; clients that omit the segment continue to hit the default LDAP
  domain unaffected.

### What changes for clients moving a workload to Entra ID

| Concern | LDAP/AD | Entra ID |
|---|---|---|
| Object identifier | distinguished name (DN) | objectId (GUID) or UPN |
| Re-addressing an object | `DN` field | `ID` field (`DN` is `null`) |
| Group kinds | security | `Security` or `Microsoft365` (`groupType`) |
| Group members in payloads | DNs | objectId / UPN |
| OUs | supported | **not supported** (use administrative units) |

Because responses are normalized to a consistent shape, the JSON structure is the
same on both backends — only identifier *values* differ. Have clients read the
`ID` field as the durable handle and avoid assuming a DN exists.

### Suggested cutover steps per workload

1. Configure the Entra domain and grant admin consent (least-privilege roles).
2. Point a non-critical client at `/api/<entra-domain>/...` and verify
   read operations (`/users`, `/groups`, `/exists`).
3. Switch writes (create/update/membership) once reads look correct.
4. Keep LDAP and Entra domains side by side as long as needed; there is no
   forced cutover.
