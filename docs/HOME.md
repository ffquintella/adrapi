# ADRAPI

> Active Directory REST API for querying and managing users, groups, and OUs over LDAP.

ADRAPI exposes versioned REST endpoints for **users**, **groups**, and
**organizational units (OUs)**, performs LDAP-backed read/write operations, and
secures every request with API-key authentication and claims-based
authorization (`isAdministrator`, `isMonitor`).

## Start here

- **[Usage Guide](USAGE_GUIDE.md)** — run the service and call the API.
- **[API Reference](API_REFERENCE.md)** — endpoints, contracts, responses.
- **[CLI Tools](CLI_TOOLS.md)** — manage API keys and encrypted secrets with `adrapi-api-keys`.
- **[Curl Collection](CURL_COLLECTION.md)** — copy-paste request examples.

## Reference

- [Code Overview](CODE_OVERVIEW.md)
- [Migration Notes (v1 → v2)](MIGRATION_NOTES.md)

## Implementation stages

The service was built in documented stages — useful background on design
decisions:

- [Stage 1 — Discovery Spec](STAGE1_DISCOVERY_SPEC.md)
- [Stage 2 — API Contract](STAGE2_API_CONTRACT.md)
- [Stage 3 — Group Implementation](STAGE3_GROUP_IMPLEMENTATION.md)
- [Stage 4 — OU Management](STAGE4_OU_MANAGEMENT.md)
- [Stage 5 — Security Hardening](STAGE5_SECURITY_HARDENING.md)
- [Stage 6 — Observability & Auditability](STAGE6_OBSERVABILITY_AUDITABILITY.md)
- [Stage 7 — Testing & Quality](STAGE7_TESTING_QUALITY.md)
