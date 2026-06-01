# Entra ID Integration — Stage 9: Testing and Quality Gates

Status: **complete**. Deliverable: passing quality gates for the Entra ID
integration — unit, gated tenant integration, cross-backend contract, and CI
gates with a changed-module coverage threshold.

---

## What was implemented

| Concern | Where |
|---|---|
| Cross-backend response-shape contract tests | `tests/BackendContractTests.cs` |
| Gated real-tenant integration tests | `tests/EntraGraphIntegrationTests.cs` |
| NUKE gated Entra integration target + gate wiring | `build/Build.cs` |
| CI job for Entra integration | `.github/workflows/quality-gate.yml` |

## 1. Unit tests (token / paging / throttling / mapping)

Covered cumulatively across the earlier stages; all run in the standard
(non-gated) unit suite:

| Area | Tests |
|---|---|
| Token acquisition (config/validation, MSAL inputs) | `EntraAuthTests` |
| Graph client paging, 429/5xx retry, Retry-After, errors | `GraphClientTests` |
| User mapping + provider lifecycle | `GraphUserTests` |
| Group mapping + membership ops | `GraphGroupTests` |
| Identifier translation + normalization + OU non-support | `DirectoryMappingTests` |
| Error mapping, tenant validation, OData hardening | `EntraSecurityTests` |
| Audit/observability | `GraphAuditTests` |

## 2. Integration tests against a test tenant (gated)

`EntraGraphIntegrationTests` exercises the real Graph API through
`GraphDirectoryProvider` (user lifecycle: create → exists → get → search →
disable → set-password → delete; group + membership: create → add → idempotent
re-add → replace-empty → delete, with cleanup). It is **gated exactly like the
LDAP integration tests** — a no-op unless:

```
ADRAPI_RUN_ENTRA_INTEGRATION=1
ENTRA_TENANT_ID=<guid>   ENTRA_CLIENT_ID=<guid>   ENTRA_CLIENT_SECRET=<secret>
ENTRA_TEST_UPN_DOMAIN=<verified-domain, e.g. contoso.onmicrosoft.com>
```

The app registration needs `User.ReadWrite.All`, `Group.ReadWrite.All`, and
`GroupMember.ReadWrite.All` with admin consent.

## 3. Contract/regression: stable shapes across backends

`BackendContractTests` freezes the v2 `User`/`Group` JSON shapes and asserts the
shape is **identical regardless of backend** after normalization, plus the
identifier contract (Entra `DN == null`, LDAP `DN` preserved, both keep `ID`;
every group reports a `GroupType`). A field drift fails these tests, forcing a
deliberate contract change.

## 4. CI gates

`build/Build.cs`:

- **Unit_Test** excludes the gated LDAP/Entra integration tests and the
  ordering-sensitive regression suites.
- **Entra_Integration_Test** (new) — `OnlyWhenDynamic(ADRAPI_RUN_ENTRA_INTEGRATION == 1)`,
  mirroring `Integration_Test` for LDAP.
- **Quality_Gate** now depends on `Unit_Test, Regression_Test, Integration_Test,
  Entra_Integration_Test, Coverage_Threshold` — build + unit + regression +
  conditional LDAP/Entra integration must pass, and the changed-module coverage
  threshold (default 0.70) is enforced. New Entra/Directory files are picked up
  automatically by the git-diff-based `scripts/check_changed_coverage.py`.

`.github/workflows/quality-gate.yml` adds an `entra-integration` job, gated by the
`ADRAPI_RUN_ENTRA_INTEGRATION` repo variable, sourcing tenant/app credentials
from repo secrets.

## Testing

- `dotnet test` — full suite (547 tests; the gated integration tests no-op without
  the flag).
- `dotnet run --project build/_build.csproj -- Quality_Gate` — the full gate.
