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


---

# Roadmap: Microsoft Entra ID Integration

## Tracking Structure

Status legend is the same as above (`[x]` done, `[ ]` not done, `[-]` in progress).

### Stage Status

- [x] Stage 1 - Discovery and Scope Definition
- [x] Stage 2 - Authentication and Authorization (Entra ID OAuth2/OIDC)
- [x] Stage 3 - Microsoft Graph Client Foundation
- [x] Stage 4 - User Management via Graph
- [x] Stage 5 - Group and Membership Management via Graph
- [x] Stage 6 - Directory Object Mapping and Abstraction
- [x] Stage 7 - Security, Secrets, and Tenant Configuration
- [x] Stage 8 - Observability and Auditability
- [x] Stage 9 - Testing and Quality Gates
- [x] Stage 10 - Documentation and Client Usage

## 1. Discovery and Scope Definition

- [x] Inventory current on-prem LDAP/AD capabilities that must have an Entra ID equivalent (users, groups, OUs, membership).
- [x] Decide integration model: Entra ID as an additional backend vs. replacement vs. hybrid (sync/coexistence). **Decision: additive per-domain backend (hybrid/coexistence), reusing the multi-domain seam.**
- [x] Map Entra ID concepts to existing adrapi concepts (e.g. OUs → administrative units, security/Microsoft 365 groups, directory roles).
- [x] Identify Microsoft Graph API surface and required permissions (delegated vs. application).
- Deliverable: scope document with capability mapping and gap list. See `/Users/felipe/Dev/adrapi/docs/ENTRA_STAGE1_SCOPE.md`.

## 2. Authentication and Authorization (Entra ID OAuth2/OIDC)

- [x] Implement app registration onboarding (client ID, tenant ID, client secret/certificate). Config under `ldap:domains:{name}:entra` with `kind: entraid`; secret sourced from the encrypted store; validated at startup.
- [x] Support OAuth2 client-credentials flow for service-to-service Graph access. `EntraTokenProvider` via MSAL confidential client.
- [x] Acquire and cache/refresh Graph access tokens (MSAL). MSAL in-memory app token cache, one client per domain, auto-refresh.
- [x] Map adrapi `Reading`/`Writting` policies onto required Graph scopes/app roles. `EntraScopeMap` (ReadWrite implies Read) + coverage validation.
- Deliverable: working token acquisition with secure secret handling. See `/Users/felipe/Dev/adrapi/docs/ENTRA_STAGE2_AUTH.md`.

## 3. Microsoft Graph Client Foundation

- [x] Add a Graph client wrapper with retry, throttling (429) handling, and paging. `GraphClient` in `adrapi/Entra/GraphClient.cs` (Retry-After-aware backoff, `@odata.nextLink` paging, bearer-token injection).
- [x] Abstract a directory provider interface so LDAP and Graph share a contract. `IDirectoryProvider` + `LdapDirectoryProvider` (reference) + `GraphDirectoryProvider` under `adrapi/Directory/`.
- [x] Configuration switch to select backend per request or per deployment. `DirectoryProviderFactory` selects on domain `kind` (route `{domain}` = per request, default domain = per deployment).
- Deliverable: reusable Graph client and provider abstraction. See `/Users/felipe/Dev/adrapi/docs/ENTRA_STAGE3_GRAPH_CLIENT.md`.

## 4. User Management via Graph

- [x] Read user (get/list/search/exists). `GraphDirectoryProvider` via `GraphClient` paging + `GraphUserMapper`; 404 → null/false.
- [x] Create/update/disable/delete user. `POST/PATCH/DELETE /users`; disable via `accountEnabled`. LDAP parity incl. `UserManager.SetAccountEnabledAsync` (userAccountControl bit).
- [x] Password/credential operations where applicable. Graph `PATCH passwordProfile`; LDAP `unicodePwd` via `SaveUserAsync` (LDAPS). Requires privileged grants (documented).
- Deliverable: user lifecycle parity through Graph. See `/Users/felipe/Dev/adrapi/docs/ENTRA_STAGE4_USER_MANAGEMENT.md`.

## 5. Group and Membership Management via Graph

