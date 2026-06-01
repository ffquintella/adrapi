# Entra ID Integration — Stage 10: Documentation and Client Usage

Status: **complete**. Deliverable: published docs + sample curl collection for the
Entra ID backend.

---

## What was produced

| Item | Where |
|---|---|
| App registration + admin-consent setup guide | `docs/ENTRA_ID_GUIDE.md` §1 |
| Configuration reference (enabling/selecting the backend) | `docs/ENTRA_ID_GUIDE.md` §2 |
| Capabilities + addressing/identifier rules | `docs/ENTRA_ID_GUIDE.md` §3 |
| Single-tenant vs multi-tenant | `docs/ENTRA_ID_GUIDE.md` §4 |
| Usage (REST contract) + troubleshooting | `docs/ENTRA_ID_GUIDE.md` §5–6 |
| Sample curl collection (Entra endpoints) | `docs/CURL_COLLECTION.md` → "Microsoft Entra ID-backed endpoints" |
| Migration / coexistence notes | `docs/MIGRATION_NOTES.md` → "LDAP to Entra ID (coexistence)" |
| Docs navigation | `docs/_sidebar.md` ("Microsoft Entra ID" section) |

## Coverage of the Stage 10 checklist

- **App registration and admin-consent setup guide** — step-by-step registration,
  the exact application Graph permissions per `Reading`/`Writting` policy, admin
  consent, and credential (secret/certificate) creation.
- **Configuration reference for selecting/enabling the Entra ID backend** — the
  `kind: entraid` + `entra` block, a keyed config table, secret-store commands,
  and the startup validation/warning behaviour.
- **Usage docs and sample curl collection** — the domain-scoped v2 routes, the
  identifier rules (objectId/UPN, no DN), security vs Microsoft 365 group
  creation, and the membership (delta/replace/list) curls.
- **Migration/coexistence notes** — the additive per-domain model, the LDAP↔Entra
  identifier/shape differences, and a per-workload cutover sequence.

## Note on integration status

The docs state the integration status plainly (see the banner in
`ENTRA_ID_GUIDE.md`): the provider layer, auth, secret handling, startup
validation, and OU-on-Entra rejection are live; wiring the user/group v2
controllers to dispatch to the Graph provider is the remaining integration step.
The documented routes/payloads are the stable target contract and are unchanged by
that wiring.
