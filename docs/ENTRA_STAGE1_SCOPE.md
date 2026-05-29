# Entra ID Integration — Stage 1: Discovery and Scope

Status: **complete**. This is the deliverable for Stage 1 of the
*Microsoft Entra ID Integration* roadmap (`roadmap.md`): a scope document with a
capability mapping and a gap list. It does not change code.

Context: adrapi today is an HTTP front end over **on-prem Active Directory / LDAP**
(Novell.Directory.Ldap), reached via the static manager chain
`Controller → {User,Group,OU}Manager → LdapQueryManager → LdapConnectionManager`.
Multi-domain routing already exists (`/api/{domain}/...`), with one `LdapConfig`
and one connection pool per domain. Entra ID integration builds on that seam.

---

## 1. Inventory of current on-prem LDAP/AD capabilities

Authoritative source: `docs/API_REFERENCE.md` and the V2 controllers/managers.
Every operation below must have an Entra ID equivalent or an explicit gap entry.

### Users (`UserManager`)

| Capability | Endpoint | LDAP mechanism today |
|---|---|---|
| List (paged, cookie) | `GET /api/users` | paged search `objectClass=user`, sorted by `cn` |
| List full objects | `GET /api/users?_full=true` | subtree search |
| Get by DN or attribute | `GET /api/users/{user}` | `ReadAsync(dn)` or filter by `sAMAccountName`/`userPrincipalName` |
| Exists | `GET /api/users/{user}/exists` | lookup → 200/404 |
| Inspect attributes | `GET /api/users/{user}/attributes` | read all attributes + ranged `memberOf` |
| Is member-of group | `GET /api/users/{dn}/member-of/{group}` | resolve user, compare `memberOf` |
| List user's groups | `GET /api/users/{user}/groups` | `memberOf` (with range retrieval) |
| Authenticate (locator) | `POST /api/users/{userId}/authenticate` | resolve then LDAP **bind** as user |
| Authenticate (direct) | `POST /api/users/authenticate` | LDAP **bind** with supplied login |
| Create / update | `PUT /api/users/{dn}` | `AddAsync` / `ModifyAsync` |
| Delete | `DELETE /api/users/{userId}` | `DeleteAsync` |

User attributes in play: `name`, `givenName`, `sn`, `mail`, `userPrincipalName`,
`sAMAccountName`, `description`, `objectSid`, `distinguishedName`, `memberOf`,
`mobile`, `unicodePwd` (password set, SSL-gated), `userAccountControl` (enable/disable).

### Groups (`GroupManager`)

| Capability | Endpoint | LDAP mechanism today |
|---|---|---|
| List CNs / full | `GET /api/groups[?_full=true]` | search `objectClass=group` |
| Get by DN/CN | `GET /api/groups/{groupId}` | `ReadAsync` or `cn=` filter |
| Exists | `GET /api/groups/{dn}/exists` | lookup → 200/404 |
| List members | `GET /api/groups/{groupId}/members` | `member` attribute (optionally CN-projected) |
| Create | `POST /api/groups` / `PUT /api/groups/{dn}` | `AddAsync` |
| Update | `PUT /api/groups/{dn}` | `ModifyAsync` |
| Replace members | `PUT /api/groups/{dn}/members` | delete+add `member` (all-or-fail) |
| Add/remove delta | `PATCH /api/groups/{dn}/members` | compute set, `ModifyAsync` |
| Delete | `DELETE /api/groups/{dn}` | `DeleteAsync` |

Member identifier resolution is strict (DN, group CN, user `sAMAccountName`, user `cn`).

### OUs (`OUManager`)

| Capability | Endpoint | LDAP mechanism today |
|---|---|---|
| List / Get / Exists | `GET /api/ous[...]` | search/read `organizationalUnit` |
| Create / Update / Delete | `POST`/`PUT`/`DELETE /api/ous/{dn}` | add/modify/delete, with protected-OU + search-base guardrails |

### Cross-cutting

- Auth model: `api-key` header, `Reading`/`Writting` policies (unchanged by backend).
- Audit: `BaseController.LogAudit` (requester, correlationId, clientIp, targetDn, change).
- Identifiers are **DN-centric** throughout (request paths, member lists, responses).
- Paging via LDAP cookie; `maxResults` cap; per-domain `searchBase`.

---

## 2. Integration model — decision

**Decision: Entra ID is an additional, per-domain backend (hybrid / coexistence).
Not a replacement.**

Rationale:
- The multi-domain feature already selects a directory per request via
  `/api/{domain}/...` and a per-domain config + connection pool. Entra ID slots in
  as *another kind of domain* rather than a rewrite: a domain is backed either by
  LDAP (today) or by Entra ID/Microsoft Graph (new).
- adrapi remains the **client** calling outward (adrapi → Entra ID via Graph);
  this is the direction the team confirmed and the reason SCIM (Entra-as-client)
  was rejected.
- Coexistence is the realistic deployment: customers run on-prem AD and Entra ID
  side by side. A single adrapi instance can serve `/api/corp/users` (LDAP) and
  `/api/cloud/users` (Entra ID) simultaneously.

Implementation seam (for Stage 3): introduce a directory-provider abstraction so
the managers depend on an interface (`ILdapDirectoryProvider`-style) with an LDAP
implementation (current code) and a Graph implementation. The per-domain config
gains a `kind` (`ldap` | `entraid`); `LdapDomainRegistry` selects the provider.
**No DI rewrite required** — the registry already centralizes per-domain config.

Rejected alternatives:
- *Replacement*: breaks every on-prem deployment; not acceptable.
- *Sync/provisioning (SCIM, AD Connect-style)*: wrong direction and a different
  product; out of scope.

---

## 3. Concept mapping: AD/LDAP → Entra ID

