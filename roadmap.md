# Roadmap: Group Lifecycle + OU Management

## Tracking Structure

Use this section to track execution status for each stage and acceptance item.

### Status Legend

- `[x]` Done
- `[ ]` Not done
- `[-]` In progress

### Stage Status

- [x] Stage 1 - Discovery and Gap Validation
- [x] Stage 2 - API Contract Definition
- [x] Stage 3 - Group Management Implementation (Core)
- [x] Stage 4 - OU Management Implementation
- [x] Stage 5 - Security, Authorization, and Validation Hardening
- [x] Stage 6 - Observability and Auditability
- [x] Stage 7 - Testing and Quality Gates
- [x] Stage 8 - Documentation and Client Usage


## 1. Discovery and Gap Validation (1-2 days)

- [x] Confirm current behavior of group endpoints (`PUT /api/groups/{dn}`, `PUT /api/groups/{dn}/members`) and document failures.
- [x] Define target capabilities:
- [x] Create group
- [x] Add users to group
- [x] Remove users from group
- [x] Replace full membership set
- [x] Audit and confirm OU endpoints’ current reliability and missing operations.
- Deliverable: short spec with exact use cases and error cases. See `/Users/felipe/Dev/adrapi/docs/STAGE1_DISCOVERY_SPEC.md`.

## 2. API Contract Definition (1 day)

- [x] Standardize v2 endpoints and payloads for group management:
- [x] `POST /api/groups` (create)
- [x] `PATCH /api/groups/{dn}/members` (add/remove delta)
- [x] `PUT /api/groups/{dn}/members` (replace full set)
- [x] `GET /api/groups/{dn}/members`
- [x] Define OU contract stage:
- [x] `POST /api/ous`
- [x] `PUT /api/ous/{dn}`
- [x] `DELETE /api/ous/{dn}`
- [x] `GET /api/ous`, `GET /api/ous/{dn}`, `GET /api/ous/{dn}/exists`
- Deliverable: versioned API contract + response code matrix. See `/Users/felipe/Dev/adrapi/docs/STAGE2_API_CONTRACT.md`.

## 3. Group Management Implementation (Core) (3-5 days)

- [x] Implement group create flow with DN validation and duplicate checks.
- [x] Implement membership delta operations (add/remove) with idempotent behavior.
- [x] Implement full replace membership operation.
- [x] Resolve member identifiers safely (DN vs account/CN), with strict validation.
- [x] Add transactional-style safeguards per request (all-or-fail semantics for batch member changes).
- Deliverable: working CRUD + membership edit feature for groups. See `/Users/felipe/Dev/adrapi/docs/STAGE3_GROUP_IMPLEMENTATION.md`.

## 4. OU Management Implementation (2-4 days)

- [x] Implement/complete OU create, update, delete flows with schema validation.
- [x] Add guardrails for protected/system OUs and invalid DN operations.
- [x] Ensure OU operations are consistent with group/user location rules.
- Deliverable: complete OU lifecycle management. See `/Users/felipe/Dev/adrapi/docs/STAGE4_OU_MANAGEMENT.md`.

## 5. Security, Authorization, and Validation Hardening (1-2 days)

- [x] Enforce `Reading` vs `Writting` policies consistently.
- [x] Add LDAP input hardening and DN parsing validation on all new endpoints.
- [x] Add clear 4xx errors for invalid requests and 5xx only for real server faults.
- Deliverable: security review checklist + hardened handlers. See `/Users/felipe/Dev/adrapi/docs/STAGE5_SECURITY_HARDENING.md`.

## 6. Observability and Auditability (1 day)

- [x] Add structured logs for create/update/delete and membership changes.
- [x] Add correlation IDs and explicit audit fields (requester, target DN, change summary).
- Deliverable: actionable logs for support and incident response. See `/Users/felipe/Dev/adrapi/docs/STAGE6_OBSERVABILITY_AUDITABILITY.md`.

## 7. Testing and Quality Gates (2-3 days)

### 7.1 Unit Test Coverage

- [x] Controller tests for users/groups/ous endpoints with happy path and validation failures.
- [x] Manager tests for group create/update/delete and membership add/remove/replace behavior.
- [x] DN parsing and identifier resolution tests (DN, CN/account-based lookup).
- [x] Error mapping tests (409/422/500 pathways).

### 7.2 Integration Test Coverage

- [x] LDAP-backed integration tests for:
- [x] Create group
- [x] Add members to group
- [x] Remove members from group
- [x] Replace membership set
- [x] Create/update/delete OU
- [x] Exists/list endpoints for groups and OUs
- [x] Authentication/authorization behavior for `Reading` and `Writting` policies.

### 7.3 Regression and Contract Tests

- [x] Regression suite for existing v1/v2 endpoints that must remain stable.
- [x] API contract tests for request/response schema and status codes.
- [x] Negative tests for malformed DN, missing required fields, unknown members, and inaccessible LDAP.

