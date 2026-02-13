# Stage 5 Security, Authorization, and Validation Hardening

Date: 2026-02-13

## Scope

This stage hardens authorization consistency, LDAP/DN input validation, and response code behavior.
Coverage includes both v2 and legacy v1 controllers.

## Security Checklist

- [x] Read endpoints protected by `Reading` policy.
- [x] Mutation endpoints protected by `Writting` policy.
- [x] DN parsing validation on new/updated endpoints.
- [x] LDAP-bound identifiers validated before write operations.
- [x] Invalid requests mapped to `400/409/422`.
- [x] Unexpected server faults mapped to `500`.

## Implemented Changes

### Authorization Consistency

- Added missing write authorization to legacy group membership mutation:
- `/Users/felipe/Dev/adrapi/adrapi/Controllers/GroupsController.cs`
- `PUT /api/groups/{dn}/members` now has `[Authorize(Policy = "Writting")]`.

### Input Hardening

- Group v2:
- `GET /api/groups/{dn}/exists` now requires valid group DN format.
- Membership and create/update paths continue strict DN/member resolution behavior.
- OU:
- `GET /api/ous/{dn}` and `GET /api/ous/{dn}/exists` now validate DN format and search-base boundary.

### Error Mapping Hardening

- Removed `null` returns from exception paths in:
- `/Users/felipe/Dev/adrapi/adrapi/Controllers/V2/GroupsController.cs`
- `/Users/felipe/Dev/adrapi/adrapi/Controllers/OUsController.cs`
- Updated behavior:
- Invalid parameters/format: `400` or `409`.
- Missing resources: `404`.
- Unresolved semantic inputs (member resolution): `422`.
- Unexpected internal faults: `500`.

### v1 Audit and Hardening

- `/Users/felipe/Dev/adrapi/adrapi/Controllers/GroupsController.cs`
- added DN validation for group GET/exists/members endpoints
- replaced null/not-found-on-exception paths with explicit `500` on unexpected faults
- fixed v1 member replacement bug where `_listCN=false` could write empty member values
- added case-insensitive de-duplication during member replacement
- `/Users/felipe/Dev/adrapi/adrapi/Controllers/UsersController.cs`
- improved parameter checks (`400`) for invalid user/member-of/auth requests
- replaced broad exception-to-`404` mappings with explicit `500` for unexpected faults

## Build Verification

- `dotnet build adrapi.sln`: success
- `dotnet test adrapi.sln`: success
