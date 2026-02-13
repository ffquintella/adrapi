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

- `/Users/felipe/Dev/adrapi/tests/ManagerLogicTests.cs`
- manager-level unit coverage for deterministic create/update/delete internals:
- group/OU LDAP attribute-set generation
- group/OU LDAP entry scalar-field mapping

### Quality Gates (NUKE)

- `/Users/felipe/Dev/adrapi/build/Build.cs`
- added `Test` target (`Compile -> Test`)
- added `Coverage` target (`Compile -> Coverage`) with `XPlat Code Coverage` collection
- updated `Quality_Gate` target (`Compile -> Coverage + Test -> Quality_Gate`)
- quality gate now enforces successful build, tests, and coverage artifact generation

## Current Test Status

- `dotnet test /Users/felipe/Dev/adrapi/adrapi.sln` passes with expanded test set (`22` tests).
- `Quality_Gate` emits coverage report at:
- `/Users/felipe/Dev/adrapi/artifacts/coverage/<run-id>/coverage.cobertura.xml`

## Remaining Stage 7 Work

- manager-level behavioral tests for group create/update/delete and membership add/remove/replace
- LDAP-backed integration tests for group/OU lifecycle contracts
- regression test suite for stable v1/v2 behavior
- coverage threshold enforcement for changed modules
- merge-block policy wiring in CI provider
