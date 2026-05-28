# Stage 7 Testing and Quality Gates

Date: 2026-02-13

## Scope

This stage introduces broader automated checks for controller validation paths and establishes a build/test quality gate in NUKE.

## Implemented

### New Tests

- `/Users/felipe/Dev/adrapi/tests/ControllerValidationTests.cs`
- validates conflict/bad-request paths without LDAP dependencies
- covers:
- v2 groups invalid DN validation (`PUT`, `exists`)
- OU DN/name validation guards
- users authenticate null-body guard (`400`)

- `/Users/felipe/Dev/adrapi/tests/BaseControllerAuditTests.cs`
- covers audit helper behavior:
- requester fallback (`unknown`)
- correlation-id header resolution
- client IP extraction from `X-Forwarded-For`

- `/Users/felipe/Dev/adrapi/tests/RegressionContractTests.cs`
- regression-oriented controller contract tests for v1/v2 invalid-input and guardrail behavior
- covers protected OU delete guard, DN boundary checks, and group/users validation/error pathways

- `/Users/felipe/Dev/adrapi/tests/LdapIntegrationTests.cs`
- LDAP-backed integration flows for:
- group create/add/remove/replace membership
- unknown-member membership update returns `422` (integration-only case)
- OU create/update/delete and exists/list checks
- auth policy declaration checks for `Reading`/`Writting`
- execution gate: set `ADRAPI_RUN_LDAP_INTEGRATION=1`

- `/Users/felipe/Dev/adrapi/tests/ManagerLogicTests.cs`
- manager-level unit coverage for deterministic create/update/delete internals:
- group/OU LDAP attribute-set generation
- group/OU LDAP entry scalar-field mapping

- `/Users/felipe/Dev/adrapi/tests/ApiContractTests.cs`
- API/request contract coverage for:
- required DTO fields for group/OU create payloads
- default collection invariants for membership patch payloads
- v2 groups members route template contracts

- `/Users/felipe/Dev/adrapi/tests/NegativePathTests.cs`
- negative-path coverage for:
- null payload guard (`400`)
- inaccessible LDAP behavior (`WrongParameterException`) in controller/manager paths

### Quality Gates (NUKE)

- `/Users/felipe/Dev/adrapi/build/Build.cs`
- added `Unit_Test` target (explicit non-integration lane)
- added `Regression_Test` target (explicit regression/contract/negative lane)
- added `Coverage` target (`Compile -> Coverage`) with `XPlat Code Coverage` collection
- added `Coverage_Threshold` target to enforce changed-module coverage threshold
- updated `Quality_Gate` target (`Compile -> Unit_Test + Regression_Test + Coverage_Threshold + Integration_Test`)
- added conditional `Integration_Test` target (`OnlyWhen ADRAPI_RUN_LDAP_INTEGRATION=1`)
- quality gate now enforces build + unit + regression + conditional integration + changed-module coverage

## Current Test Status

- `dotnet test /Users/felipe/Dev/adrapi/adrapi.sln --configuration Release` passes with expanded test set (`33` tests).
- `dotnet run --project /Users/felipe/Dev/adrapi/build/_build.csproj -- Quality_Gate --configuration Release` passes.
- `Quality_Gate` emits coverage report at:
- `/Users/felipe/Dev/adrapi/artifacts/coverage/<run-id>/coverage.cobertura.xml`
- changed-module coverage is enforced by:
- `/Users/felipe/Dev/adrapi/scripts/check_changed_coverage.py`

## CI Merge Gate

- `/Users/felipe/Dev/adrapi/.github/workflows/quality-gate.yml`
- runs NUKE `Quality_Gate` on pull requests and pushes to `main`/`master`
- fails the workflow when unit/regression/coverage gates fail
- includes optional `ldap-integration` job gated by repo variable `ADRAPI_RUN_LDAP_INTEGRATION=1`
- this workflow is intended to be configured as a required status check in branch protection to block merges on failing tests
