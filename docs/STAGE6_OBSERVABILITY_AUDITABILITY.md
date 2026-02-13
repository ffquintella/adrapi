# Stage 6 Observability and Auditability

Date: 2026-02-13

## Scope

This stage adds structured audit logging for create/update/delete and membership mutation operations with correlation support.

## Implemented Audit Context

Shared audit helpers were added to:

- `/Users/felipe/Dev/adrapi/adrapi/Controllers/BaseController.cs`

Fields now emitted in audit logs:

- `action`
- `requester` (from `api-key` id)
- `correlationId` (from `X-Correlation-ID` header, fallback to ASP.NET trace id)
- `clientIp` (`X-Forwarded-For` first value or remote address)
- `targetDn`
- `changeSummary`

Audit log template:

`AUDIT action=... requester=... correlationId=... clientIp=... targetDn=... change=...`

## Instrumented Operations

### Groups (v2)

- `/Users/felipe/Dev/adrapi/adrapi/Controllers/V2/GroupsController.cs`
- create (`POST /api/groups`)
- update (`PUT /api/groups/{dn}`)
- delete (`DELETE /api/groups/{dn}`)
- members replace (`PUT /api/groups/{dn}/members`)
- members patch (`PATCH /api/groups/{dn}/members`)

### Groups (v1)

- `/Users/felipe/Dev/adrapi/adrapi/Controllers/GroupsController.cs`
- create/update (`PUT /api/groups/{dn}`)
- delete (`DELETE /api/groups/{dn}`)
- members replace (`PUT /api/groups/{dn}/members`)

### OUs (v1/v2 controller)

- `/Users/felipe/Dev/adrapi/adrapi/Controllers/OUsController.cs`
- create (`POST /api/ous`)
- update (`PUT /api/ous/{dn}`)
- delete (`DELETE /api/ous/{dn}`)

## Operational Notes

- Correlation IDs can be propagated from upstream gateway/services through `X-Correlation-ID`.
- Audit logs include API requester id and client IP to support incident investigation and change traceability.

## Build Verification

- `dotnet build adrapi.sln`: success
- `dotnet test adrapi.sln`: success
