# CLI Tools

ADRAPI ships a companion command-line tool, **`adrapi-api-keys`**, for managing
the SQLite store that backs authentication and application secrets. It lives at
[`tools/AdrapiApiKeys/`](https://github.com/ffquintella/adrapi/tree/develop/tools/AdrapiApiKeys)
and is built as a standalone
executable (`AssemblyName` = `adrapi-api-keys`).

The store has two command groups:

- **`key`** — API key CRUD, with Argon2id-hashed secrets.
- **`secret`** — encrypted application secrets (ChaCha20-Poly1305), e.g. the
  LDAP bind credentials and certificate password.

## Running the tool

From the repository root:

```bash
dotnet run --project tools/AdrapiApiKeys -- <group> <command> [options]
```

The `--` separates `dotnet run`'s own arguments from the arguments passed to the
tool. To print the built-in help:

```bash
dotnet run --project tools/AdrapiApiKeys -- help
```

> The store defaults to `cfg/api-keys.db`; the encryption seed defaults to
> `cfg/.seed`. Override either with `--db <path>` / `--seed <path>`.

## Managing API keys (`key`)

```bash
# Create a key — a secret is generated and printed ONCE.
dotnet run --project tools/AdrapiApiKeys -- key add \
  --keyID prod-admin --ip 10.0.0.5 --claims isAdministrator

# Create a key supplying your own secret (idempotent — handy for
# config management like puppet declaring the desired value).
dotnet run --project tools/AdrapiApiKeys -- key add \
  --keyID prod-monitor --ip 10.0.0.6 --claims isMonitor --secret "my-secret"

# List keys (metadata only — never the secret).
dotnet run --project tools/AdrapiApiKeys -- key list

# Rotate a secret — the previous one stops working immediately.
dotnet run --project tools/AdrapiApiKeys -- key rotate --keyID prod-admin --yes

# Verify a secret against the stored hash.
dotnet run --project tools/AdrapiApiKeys -- key verify --keyID prod-admin --secret "my-secret"

# Remove a key.
dotnet run --project tools/AdrapiApiKeys -- key remove --keyID prod-admin --yes

# Import legacy plaintext keys from a security.json (they get hashed on import).
dotnet run --project tools/AdrapiApiKeys -- key import --from security.json
```

If you omit `--keyID`, `--ip`, `--claims`, etc., the tool prompts for them
interactively — including a menu for claims (`1` = `isAdministrator`,
`2` = `isMonitor`).

### Key command reference

| Command | Options | Purpose |
| ------- | ------- | ------- |
| `key add` | `[--keyID <id>] [--ip <ip>] [--claims a,b] [--secret <s>]` | Create a key. With `--secret`, that value is stored (idempotent on an existing key); otherwise a secret is generated and printed once. |
| `key list` | — | List all keys (metadata only). |
| `key rotate` | `--keyID <id> [--secret <s>] [--yes]` | Issue a new secret; the previous one stops working immediately. No-op if `--secret` already matches. |
| `key remove` | `--keyID <id> [--yes]` | Delete a key. |
| `key verify` | `[--keyID <id>] [--secret <s>]` | Test a secret against the stored hash. |
| `key import` | `--from <security.json>` | Import a legacy plaintext keys file. |

## Managing application secrets (`secret`)

Application secrets are encrypted with a key derived from **this machine's
identity + a seed file** (`cfg/.seed`, created on first run). Back the seed up
out-of-band — without it *and* this host, the encrypted secrets cannot be
recovered.

```bash
# Store a secret (value is encrypted at rest).
dotnet run --project tools/AdrapiApiKeys -- secret set --name "ldap:bindCredentials" --value "s3cr3t"

# Decrypt and print a secret.
dotnet run --project tools/AdrapiApiKeys -- secret get --name "ldap:bindCredentials"

# List secret names (values stay encrypted).
dotnet run --project tools/AdrapiApiKeys -- secret list

# Remove a secret.
dotnet run --project tools/AdrapiApiKeys -- secret remove --name "ldap:bindCredentials" --yes

# One-shot import of ldap:bindDn, ldap:bindCredentials and certificate:password
# out of a JSON file (e.g. appsettings.Development.json or user-secrets.json).
dotnet run --project tools/AdrapiApiKeys -- secret import-ldap --from appsettings.Development.json
```

### Secret command reference

| Command | Options | Purpose |
| ------- | ------- | ------- |
| `secret set` | `[--name k] [--value v]` | Store an encrypted secret. |
| `secret get` | `[--name k]` | Decrypt and print a secret. |
| `secret list` | — | List secret names (values stay encrypted). |
| `secret remove` | `[--name k] [--yes]` | Delete a secret. |
| `secret import-ldap` | `--from <json>` | Pull `ldap:bindDn`, `ldap:bindCredentials`, `certificate:password` from a JSON file. |

## Common options

| Option | Default | Meaning |
| ------ | ------- | ------- |
| `--db <path>` | `cfg/api-keys.db` | SQLite database location. |
| `--seed <path>` | `cfg/.seed` | Encryption seed file. |
| `-y`, `--yes` | — | Skip confirmation prompts. |
| `-h`, `--help` | — | Print help. |

## Notes & gotchas

- **Claims drive authorization** (see [AGENTS.md](AGENTS.md) / the auth
  contract): `isAdministrator` → read + write (`Writting` policy);
  `isMonitor` → read-only (`Reading` policy).
- The authentication header sent to the API is `api-key: <keyID>:<secret>`.
- **API key secrets are only displayed on `add` and `rotate`.** Once the
  terminal is closed, the only recovery is to rotate.
- Legacy flat aliases (`add`, `list`, `rotate`, `remove`, `verify`, `import`)
  still work and behave as their `key <subcommand>` equivalents.
- If decryption fails, the seed file no longer matches the one used to encrypt
  (rotated seed or different host). Re-run `secret set` to recreate the values.

## Installing as a global tool (optional)

To call `adrapi-api-keys` directly instead of `dotnet run --project ...` each
time, publish the executable and put it on your `PATH`:

```bash
dotnet publish tools/AdrapiApiKeys -c Release -o ./artifacts/api-keys
# then add ./artifacts/api-keys to PATH, or copy the binary somewhere on it
./artifacts/api-keys/adrapi-api-keys key list
```
