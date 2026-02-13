# Stage 4 OU Management Implementation

Date: 2026-02-13

## Scope

This stage completes OU lifecycle management hardening for v2 create/update/delete operations.

Primary implementation:

- `/Users/felipe/Dev/adrapi/adrapi/Controllers/OUsController.cs`

## Implemented Behavior

### 1. Schema Validation for Create/Update/Delete

- OU DN must match OU RDN format (`OU=<name>,...`).
- Request OU name must match OU RDN name in DN.
- Request DN must be inside configured LDAP search base (`ldap.searchBase`).
- DN mismatch between URL and body remains rejected.

### 2. Protected/System OU Guardrails

Write/delete operations reject protected paths:

- exact `ldap.searchBase`
- configured `ldap.protectedOUs` list (optional)
- default protected prefixes:
- `OU=Domain Controllers,`
- `OU=System,`
- `OU=Microsoft Exchange System Objects,`

Protected DN operations return `409`.

### 3. Reliability Improvements for Read Paths

- `GET /api/ous/{dn}` returns `404` when OU is absent.
- `GET /api/ous/{dn}/exists` returns `404` when OU is absent.

## Consistency Rule

OU write/delete operations are constrained to the directory scope configured by `ldap.searchBase`, aligning OU operations with the same tenant/domain boundary used by group/user operations.

## Build Verification

- `dotnet build adrapi.sln`: success
- `dotnet test adrapi.sln`: success
