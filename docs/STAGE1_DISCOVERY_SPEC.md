# Stage 1 Discovery Spec: Group Lifecycle + OU Management

Date: 2026-02-13

## Scope

This document captures current behavior, validated gaps, and target use cases for:

- Group lifecycle and membership management
- OU management

It is the Stage 1 deliverable referenced by `/Users/felipe/Dev/adrapi/roadmap.md`.

## Evidence Sources

- Controllers:
- `/Users/felipe/Dev/adrapi/adrapi/Controllers/V2/GroupsController.cs`
- `/Users/felipe/Dev/adrapi/adrapi/Controllers/GroupsController.cs`
- `/Users/felipe/Dev/adrapi/adrapi/Controllers/OUsController.cs`
- Managers:
- `/Users/felipe/Dev/adrapi/adrapi/GroupManager.cs`
- `/Users/felipe/Dev/adrapi/adrapi/OUManager.cs`

## Current Behavior (as implemented)

### Groups

Implemented endpoints (v2):

- `GET /api/groups`
- `GET /api/groups?_full=true`
- `GET /api/groups/{cn}`
- `GET /api/groups/{dn}/exists`
- `GET /api/groups/{cn}/members`
- `PUT /api/groups/{dn}` (create/update)
- `PUT /api/groups/{dn}/members` (replace members)
- `DELETE /api/groups/{dn}`

Observations:

- Group create/update exists via `PUT` with DN semantics.
- Membership replacement exists via `PUT /members`.
- No explicit delta operation (`add/remove` in one call).
- No canonical `POST /api/groups` create contract.

### OUs

Implemented endpoints:

- `GET /api/ous`
- `GET /api/ous/{dn}`
- `GET /api/ous/{dn}/exists`
- `PUT /api/ous/{dn}` (create/update)
- `DELETE /api/ous/{dn}`

Observations:

- Core OU lifecycle exists, but only via DN-based `PUT` for create/update.
- No explicit `POST /api/ous` contract.

## Validated Gaps and Failure Cases

### G1. Membership update bug when `_listCN=false`

Location:

- `/Users/felipe/Dev/adrapi/adrapi/Controllers/V2/GroupsController.cs`
- `/Users/felipe/Dev/adrapi/adrapi/Controllers/GroupsController.cs`

Problem:

- In `PutMembers`, local `dname` is initialized as empty string.
- When `_listCN` is `false`, `dname` is never assigned from `member`, but still appended.
- Result: empty values can be added instead of actual member DNs.

Impact:

- Member replacement may silently corrupt membership payload.

### G2. Missing write-authorization on membership replacement endpoint

Location:

- `/Users/felipe/Dev/adrapi/adrapi/Controllers/V2/GroupsController.cs` (`PutMembers`)
- `/Users/felipe/Dev/adrapi/adrapi/Controllers/GroupsController.cs` (`PutMembers`)

Problem:

- Endpoint is under class-level `Reading` policy and does not have `[Authorize(Policy = "Writting")]`.

Impact:

- Non-admin monitor-level clients may modify memberships.

### G3. No add/remove delta contract

Problem:

- Only full replacement endpoint exists for members.
- Common operational use case (add one user / remove one user) lacks explicit API.

Impact:

- Clients must read-modify-write entire member list, increasing race/conflict risk.

### G4. Group create contract shape is implicit

Problem:

- Creation is multiplexed with update through `PUT /api/groups/{dn}` and DN parsing constraints.
- No dedicated `POST /api/groups` contract.

Impact:

- Harder integration and validation clarity for clients.

### G5. OU create/update API is DN-coupled only

Problem:

- OU create/update also multiplexed in `PUT /api/ous/{dn}`.
- No dedicated create endpoint and no explicit guardrails for protected paths.

Impact:

- Client contract ambiguity; harder policy enforcement evolution.

## Target Capability Use Cases

### Group lifecycle and membership

- UC-G1: Create group with explicit API contract.
- UC-G2: Add users/groups to an existing group without replacing entire list.
- UC-G3: Remove users/groups from an existing group without replacing entire list.
- UC-G4: Replace entire membership deterministically (existing behavior retained).
- UC-G5: Query membership and existence quickly for sync jobs.

### OU management

- UC-OU1: Create OU with explicit request contract.
- UC-OU2: Update OU metadata safely.
- UC-OU3: Delete OU with validation and predictable errors.
- UC-OU4: List/get/exists with stable response contracts.

## Stage 1 Exit Criteria (Met)

- Current behavior for groups and OUs mapped from implementation.
- Critical gaps identified with exact code locations and impact.
- Use cases defined for next stages.
- Stage 1 deliverable document produced.
