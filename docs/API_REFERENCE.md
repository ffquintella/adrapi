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
| GET | `/api/groups/{cn}` | Get group |
| GET | `/api/groups/{dn}/exists` | Check group existence |
| GET | `/api/groups/{cn}/members` | List group members |
| PUT | `/api/groups/{dn}` | Create or update group |
| PUT | `/api/groups/{dn}/members` | Replace group members |
| DELETE | `/api/groups/{dn}` | Delete group |

Important query parameters:

- `_listCN` (bool): indicates if member values are CN/account-style values that must be resolved to DNs.

### OUs (`/api/ous`)

| Method | Path | Description |
|---|---|---|
| GET | `/api/ous` | List OUs |
| GET | `/api/ous/{dn}` | Get OU |
| GET | `/api/ous/{dn}/exists` | Check OU existence |
| PUT | `/api/ous/{dn}` | Create or update OU |
| DELETE | `/api/ous/{dn}` | Delete OU |

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

### OU (create/update)

```json
{
  "name": "MyOU",
  "dn": "OU=MyOU,DC=homologa,DC=br",
  "description": "Example OU"
}
```

## Legacy v1 Notes

- v1 users/groups controllers are still present and marked deprecated.
- Prefer `api-version: 2.0` for all new integrations.

## Interactive API Docs

Swagger UI is available at:

- `/swagger`