- [x] Create/update/delete group (security and Microsoft 365). `GraphGroupMapper` maps `Group.GroupType` onto the Graph `groupTypes`/`mailEnabled`/`securityEnabled` trio; immutable `groupTypes` sent on create only.
- [x] Add/remove members (delta) and replace full membership set. Idempotent `members/$ref` add/remove and a diff-based replace in `GraphDirectoryProvider`.
- [x] List members and resolve member identifiers (UPN/objectId/DN-equivalent). Groups resolved by objectId or displayName; members by objectId or UPN; member listing prefers UPN, falls back to objectId.
- Deliverable: group + membership parity through Graph. See `/Users/felipe/Dev/adrapi/docs/ENTRA_STAGE5_GROUP_MANAGEMENT.md`.

## 6. Directory Object Mapping and Abstraction

- [x] Normalize response models so v2 endpoints return consistent shapes regardless of backend. `DirectoryObjectNormalizer` (trim/blank→null, Entra DN→null, default `GroupType`) applied on every provider read.
- [x] OU operations are LDAP/AD-only and are explicitly NOT supported on Entra ID (no OU object; administrative units are a separate Graph concept). `BaseController.TryResolveLdapDomain` rejects Entra domains with a 400; documented alternative (administrative units).
- [x] Handle identifier translation (DN ↔ objectId/UPN) at the edges. `DirectoryIdentifiers` classifies objectId/DN/UPN/Name; Graph edge rejects DNs (`EnsureGraphAddressable`); group/member identifiers resolved per backend.
- Deliverable: backend-agnostic API surface. See `/Users/felipe/Dev/adrapi/docs/ENTRA_STAGE6_MAPPING_ABSTRACTION.md`.

## 7. Security, Secrets, and Tenant Configuration

- [x] Secure storage for client secrets/certificates (no secrets in source/config-in-plaintext). Encrypted SQLite secret store overlay (Stage 2) + certificate alternative; gitignored config; documented in the checklist.
- [x] Least-privilege Graph permissions; document required admin consent. `EntraScopeMap` policy→app-role mapping; startup warns when `grantedPermissions` miss the read baseline; checklist documents required roles + consent.
- [x] Multi-tenant vs. single-tenant configuration support. `EntraConfig.Validate` rejects `common`/`organizations`/`consumers` (invalid for app-only); multi-tenant via one domain per customer tenant.
- [x] Input hardening and clear 4xx/5xx error mapping for Graph errors. `GraphQuery.EscapeODataLiteral` (OData injection) + `DirectoryErrorMapper` (deterministic 4xx/5xx ProblemDetails, no upstream leak, request-id surfaced).
- Deliverable: security review checklist for the Entra ID path. See `/Users/felipe/Dev/adrapi/docs/ENTRA_STAGE7_SECURITY.md`.

## 8. Observability and Auditability

- [x] Structured logs for Graph operations with correlation IDs, requester, target object, and client IP. `GraphClient` emits a `GraphOperationLog` per operation (success/error) enriched from the ambient `DirectoryOperationContext`; query strings stripped from the logged target.
- [x] Surface Graph request IDs in logs for cross-correlation with Microsoft support. `request-id`/`client-request-id` headers captured and included in every audit record.
- Deliverable: actionable, auditable logs for the Entra ID backend. See `/Users/felipe/Dev/adrapi/docs/ENTRA_STAGE8_OBSERVABILITY.md`.

## 9. Testing and Quality Gates

- [x] Unit tests for token acquisition, Graph client paging/throttling, and mapping logic. Covered across `EntraAuthTests`, `GraphClientTests`, `GraphUserTests`, `GraphGroupTests`, `DirectoryMappingTests`, `EntraSecurityTests`, `GraphAuditTests`.
- [x] Integration tests against a test tenant (gated by env flag, mirroring LDAP integration gating). `tests/EntraGraphIntegrationTests.cs` gated by `ADRAPI_RUN_ENTRA_INTEGRATION` (+ tenant/app env vars); user + group/membership lifecycle with cleanup.
- [x] Contract/regression tests ensuring v2 response shapes stay stable across backends. `tests/BackendContractTests.cs` freezes User/Group JSON shapes and asserts identical shape + identifier contract across LDAP/Entra.
- [x] CI gates: build + unit + integration must pass; coverage threshold for changed modules. NUKE `Entra_Integration_Test` target added to `Quality_Gate`; `entra-integration` CI job; changed-module coverage threshold covers new Entra/Directory files.
- Deliverable: passing quality gates for the Entra ID integration. See `/Users/felipe/Dev/adrapi/docs/ENTRA_STAGE9_TESTING_QUALITY.md`.

