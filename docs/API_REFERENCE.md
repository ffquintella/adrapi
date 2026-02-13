# ADRAPI API Reference

This document describes the HTTP API implemented in `adrapi`.

## Base URL and Versioning

- Base URL (example): `https://<host>:6001`
- API version header: `api-version`
- Supported versions:
- `2.0` (current)
- `1.0` (deprecated)

## Authentication and Authorization

Every request must include:

- `api-key: <keyId>:<secretKey>`
- `api-version: 2.0` (or `1.0` for legacy routes)

Optional request header:

- `X-Correlation-ID: <id>` (used for end-to-end log correlation)

Authorization policies:

- `Reading`: allows users with claim `isAdministrator` or `isMonitor`
- `Writting`: allows users with claim `isAdministrator`

## Common Response Codes

- `200` success
- `400` bad request (invalid input)
- `401` authentication failed
- `404` resource not found
- `409` conflict (invalid DN/query combinations)
- `422` semantic validation error for member resolution
- `500` internal server error

Error mapping policy for hardened v2 handlers:

- use `4xx` for invalid/missing/semantically incorrect client input
- use `5xx` only for unexpected server/LDAP faults

Auditability note:

- Mutation endpoints emit structured `AUDIT` records including `requester`, `correlationId`, `clientIp`, `targetDn`, and change summary.

Special legacy code:

- `250` user exists but is not a member of the requested group (`GET /api/users/{dn}/member-of/{group}`)

## Endpoints (v2)

### Users (`/api/users`)

| Method | Path | Description |
|---|---|---|
| GET | `/api/users` | List users (paged LDAP response with cookie support) |
| GET | `/api/users?_full=true` | List full user objects |
| GET | `/api/users/{user}` | Get user by DN or attribute value |
| GET | `/api/users/{user}/exists` | Check if user exists |
| GET | `/api/users/{dn}/member-of/{group}` | Check group membership |
| POST | `/api/users/authenticate` | Validate credentials directly |
| POST | `/api/users/{userId}/authenticate` | Validate user credentials using user locator |
| PUT | `/api/users/{dn}` | Create or update user |
| DELETE | `/api/users/{userId}` | Delete user |

Query parameters (GET `/api/users`):

- `_start` (int, default `-1`)
- `_end` (int, default `-1`)
- `_cookie` (string, default empty)
- `_attribute` (string, default empty)
- `_filter` (string, default empty)

When `_start` and `_end` are both `-1`, LDAP paged mode is used and response includes `Cookie`.

### Groups (`/api/groups`)

| Method | Path | Description |
|---|---|---|
| GET | `/api/groups` | List group CNs |
| GET | `/api/groups?_full=true` | List full group objects |
| GET | `/api/groups/{groupId}` | Get group by DN (or CN for compatibility) |
| GET | `/api/groups/{dn}/exists` | Check group existence |
| GET | `/api/groups/{groupId}/members` | List group members by DN (or CN for compatibility) |
| POST | `/api/groups` | Create group (explicit v2 contract) |
| PUT | `/api/groups/{dn}` | Create or update group |
| PATCH | `/api/groups/{dn}/members` | Add/remove members (delta contract) |
| PUT | `/api/groups/{dn}/members` | Replace group members |
| DELETE | `/api/groups/{dn}` | Delete group |

Important query parameters:

- `_listCN` (bool): indicates if member values are CN/account-style values that must be resolved to DNs.
- Membership write endpoints (`POST /api/groups`, `PUT /api/groups/{dn}`, `PUT/PATCH /api/groups/{dn}/members`) use all-or-fail validation: unresolved members return `422` and no partial update is persisted.
- Member identifier resolution is strict:
- DN-like values must exist as valid user/group DNs.
- Non-DN values are resolved by group CN or user `sAMAccountName` (and by user `cn` when `_listCN=false`).

#### Group Mutation Examples

Create group (new v2 create contract):

