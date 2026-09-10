# Microsoft Entra ID — Step-by-Step Setup

This is the hands-on companion to the [Entra ID Guide](ENTRA_ID_GUIDE.md). It walks
through everything you do **on the Microsoft side** (app registration, permissions,
admin consent, credentials) and then **in adrapi** (configuration + secret store),
followed by verification.

ADRAPI talks to Microsoft Graph as a **daemon / service** using the OAuth2
**client-credentials** flow. That means:

- **Application permissions** (app roles), *not* delegated permissions.
- **Admin consent is mandatory** — nothing works until a Global Administrator (or
  Privileged Role Admin) consents.
- A **tenant-specific** authority — there is no "common" endpoint for app-only.

---

## 0. Prerequisites

- An Entra ID tenant and an account with **Global Administrator** (or
  Application Administrator + the ability to grant admin consent).
- Your **Tenant ID** (Directory ID) — Entra admin center → *Overview*.
- One of: the [Entra admin center](https://entra.microsoft.com), the **Azure CLI**
  (`az`), or the **Microsoft Graph PowerShell** SDK.

Pick **one** of sections 1–4 (portal *or* CLI *or* PowerShell). Then do section 5
(adrapi) and section 6 (verify).

---

## 1. Register the application (portal)

1. [entra.microsoft.com](https://entra.microsoft.com) → **Identity → Applications →
   App registrations → New registration**.
2. **Name:** `adrapi-graph` (anything).
3. **Supported account types:**
   - *Accounts in this organizational directory only* → **single tenant** (typical).
   - *Accounts in any organizational directory* → **multi-tenant** (see
     [Guide §4](ENTRA_ID_GUIDE.md#4-single-tenant-vs-multi-tenant); you then
     configure one adrapi domain per customer tenant).
4. **Redirect URI:** leave empty (no user sign-in).
5. **Register.** On the **Overview** page copy the **Application (client) ID** and
   **Directory (tenant) ID**.

## 2. Add Graph application permissions + grant consent (portal)

1. In the app → **API permissions → Add a permission → Microsoft Graph →
   Application permissions**.
2. Add the permissions for the access level you need (see
   [§7 reference table](#7-permission-reference)):
   - **Read-only deployment:** `User.Read.All`, `Group.Read.All`, `GroupMember.Read.All`
   - **Full lifecycle:** `User.ReadWrite.All`, `Group.ReadWrite.All`, `GroupMember.ReadWrite.All`
3. Click **Grant admin consent for &lt;tenant&gt;** and confirm. Each row should show
   a green **Granted** state. **This step is required.**
4. Remove the default delegated `User.Read` if you like — adrapi does not use it.

## 3. Create a credential (portal)

Choose a **client secret** (simplest) or a **certificate** (recommended for prod).

**Client secret:** app → **Certificates & secrets → Client secrets → New client
secret** → set an expiry → **Add** → copy the **Value** immediately (shown once).

**Certificate:** app → **Certificates & secrets → Certificates → Upload
certificate** → upload the **public** cert (`.cer`/`.crt`/`.pem`). adrapi loads the
matching PKCS#12 (`.p12`/`.pfx`). To generate a self-signed pair:

```bash
openssl req -x509 -newkey rsa:2048 -nodes -days 365 \
  -keyout adrapi-graph.key -out adrapi-graph.crt -subj "/CN=adrapi-graph"
# bundle the private key + cert into a .p12 for adrapi:
openssl pkcs12 -export -out adrapi-graph.p12 -inkey adrapi-graph.key -in adrapi-graph.crt
# upload adrapi-graph.crt to the app registration; deploy adrapi-graph.p12 to adrapi.
```

→ skip to [§5](#5-configure-adrapi).

## 4. Same thing via CLI / PowerShell

### Azure CLI

```bash
GRAPH=00000003-0000-0000-c000-000000000000   # Microsoft Graph's well-known appId

# Register the app (single tenant) and a service principal for it.
az ad app create --display-name "adrapi-graph" --sign-in-audience AzureADMyOrg
APP_ID=$(az ad app list --display-name "adrapi-graph" --query "[0].appId" -o tsv)
az ad sp create --id "$APP_ID"

# Add APPLICATION permissions (=Role). Swap in Read.All ids for a read-only app.
az ad app permission add --id "$APP_ID" --api $GRAPH --api-permissions \
  741f803b-c850-494e-b5df-cde7c675a1ca=Role \
  62a82d76-70ea-41e2-9197-370581804d09=Role \
  dbaae8cf-10b5-4b86-a4a1-f871c94c6695=Role
# (User.ReadWrite.All, Group.ReadWrite.All, GroupMember.ReadWrite.All)

# Grant admin consent (requires a privileged admin).
az ad app permission admin-consent --id "$APP_ID"

# Credential — secret:
az ad app credential reset --id "$APP_ID" --display-name adrapi --years 1
#   -> prints "password" (the client secret) and the appId/tenant.
# Credential — certificate (alternative): create or upload one:
az ad app credential reset --id "$APP_ID" --create-cert
```

GUIDs are the well-known Graph app-role ids (stable across tenants) — see
[§7](#7-permission-reference).

### Microsoft Graph PowerShell (outline)

```powershell
Connect-MgGraph -Scopes "Application.ReadWrite.All","AppRoleAssignment.ReadWrite.All"
$app = New-MgApplication -DisplayName "adrapi-graph" -SignInAudience AzureADMyOrg
$sp  = New-MgServicePrincipal -AppId $app.AppId
# Assign each app role to $sp via New-MgServicePrincipalAppRoleAssignment against the
# Microsoft Graph service principal, then grant admin consent in the portal, and
# add a secret/cert with Add-MgApplicationPassword / Add-MgApplicationKey.
```

For most teams the **portal** (§1–3) or **az CLI** (§4) is the fastest path.

## 5. Configure adrapi

Add an Entra-backed **domain** under `directories:domains:<name>` with
`kind: entraid`, alongside your on-prem LDAP domain; the new domain is selected
via the `{domain}` route segment. (The deprecated `ldap:domains:<name>` location
is still read through the 1.x line — see
[Directory configuration reference](DIRECTORIES_CONFIG.md).)

`adrapi/appsettings.json`:

```jsonc
"directories": {
  "defaultDomain": "corp",
  "domains": {
    "corp": {
      "kind": "ldap",
      "ldap": {
        "servers": [ "dc-corp:636" ], "ssl": true, "poolSize": 10,
        "bindDn": "...", "searchBase": "DC=corp,DC=example", "maxResults": 999
      }
    },
    "cloud": {
      "kind": "entraid",
      "entra": {
        "tenantId": "<Directory (tenant) ID>",
        "clientId": "<Application (client) ID>",
        // clientSecret is NOT put here — it comes from the encrypted secret store.
        "grantedPermissions": [
          "User.ReadWrite.All", "Group.ReadWrite.All", "GroupMember.ReadWrite.All"
        ]
        // certificate alternative to a secret:
        // "certificatePath": "/run/secrets/adrapi-graph.p12"
        // (certificatePassword also goes in the secret store)
      }
    }
  }
}
```

Store the secret (or cert password) in the encrypted SQLite store — the secret
**name is the verbatim config path**:

```bash
# client secret:
adrapi-api-keys secret set directories:domains:cloud:entra:clientSecret '<the secret value>'

# OR, if using a certificate:
adrapi-api-keys secret set directories:domains:cloud:entra:certificatePassword '<p12 password>'
```

`grantedPermissions` should list exactly what you consented to in §2/§4 — adrapi
uses it to **warn at startup** if the read baseline isn't covered, catching a
missing admin-consent early.

## 6. Verify

1. **Start adrapi.** Startup validates each Entra domain (tenant/client/credential)
   and logs a warning if `grantedPermissions` miss the `Reading` baseline. A
   misconfigured domain fails fast with a clear message.
2. **Smoke test** a read against the new domain (replace host/key):

   ```bash
   curl -k "https://localhost:6001/api/cloud/users/someone@contoso.com/exists" \
     -H "api-version: 2.0" -H "api-key: <keyId>:<secret>"
   ```

   - `200 OK` → the user exists. `404` → not found (auth worked).
   - `502` "upstream authentication failed" → bad/expired secret or wrong tenant.
   - `502` + a startup permission warning → admin consent not granted.

   More examples: [Curl Collection → Entra endpoints](CURL_COLLECTION.md#microsoft-entra-id-backed-endpoints).
3. Graph operations are audited under the `GraphAudit` logger with the Graph
   `request-id`, so you can cross-correlate with Microsoft support.

## 7. Permission reference

adrapi maps its authorization policies onto these **Microsoft Graph application
permissions**. A `*.ReadWrite.All` grant implicitly satisfies the matching
`*.Read.All`.

| adrapi policy | Graph application permission | App-role ID (well-known) |
|---|---|---|
| `Reading` | `User.Read.All` | `df021288-bdef-4463-88db-98f22de89214` |
| `Reading` | `Group.Read.All` | `5b567255-7703-4780-807c-7be8301ae99b` |
| `Reading` | `GroupMember.Read.All` | `98830695-27a2-44f7-8c18-0c3ebc9698f6` |
| `Writting` | `User.ReadWrite.All` | `741f803b-c850-494e-b5df-cde7c675a1ca` |
| `Writting` | `Group.ReadWrite.All` | `62a82d76-70ea-41e2-9197-370581804d09` |
| `Writting` | `GroupMember.ReadWrite.All` | `dbaae8cf-10b5-4b86-a4a1-f871c94c6695` |

Microsoft Graph resource appId (for `az ad app permission add --api`):
`00000003-0000-0000-c000-000000000000`.

**Least privilege:** grant only what the deployment uses — a read-only adrapi needs
only the three `*.Read.All` roles. Avoid `Directory.ReadWrite.All`.

**Password operations** (`SetUserPassword`): resetting another user's password via
app-only is **privileged** and needs more than `User.ReadWrite.All` (e.g.
`User-PasswordProfile.ReadWrite.All` and/or assigning the app a privileged
directory role). Only enable it if you use that feature, and consult the current
[Microsoft Graph permissions reference](https://learn.microsoft.com/graph/permissions-reference)
for the exact requirement in your tenant.

## 8. Common issues

| Symptom | Cause | Fix |
|---|---|---|
| Startup warning "missing granted Graph app roles" | consent not granted / `grantedPermissions` incomplete | grant admin consent (§2/§4); update `grantedPermissions` |
| `502` upstream auth on every call | wrong/expired secret, wrong `tenantId`/`clientId` | rotate secret, re-check IDs |
| `403` from Graph (mapped to `502`) | permission not consented, or delegated instead of application | ensure **Application** permissions + admin consent |
| Startup error: tenant `common`/`organizations` | app-only needs a specific tenant | set `tenantId` to the tenant GUID / verified domain |
| `400` "distinguished name … objectId or userPrincipalName" | sent an LDAP DN to the Entra domain | use objectId or UPN |
| `400` "Organizational unit operations are not supported" | OU call on an Entra domain | OUs are LDAP/AD-only |

---

See also: [Entra ID Guide](ENTRA_ID_GUIDE.md) · [Security checklist](ENTRA_STAGE7_SECURITY.md) ·
[Curl Collection](CURL_COLLECTION.md#microsoft-entra-id-backed-endpoints).
