# Roadmap: Group Lifecycle + OU Management

## Tracking Structure

Use this section to track execution status for each stage and acceptance item.

### Status Legend

- `[x]` Done
- `[ ]` Not done
- `[-]` In progress

### Stage Status

- [x] Stage 1 - Discovery and Gap Validation
- [ ] Stage 2 - API Contract Definition
- [ ] Stage 3 - Group Management Implementation (Core)
- [ ] Stage 4 - OU Management Implementation
- [ ] Stage 5 - Security, Authorization, and Validation Hardening
- [ ] Stage 6 - Observability and Auditability
- [ ] Stage 7 - Testing and Quality Gates
- [ ] Stage 8 - Documentation and Client Usage
- [ ] Stage 9 - Rollout Strategy
- [ ] Stage 10 - Acceptance Criteria Sign-off

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

- [ ] Standardize v2 endpoints and payloads for group management:
- [ ] `POST /api/groups` (create)
- [ ] `PATCH /api/groups/{dn}/members` (add/remove delta)
- [ ] `PUT /api/groups/{dn}/members` (replace full set)
- [ ] `GET /api/groups/{dn}/members`
- [ ] Define OU contract stage:
- [ ] `POST /api/ous`
- [ ] `PUT /api/ous/{dn}`
- [ ] `DELETE /api/ous/{dn}`
- [ ] `GET /api/ous`, `GET /api/ous/{dn}`, `GET /api/ous/{dn}/exists`
- Deliverable: versioned API contract + response code matrix.

## 3. Group Management Implementation (Core) (3-5 days)

- [ ] Implement group create flow with DN validation and duplicate checks.
- [ ] Implement membership delta operations (add/remove) with idempotent behavior.
- [ ] Implement full replace membership operation.
- [ ] Resolve member identifiers safely (DN vs account/CN), with strict validation.
- [ ] Add transactional-style safeguards per request (all-or-fail semantics for batch member changes).
- Deliverable: working CRUD + membership edit feature for groups.

## 4. OU Management Implementation (2-4 days)

- [ ] Implement/complete OU create, update, delete flows with schema validation.
- [ ] Add guardrails for protected/system OUs and invalid DN operations.
- [ ] Ensure OU operations are consistent with group/user location rules.
- Deliverable: complete OU lifecycle management.

## 5. Security, Authorization, and Validation Hardening (1-2 days)

- [ ] Enforce `Reading` vs `Writting` policies consistently.
- [ ] Add LDAP input hardening and DN parsing validation on all new endpoints.
- [ ] Add clear 4xx errors for invalid requests and 5xx only for real server faults.
- Deliverable: security review checklist + hardened handlers.

## 6. Observability and Auditability (1 day)

- [ ] Add structured logs for create/update/delete and membership changes.
- [ ] Add correlation IDs and explicit audit fields (requester, target DN, change summary).
- Deliverable: actionable logs for support and incident response.

## 7. Testing and Quality Gates (2-3 days)

### 7.1 Unit Test Coverage

- [ ] Controller tests for users/groups/ous endpoints with happy path and validation failures.
- [ ] Manager tests for group create/update/delete and membership add/remove/replace behavior.
- [ ] DN parsing and identifier resolution tests (DN, CN/account-based lookup).
- [ ] Error mapping tests (409/422/500 pathways).

### 7.2 Integration Test Coverage

- [ ] LDAP-backed integration tests for:
- [ ] Create group
- [ ] Add members to group
- [ ] Remove members from group
- [ ] Replace membership set
- [ ] Create/update/delete OU
- [ ] Exists/list endpoints for groups and OUs
- [ ] Authentication/authorization behavior for `Reading` and `Writting` policies.

### 7.3 Regression and Contract Tests

- [ ] Regression suite for existing v1/v2 endpoints that must remain stable.
- [ ] API contract tests for request/response schema and status codes.
- [ ] Negative tests for malformed DN, missing required fields, unknown members, and inaccessible LDAP.

### 7.4 CI Quality Gates

- [ ] Enforce: build + unit + integration + regression tests must pass.
- [ ] Enforce minimum coverage threshold for changed modules.
- [ ] Block merges on failing tests.

Deliverable: CI gates requiring passing tests and coverage for group/OU features.

## 8. Documentation and Client Usage (1 day)

- [ ] Update API reference and usage docs with new group/OU flows and examples.
- [ ] Add migration notes for clients moving to new endpoints.
- [ ] Add troubleshooting examples for membership sync and OU operations.
- Deliverable: published docs + sample curl collection.


## Progress Log

Use this section to record dated updates.

- 2026-02-13: `Stage 1` started.
- 2026-02-13: `Stage 1` completed. Notes: discovery spec created at `/Users/felipe/Dev/adrapi/docs/STAGE1_DISCOVERY_SPEC.md`.