```bash
curl -k -X POST 'https://localhost:6001/api/groups?_listCN=true' \
  -H 'Content-Type: application/json' \
  -H 'api-version: 2.0' \
  -H 'api-key: <adminKeyId>:<secretKey>' \
  -H 'X-Correlation-ID: grp-create-001' \
  -d '{
    "dn":"CN=DevOps,OU=Groups,DC=homologa,DC=br",
    "name":"DevOps",
    "description":"DevOps team",
    "members":["jdoe","maria.silva"]
  }'
```

Patch membership delta (add/remove):

```bash
curl -k -X PATCH 'https://localhost:6001/api/groups/CN=DevOps,OU=Groups,DC=homologa,DC=br/members?_listCN=true' \
  -H 'Content-Type: application/json' \
  -H 'api-version: 2.0' \
  -H 'api-key: <adminKeyId>:<secretKey>' \
  -H 'X-Correlation-ID: grp-patch-001' \
  -d '{
    "add":["new.user"],
    "remove":["old.user"]
  }'
```

Replace full membership set:

```bash
curl -k -X PUT 'https://localhost:6001/api/groups/CN=DevOps,OU=Groups,DC=homologa,DC=br/members?_listCN=true' \
  -H 'Content-Type: application/json' \
  -H 'api-version: 2.0' \
  -H 'api-key: <adminKeyId>:<secretKey>' \
  -H 'X-Correlation-ID: grp-replace-001' \
  -d '["jdoe","maria.silva"]'
```

### OUs (`/api/ous`)

| Method | Path | Description |
|---|---|---|
| GET | `/api/ous` | List OUs |
| GET | `/api/ous/{dn}` | Get OU |
| GET | `/api/ous/{dn}/exists` | Check OU existence |
| POST | `/api/ous` | Create OU (explicit v2 contract) |
| PUT | `/api/ous/{dn}` | Create or update OU |
| DELETE | `/api/ous/{dn}` | Delete OU |

OU write guardrails:

- DN must be OU-formatted (`OU=<name>,...`) and under `ldap.searchBase`.
- OU name in payload must match DN RDN OU name.
- Protected/system OUs are blocked from update/delete:
- `ldap.searchBase` root DN
- optional `ldap.protectedOUs` entries
- built-in prefixes: `OU=Domain Controllers,`, `OU=System,`, `OU=Microsoft Exchange System Objects,`

#### OU Mutation Examples

Create OU:

```bash
curl -k -X POST 'https://localhost:6001/api/ous' \
  -H 'Content-Type: application/json' \
  -H 'api-version: 2.0' \
  -H 'api-key: <adminKeyId>:<secretKey>' \
  -H 'X-Correlation-ID: ou-create-001' \
  -d '{
    "dn":"OU=Platform,DC=homologa,DC=br",
    "name":"Platform",
    "description":"Platform OU"
  }'
```

Update OU:

```bash
curl -k -X PUT 'https://localhost:6001/api/ous/OU=Platform,DC=homologa,DC=br' \
  -H 'Content-Type: application/json' \
  -H 'api-version: 2.0' \
  -H 'api-key: <adminKeyId>:<secretKey>' \
  -H 'X-Correlation-ID: ou-update-001' \
  -d '{
    "name":"Platform",
    "description":"Platform OU - updated"
  }'
```

Delete OU:

```bash
curl -k -X DELETE 'https://localhost:6001/api/ous/OU=Platform,DC=homologa,DC=br' \
  -H 'api-version: 2.0' \
  -H 'api-key: <adminKeyId>:<secretKey>' \
  -H 'X-Correlation-ID: ou-delete-001'
```

### Infos (`/api/infos`)

| Method | Path | Description |
|---|---|---|
| GET | `/api/infos` | List info keys (`version`, `about`) |
| GET | `/api/infos/version` | Return deployed version |
| GET | `/api/infos/about` | Return service description |

