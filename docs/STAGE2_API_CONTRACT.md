# Stage 2 API Contract: Group Lifecycle + OU Management

Date: 2026-02-13

## Scope

This document defines the Stage 2 v2 API contract additions and status matrix for group membership and OU lifecycle operations.

## v2 Group Contract

- `POST /api/groups`
- `PATCH /api/groups/{dn}/members`
- `PUT /api/groups/{dn}/members`
- `GET /api/groups/{dn}/members`

### Request DTOs

`POST /api/groups`

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

`PATCH /api/groups/{dn}/members`

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

## v2 OU Contract

- `POST /api/ous`
- `PUT /api/ous/{dn}`
- `DELETE /api/ous/{dn}`
- `GET /api/ous`
- `GET /api/ous/{dn}`
- `GET /api/ous/{dn}/exists`

`POST /api/ous`

```json
{
  "dn": "OU=MyOU,DC=homologa,DC=br",
  "name": "MyOU",
  "description": "Example OU"
}
```

## Response Code Matrix

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