| adrapi / AD concept | Entra ID equivalent | Notes / gaps |
|---|---|---|
| Distinguished Name (DN) | Object **id** (GUID) and **userPrincipalName** | No DN in Entra ID. Need an identifier-translation layer; routes/members keyed by DN must accept/return id/UPN. **Major contract impact.** |
| `objectSid` | `id` (GUID); `onPremisesSecurityIdentifier` if synced | SID only present for synced objects. |
| User (`user` class) | `/users` resource | Direct mapping. |
| `sAMAccountName` | `onPremisesSamAccountName` (synced) / use `userPrincipalName` | sAMAccountName only for synced users → prefer UPN. |
| `userPrincipalName` | `userPrincipalName` | Direct. |
| `givenName`/`sn`/`mail`/`mobile`/`displayName`/`description` | `givenName`/`surname`/`mail`/`mobilePhone`/`displayName`/no direct (`jobTitle`/notes) | `description` has no direct user property. |
| `userAccountControl` (enable/disable) | `accountEnabled` (bool) | Simpler boolean. |
| `unicodePwd` (set password) | `passwordProfile` (create) / `authentication/methods` (reset) | Different model; reset needs higher privilege. |
| Authenticate (LDAP bind) | **No password bind via Graph** | ROPC/OAuth flows exist but are discouraged/often blocked by Conditional Access + MFA. **Likely unsupported / out of scope** — document as gap. |
| Group (`group` class) | `/groups` (security and Microsoft 365) | `securityEnabled`/`mailEnabled` flags distinguish types. |
| `member` / `memberOf` | `/groups/{id}/members`, `/users/{id}/memberOf` | `$ref` add/remove; transitive vs direct membership differs. |
| Nested/dynamic groups | dynamic membership rules | Read-only membership for dynamic groups. |
| Organizational Unit (`organizationalUnit`) | **No equivalent** (administrative units ≠ OUs) | **Hard gap.** OU endpoints are LDAP-only (already documented). AUs are a partial analog via Graph but not a drop-in. |
| `searchBase` scoping | tenant + optional AU scope | No DN tree; scoping is tenant-wide or AU-based. |
| Directory roles (implicit) | `/directoryRoles`, role assignments | Not currently exposed by adrapi; future. |

---

## 4. Microsoft Graph API surface + permissions

Calling style: **application permissions** (client-credentials / app-only) for
service-to-service automation, matching adrapi's service-bind model. Delegated
permissions only if a future feature acts on behalf of a signed-in user.

| adrapi operation | Graph endpoint | App permission (least privilege) |
|---|---|---|
| List/get/exists user | `GET /users`, `GET /users/{id\|upn}` | `User.Read.All` |
| User attributes/groups | `GET /users/{id}`, `GET /users/{id}/memberOf` | `User.Read.All`, `GroupMember.Read.All` |
| Create user | `POST /users` | `User.ReadWrite.All` |
| Update / enable-disable user | `PATCH /users/{id}` (`accountEnabled`) | `User.ReadWrite.All` |
| Delete user | `DELETE /users/{id}` | `User.ReadWrite.All` |
| Reset password | `POST /users/{id}/authentication/.../resetPassword` or `PATCH passwordProfile` | `User-PasswordProfile.ReadWrite.All` / privileged; often needs admin role |
| Authenticate (verify password) | — | **No app-only Graph path**; gap (see §3) |
| List/get/exists group | `GET /groups`, `GET /groups/{id}` | `Group.Read.All` |
| Group members (list) | `GET /groups/{id}/members` | `GroupMember.Read.All` |
| Create/update/delete group | `POST/PATCH/DELETE /groups/{id}` | `Group.ReadWrite.All` |
| Add/remove members | `POST/DELETE /groups/{id}/members/$ref` | `GroupMember.ReadWrite.All` |
| OU operations | — | **Unsupported on Entra ID** (no OU object) |

Operational notes for later stages: Graph requires **admin consent** for these
application permissions; responses are **paged** (`@odata.nextLink`); throttling
returns **429** with `Retry-After`; auth via **MSAL** client-credentials against
`https://login.microsoftonline.com/{tenant}/oauth2/v2.0/token` with scope
`https://graph.microsoft.com/.default`.

---

## 5. Gap list (carried into later stages)

1. **Identifier model (DN → id/UPN).** adrapi is DN-centric; Entra ID has no DN.
   Needs a translation layer and a decision on how `{domain}/users/{key}` and
   group member lists are keyed for Entra domains. *Highest-impact gap.* (Stage 6)
2. **OUs unsupported.** No Entra ID equivalent; `/api/ous` returns a clear
   "unsupported for this domain" response on Entra domains. (Stages 4/6)
3. **Password authentication unsupported.** LDAP bind has no app-only Graph
   equivalent; `POST /authenticate` likely returns "unsupported" on Entra domains.
   Re-evaluate only if a delegated/ROPC flow is explicitly required. (Stage 4)
4. **`description` and `sAMAccountName` semantics differ.** Map to nearest Graph
   property or omit; document per-field behavior. (Stage 4)
5. **Group type & membership semantics.** security vs M365 groups; dynamic-group
   membership is read-only; transitive vs direct membership must be chosen. (Stage 5)
6. **Throttling/paging/consent** are new operational concerns absent from LDAP. (Stages 3/7)
7. **Secrets:** client secret/certificate handling for the app registration must
   reuse adrapi's encrypted-secret pipeline, not plaintext config. (Stage 7)

---

## 6. Stage exit

All Stage 1 checklist items are satisfied by this document: capability inventory
(§1), integration-model decision (§2), concept mapping (§3), Graph surface and
permissions (§4), and the consolidated gap list (§5). Stage 2 (API contract) can
proceed against the per-domain additive-backend model.