## 10. Documentation and Client Usage

- [x] App registration and admin-consent setup guide. `docs/ENTRA_ID_GUIDE.md` §1 (registration, per-policy Graph app permissions, admin consent, secret/cert).
- [x] Configuration reference for selecting/enabling the Entra ID backend. `docs/ENTRA_ID_GUIDE.md` §2 (`kind: entraid` + `entra` block, keyed table, secret-store commands).
- [x] Usage docs and sample curl collection for the Entra ID-backed endpoints. `docs/ENTRA_ID_GUIDE.md` §5 + `docs/CURL_COLLECTION.md` "Microsoft Entra ID-backed endpoints".
- [x] Migration/coexistence notes for clients moving from LDAP to Entra ID. `docs/MIGRATION_NOTES.md` "LDAP to Entra ID (coexistence)".
- Deliverable: published docs + sample collection. See `/Users/felipe/Dev/adrapi/docs/ENTRA_STAGE10_DOCS.md`.

---

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
- 2026-05-29: `Entra ID Stage 1` completed. Notes: scope document with capability inventory, integration-model decision (additive per-domain backend reusing the multi-domain seam), AD→Entra concept mapping, Graph API surface + app permissions, and gap list at `/Users/felipe/Dev/adrapi/docs/ENTRA_STAGE1_SCOPE.md`.
- 2026-05-29: `Entra ID Stage 2` completed. Notes: Entra auth layer added under `/Users/felipe/Dev/adrapi/adrapi/Entra/` (EntraConfig, EntraTokenProvider via MSAL client-credentials, EntraScopeMap policy→app-role mapping); domain-kind awareness + startup validation; client secret sourced from the encrypted store; 13 unit tests (full suite 466 passing). Doc at `/Users/felipe/Dev/adrapi/docs/ENTRA_STAGE2_AUTH.md`.
- 2026-05-29: `Entra ID Stage 4` completed. Notes: full user lifecycle over Graph in `/Users/felipe/Dev/adrapi/adrapi/Directory/GraphDirectoryProvider.cs` (read/list/search/exists/create/update/disable/delete + set-password) with `/Users/felipe/Dev/adrapi/adrapi/Entra/GraphUserMapper.cs`; `IDirectoryProvider` extended (exists/search/enable/password) with LDAP parity, incl. new `UserManager.SetAccountEnabledAsync` (userAccountControl ACCOUNTDISABLE bit); 16 unit tests added; controller test suite made deterministic (parallelization disabled + `LdapDomainRegistry.ClearCache()` priming); full suite 492 passing. Doc at `/Users/felipe/Dev/adrapi/docs/ENTRA_STAGE4_USER_MANAGEMENT.md`.
- 2026-06-01: `Entra ID Stage 10` completed. Notes: published Entra docs — setup/config/usage guide at `/Users/felipe/Dev/adrapi/docs/ENTRA_ID_GUIDE.md` (app registration + admin consent, configuration reference, capabilities/addressing, single vs multi-tenant, troubleshooting), Entra curl collection appended to `/Users/felipe/Dev/adrapi/docs/CURL_COLLECTION.md`, LDAP↔Entra coexistence/migration section in `/Users/felipe/Dev/adrapi/docs/MIGRATION_NOTES.md`, and docs navigation updated in `/Users/felipe/Dev/adrapi/docs/_sidebar.md`. Doc tracker at `/Users/felipe/Dev/adrapi/docs/ENTRA_STAGE10_DOCS.md`. **All 10 Entra ID stages complete.**
- 2026-06-01: `Entra ID Stage 9` completed. Notes: quality gates for the Entra path — cross-backend response-shape contract tests (`/Users/felipe/Dev/adrapi/tests/BackendContractTests.cs`), gated real-tenant integration tests (`/Users/felipe/Dev/adrapi/tests/EntraGraphIntegrationTests.cs`, `ADRAPI_RUN_ENTRA_INTEGRATION` + tenant/app env vars, mirroring LDAP gating), new NUKE `Entra_Integration_Test` target wired into `Quality_Gate`, and an `entra-integration` CI job in `/Users/felipe/Dev/adrapi/.github/workflows/quality-gate.yml`; unit coverage for token/paging/throttling/mapping confirmed across Stages 2–8 suites; 7 unit tests added (full suite 547 passing). Doc at `/Users/felipe/Dev/adrapi/docs/ENTRA_STAGE9_TESTING_QUALITY.md`.
- 2026-06-01: `Entra ID Stage 8` completed. Notes: auditable Graph logs — `DirectoryOperationContext` (`/Users/felipe/Dev/adrapi/adrapi/Directory/DirectoryOperationContext.cs`) ambient requester/correlation/clientIp scope; `GraphClient` emits a structured `GraphOperationLog` per operation (success & failure) via `IDirectoryAuditSink`/`NLogDirectoryAuditSink` (`/Users/felipe/Dev/adrapi/adrapi/Directory/DirectoryAudit.cs`), surfacing the Graph `request-id`/`client-request-id` and logging the target path without query strings; `BaseController.BeginDirectoryScope()` opens the scope from request metadata; 4 unit tests added (full suite 540 passing). Doc at `/Users/felipe/Dev/adrapi/docs/ENTRA_STAGE8_OBSERVABILITY.md`.
- 2026-06-01: `Entra ID Stage 7` completed. Notes: security checklist for the Entra path at `/Users/felipe/Dev/adrapi/docs/ENTRA_STAGE7_SECURITY.md`; `DirectoryErrorMapper` (`/Users/felipe/Dev/adrapi/adrapi/Directory/DirectoryErrorMapper.cs`) maps Graph/provider exceptions to deterministic 4xx/5xx `ProblemDetails` (no upstream leak on 5xx, Graph request-id surfaced); `EntraConfig.Validate` rejects `common`/`organizations`/`consumers` tenant placeholders (single vs multi-tenant); startup warns on missing read-baseline grants (least-privilege/admin-consent); `GraphQuery.EscapeODataLiteral` hardens OData `$filter` inputs; 21 unit tests added (full suite 536 passing).
- 2026-06-01: `Entra ID Stage 6` completed. Notes: backend-agnostic surface — `DirectoryObjectNormalizer` (`/Users/felipe/Dev/adrapi/adrapi/Directory/DirectoryObjectNormalizer.cs`) gives consistent User/Group shapes (trim, blank→null, Entra DN→null, default `GroupType`), applied on every Ldap/Graph provider read; `DirectoryIdentifiers` (`/Users/felipe/Dev/adrapi/adrapi/Directory/DirectoryIdentifiers.cs`) classifies objectId/DN/UPN/Name and the Graph provider rejects DN identifiers at the edge; OU operations rejected on Entra-backed domains via `BaseController.TryResolveLdapDomain` (wired into `OUsController`); 9 unit tests added (full suite 515 passing). Doc at `/Users/felipe/Dev/adrapi/docs/ENTRA_STAGE6_MAPPING_ABSTRACTION.md`.
- 2026-06-01: `Entra ID Stage 5` completed. Notes: group + membership parity through Graph — `GraphGroupMapper` (`/Users/felipe/Dev/adrapi/adrapi/Entra/GraphGroupMapper.cs`) maps security vs Microsoft 365 groups; `GraphDirectoryProvider` implements group CRUD and idempotent `members/$ref` add/remove plus diff-based replace, with group (objectId/displayName) and member (objectId/UPN) identifier resolution; `IDirectoryProvider` extended with `GroupExists`/membership ops and LDAP parity; `Group.GroupType` added to the shared model; 19 unit tests added (full suite 506 passing). Doc at `/Users/felipe/Dev/adrapi/docs/ENTRA_STAGE5_GROUP_MANAGEMENT.md`.
- 2026-05-29: `Entra ID Stage 3` completed. Notes: reusable Graph client (`/Users/felipe/Dev/adrapi/adrapi/Entra/GraphClient.cs`) with Retry-After-aware 429/5xx retry, `@odata.nextLink` paging, and `request-id` surfacing via `GraphException`; backend-agnostic directory abstraction under `/Users/felipe/Dev/adrapi/adrapi/Directory/` (`IDirectoryProvider`, `LdapDirectoryProvider`, `GraphDirectoryProvider`) with `DirectoryProviderFactory` selecting backend per request/deployment by domain kind; Graph CRUD deferred to Stage 4/5 and OUs explicitly unsupported on Entra ID; 13 unit tests added (full suite 479 passing). Doc at `/Users/felipe/Dev/adrapi/docs/ENTRA_STAGE3_GRAPH_CLIENT.md`.