### 7.4 CI Quality Gates

- [x] Enforce: build + unit + integration + regression tests must pass.
- [x] Enforce minimum coverage threshold for changed modules.
- [x] Block merges on failing tests.

Deliverable: CI gates requiring passing tests and coverage for group/OU features. Stage 7 progress details: `/Users/felipe/Dev/adrapi/docs/STAGE7_TESTING_QUALITY.md`.

## 8. Documentation and Client Usage (1 day)

- [x] Update API reference and usage docs with new group/OU flows and examples.
- [x] Add migration notes for clients moving to new endpoints.
- [x] Add troubleshooting examples for membership sync and OU operations.
- [x] Make shure all operations are logged and that the logs register the ip and the api identification
- Deliverable: published docs + sample curl collection.


## Progress Log

Use this section to record dated updates.

- 2026-02-13: `Stage 1` started.
- 2026-02-13: `Stage 1` completed. Notes: discovery spec created at `/Users/felipe/Dev/adrapi/docs/STAGE1_DISCOVERY_SPEC.md`.
- 2026-02-13: `Stage 2` completed. Notes: v2 contracts implemented in `/Users/felipe/Dev/adrapi/adrapi/Controllers/V2/GroupsController.cs` and `/Users/felipe/Dev/adrapi/adrapi/Controllers/OUsController.cs`; contract doc at `/Users/felipe/Dev/adrapi/docs/STAGE2_API_CONTRACT.md`.
- 2026-02-13: `Stage 3` completed. Notes: group create validation, strict member resolution, and all-or-fail membership update logic implemented in `/Users/felipe/Dev/adrapi/adrapi/Controllers/V2/GroupsController.cs`.
- 2026-02-13: `Stage 4` completed. Notes: OU validation and protected/system OU guardrails implemented in `/Users/felipe/Dev/adrapi/adrapi/Controllers/OUsController.cs`.
- 2026-02-13: `Stage 5` completed. Notes: authorization consistency and explicit 4xx/5xx error mapping hardened in `/Users/felipe/Dev/adrapi/adrapi/Controllers/V2/GroupsController.cs`, `/Users/felipe/Dev/adrapi/adrapi/Controllers/OUsController.cs`, `/Users/felipe/Dev/adrapi/adrapi/Controllers/GroupsController.cs`, and `/Users/felipe/Dev/adrapi/adrapi/Controllers/UsersController.cs` (including v1 hardening).
- 2026-02-13: `Stage 6` completed. Notes: structured audit logs with correlation IDs, requester, target DN, change summary, and client IP were added via `/Users/felipe/Dev/adrapi/adrapi/Controllers/BaseController.cs` and wired into group/OU mutation flows.
- 2026-02-13: `Stage 7` started. Notes: added controller/audit unit tests and NUKE `Test` + `Quality_Gate` targets in `/Users/felipe/Dev/adrapi/build/Build.cs`.
- 2026-02-13: `Stage 7` progress update. Notes: expanded test suite to 18 passing tests (controller validation/regression + audit helper tests) and enabled coverage artifact generation in `Quality_Gate`.
- 2026-02-13: `Stage 7.1` completed. Notes: test suite expanded to 22 passing tests including manager logic unit tests.
- 2026-02-13: `Stage 7.2` completed. Notes: LDAP-backed integration tests added in `/Users/felipe/Dev/adrapi/tests/LdapIntegrationTests.cs` and gated by `ADRAPI_RUN_LDAP_INTEGRATION=1`; default quality gate includes conditional integration target.
- 2026-02-13: `Stage 7.3` completed. Notes: regression/contract/negative coverage completed with `/Users/felipe/Dev/adrapi/tests/RegressionContractTests.cs`, `/Users/felipe/Dev/adrapi/tests/ApiContractTests.cs`, and `/Users/felipe/Dev/adrapi/tests/NegativePathTests.cs`; `dotnet test` and NUKE `Quality_Gate` pass with 33 tests.
- 2026-02-13: `Stage 7.4` completed. Notes: explicit unit/regression/integration quality gates plus changed-module coverage threshold are enforced in `/Users/felipe/Dev/adrapi/build/Build.cs` and `/Users/felipe/Dev/adrapi/scripts/check_changed_coverage.py`; merge-block CI workflow added at `/Users/felipe/Dev/adrapi/.github/workflows/quality-gate.yml`.
- 2026-02-13: `Stage 8` completed. Notes: docs updated with v2 group/OU usage and troubleshooting in `/Users/felipe/Dev/adrapi/docs/API_REFERENCE.md` and `/Users/felipe/Dev/adrapi/docs/USAGE_GUIDE.md`; migration guide added at `/Users/felipe/Dev/adrapi/docs/MIGRATION_NOTES.md`; sample curl collection added at `/Users/felipe/Dev/adrapi/docs/CURL_COLLECTION.md`; logging/audit traceability contract (requester + clientIp + correlationId) documented.