## Key Request Bodies

### AuthenticationRequest

```json
{
  "login": "srv-adrapi",
  "password": "secret"
}
```

### User (create/update)

```json
{
  "name": "John Doe",
  "account": "jdoe",
  "dn": "CN=jdoe,OU=Users,DC=homologa,DC=br",
  "mail": "john.doe@example.com",
  "description": "Example user",
  "password": "StrongPass!123",
  "isDisabled": false,
  "isLocked": false,
  "passwordExpired": false,
  "memberOf": []
}
```

### Group (create/update)

```json
{
  "name": "MyGroup",
  "dn": "CN=MyGroup,OU=Groups,DC=homologa,DC=br",
  "description": "Example group",
  "member": [
    "CN=jdoe,OU=Users,DC=homologa,DC=br"
  ]
}
```

### GroupCreateRequest (`POST /api/groups`)

```json
{
  "dn": "CN=MyGroup,OU=Groups,DC=homologa,DC=br",
  "name": "MyGroup",
  "description": "Example group",
  "members": [
    "CN=jdoe,OU=Users,DC=homologa,DC=br"
  ]
}
```

### GroupMembersPatchRequest (`PATCH /api/groups/{dn}/members`)

```json
{
  "add": [
    "CN=jdoe,OU=Users,DC=homologa,DC=br"
  ],
  "remove": [
    "CN=olduser,OU=Users,DC=homologa,DC=br"
  ]
}
```

### OU (create/update)

```json
{
  "name": "MyOU",
  "dn": "OU=MyOU,DC=homologa,DC=br",
  "description": "Example OU"
}
```

## Stage 2 Response Matrix (v2 Contracts)

| Endpoint | 200 | 400 | 404 | 409 | 422 | 500 |
|---|---|---|---|---|---|---|
| `POST /api/groups` | created | invalid body | - | duplicate DN or invalid DN | unresolved member when `_listCN=true` | ldap/internal error |
| `PATCH /api/groups/{dn}/members` | updated | invalid/empty patch body | group not found | - | unresolved member when `_listCN=true` | ldap/internal error |
| `PUT /api/groups/{dn}/members` | replaced | invalid body | group not found | - | unresolved member when `_listCN=true` | ldap/internal error |
| `GET /api/groups/{dn}/members` | listed | - | group not found | - | - | ldap/internal error |
| `POST /api/ous` | created | invalid body | - | duplicate DN or invalid DN | - | ldap/internal error |
| `PUT /api/ous/{dn}` | created/updated | invalid body | - | invalid DN / DN mismatch | - | ldap/internal error |
| `DELETE /api/ous/{dn}` | deleted | - | OU not found | invalid DN | - | ldap/internal error |
| `GET /api/ous` | listed | - | - | - | - | ldap/internal error |
| `GET /api/ous/{dn}` | returned | - | OU not found | - | - | ldap/internal error |
| `GET /api/ous/{dn}/exists` | exists | - | OU not found | - | - | ldap/internal error |

## Legacy v1 Notes

- v1 users/groups controllers are still present and marked deprecated.
- Prefer `api-version: 2.0` for all new integrations.
- v1 controllers keep `Reading`/`Writting` policy protection; do not bypass the same `api-key` and claims model.

## Interactive API Docs

Swagger UI is available at:

- `/swagger`

## Logging and Audit Contract

Mutation endpoints emit `AUDIT` log entries through `BaseController.LogAudit`.

Expected metadata fields:

- `action`: operation identifier (`group.create.success`, `ou.delete.request`, etc.)
- `requester`: API key ID extracted from header `api-key` (`keyId:secret`)
- `correlationId`: value from `X-Correlation-ID` or request trace ID fallback
- `clientIp`: first IP from `X-Forwarded-For` or remote socket IP fallback
- `targetDn`: target LDAP DN
- `change`: compact change summary

This guarantees every write operation can be traced to source API identity and client IP.
