# Directory configuration reference (`directories`)

Since **1.10.0** adrapi configures its directories under a backend-neutral
`directories` section. LDAP is one `kind` among peers rather than the section
that everything else hangs off.

This file is the precise reference for anything that *renders* adrapi's config —
notably the Puppet module `ffquintella/puppet-dockerapp_adrapi`.

---

## 1. Schema

```jsonc
"directories": {
  // Domain served by the domain-less routes (/api/users, ...). Optional;
  // defaults to "default". May name a domain of ANY kind.
  "defaultDomain": "corp",

  "domains": {
    "corp": {
      "kind": "ldap",                 // discriminator; "ldap" when omitted
      "ldap": {
        "servers": [ "dc-corp:636" ], // "host:port", at least one, required
        "ssl": true,                  // true -> LDAPS (636); false -> 389
        "poolSize": 10,
        "bindDn": "CN=svc,DC=corp,DC=example",
        "bindCredentials": "",        // leave empty here — see §3
        "searchBase": "DC=corp,DC=example",
        "searchFilter": "",
        "maxResults": 999,
        "adminCn": "",
        "trustedCertificatesFile": "cfg/ldap-trusted-certs.json"
      }
    },

    "cloud": {
      "kind": "entraid",
      "entra": {
        "tenantId": "<Directory (tenant) ID>",   // required, tenant-specific
        "clientId": "<Application (client) ID>", // required
        "clientSecret": "",                      // leave empty here — see §3
        // certificate alternative to a secret (exactly one of the two):
        // "certificatePath": "/run/secrets/adrapi-graph.p12",
        // "certificatePassword": "",            // leave empty here — see §3
        "authorityHost": "https://login.microsoftonline.com",
        "graphBaseUrl": "https://graph.microsoft.com/v1.0",
        "scopes": [ "https://graph.microsoft.com/.default" ],
        "grantedPermissions": [ "User.Read.All", "Group.Read.All" ]
      }
    }
  }
}
```

### Rules

- **Domain names** are case-insensitive and may not be `users`, `groups`, `ous`
  or `infos` — they would make routes like `/api/users/users` ambiguous.
- **`kind`** selects the backend: `ldap` (default) or `entraid`. Adding a future
  backend (SCIM/Okta/Google) is a new `kind` with its own sub-object, not a new
  top-level section. An unrecognised `kind` fails startup validation.
- **`defaultDomain`** must name a domain in `directories:domains` — unless the
  deprecated top-level `ldap` section is still present, which then backs it (§2).
- A domain of `kind: ldap` may also carry its LDAP settings **flat** on the
  domain object instead of inside an `ldap` sub-object; this exists so a legacy
  block can be moved across verbatim. The `ldap` sub-object is the documented
  shape — prefer it.
- OU endpoints are LDAP-only; they return 400 on an `entraid` domain.

---

## 2. Deprecated layout (still read, **removed in 2.0.0**)

Up to 1.9.0 the same information lived under `ldap`:

| Deprecated                            | Current                                            |
| ------------------------------------- | -------------------------------------------------- |
| `ldap:defaultDomain`                  | `directories:defaultDomain`                        |
| `ldap` (top level = default domain)   | `directories:domains:<default>:ldap`               |
| `ldap:domains:<name>` (flat)          | `directories:domains:<name>:ldap`                  |
| `ldap:domains:<name>:kind`            | `directories:domains:<name>:kind`                  |
| `ldap:domains:<name>:entra`           | `directories:domains:<name>:entra`                 |

Resolution order, per lookup (`adrapi/Directory/DirectorySchema.cs`):

1. `directories:domains:<name>` — used when present.
2. `ldap:domains:<name>` — used otherwise; logs a deprecation warning **once**.
3. The top-level `ldap` section — only for the default domain; logs a
   deprecation warning once.

Consequences worth knowing:

- A deployment that never touches its config keeps working exactly as before,
  with warnings in the log.
- Both layouts can be mixed while migrating: domains already moved to
  `directories` win over a same-named legacy block.
- In a **purely** legacy config the default domain is always the top-level
  `ldap` section, even if `ldap:domains` happens to contain a child of the same
  name. That quirk is preserved deliberately; the new layout does not have it.

---

## 3. Secrets

Secrets never go in `appsettings.json`. They live in the encrypted SQLite store
and are overlaid onto the configuration tree **under their verbatim key path**,
so a secret's name is exactly the config path it fills:

```bash
adrapi-api-keys secret set directories:domains:cloud:entra:clientSecret       '<value>'
adrapi-api-keys secret set directories:domains:cloud:entra:certificatePassword '<value>'
adrapi-api-keys secret set directories:domains:corp:ldap:bindCredentials       '<value>'
```

Deprecated secret names, still read when the current one is absent, **removed in
2.0.0**:

| Deprecated secret name                            | Current secret name                                     |
| ------------------------------------------------- | -------------------------------------------------------- |
| `ldap:domains:<name>:entra:clientSecret`          | `directories:domains:<name>:entra:clientSecret`          |
| `ldap:domains:<name>:entra:certificatePassword`   | `directories:domains:<name>:entra:certificatePassword`   |
| `ldap:domains:<name>:entra:certificatePath`       | `directories:domains:<name>:entra:certificatePath`       |
| `ldap:domains:<name>:bindCredentials` / `bindDn`  | `directories:domains:<name>:ldap:bindCredentials` / `bindDn` |
| `ldap:bindCredentials` / `ldap:bindDn` (default)  | `directories:domains:<default>:ldap:bindCredentials` / `bindDn` |

A credential is only taken from a deprecated path when the current path leaves
it unset, and a legacy `clientSecret` is never paired with a certificate
declared in the new layout (that combination is rejected as ambiguous).

### Re-keying

```bash
# copy legacy secret names to the new ones (idempotent, keeps the old entries)
adrapi-api-keys secret migrate-directories --domain corp

# after adrapi has restarted cleanly, delete the legacy entries
adrapi-api-keys secret migrate-directories --domain corp --yes
```

`--domain <name>` also moves the top-level `ldap:bindDn` / `ldap:bindCredentials`
to that domain — the CLI cannot infer which domain the old default became.

---

## 4. Startup validation

`Startup.ValidateDirectoryConfiguration` enumerates every domain from both
layouts and validates it by `kind`:

- `ldap` — at least one `host:port` server, valid ports, and `ssl: true` is
  rejected against port 389.
- `entraid` — `tenantId` and `clientId` required; exactly one of `clientSecret`
  / `certificatePath` (counting secrets resolved from the store, including
  deprecated paths); `tenantId` may not be `common`/`organizations`/`consumers`.
  A non-fatal warning is logged when `grantedPermissions` don't cover the
  `Reading` baseline.
- anything else — hard error naming the unsupported `kind`.

Failures throw `InvalidOperationException` prefixed
`Invalid directory configuration:` and the service refuses to start.
