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

## Building the standalone client

The recommended way to use the tool is to build it once as a **self-contained,
single-file executable** — no `dotnet` runtime needed to run it afterwards.
From the repository root:

```bash
./build-api-keys.sh            # macOS/Linux — auto-detects your platform
build-api-keys.bat             # Windows (defaults to win-x64)
```

Pass a [.NET Runtime Identifier](https://learn.microsoft.com/dotnet/core/rid-catalog)
to cross-build for another platform, e.g. `./build-api-keys.sh linux-x64`.

The binary lands in `artifacts/api-keys/<rid>/` and runs directly:

```bash
./artifacts/api-keys/osx-arm64/adrapi-api-keys help
```

Copy that executable anywhere on your `PATH` (e.g. `/usr/local/bin`) to call it
as plain `adrapi-api-keys` from any directory.

> The store defaults to `cfg/api-keys.db`; the encryption seed defaults to
> `cfg/.seed`. Override either with `--db <path>` / `--seed <path>`. Run the
> tool from the repository root (or pass absolute `--db`/`--seed` paths) so it
> finds the `cfg/` files.

### Running from source (development)

If you have the SDK and just want to run it without building a binary:

```bash
dotnet run --project tools/AdrapiApiKeys -- <group> <command> [options]
```

The `--` separates `dotnet run`'s own arguments from the tool's arguments. The
command examples below show the `adrapi-api-keys` executable form; prefix them
with `dotnet run --project tools/AdrapiApiKeys -- ` to run from source.

## Managing API keys (`key`)

```bash
# Create a key — a secret is generated and printed ONCE.
adrapi-api-keys key add \
  --keyID prod-admin --ip 10.0.0.5 --claims isAdministrator

# Create a key supplying your own secret (idempotent — handy for
# config management like puppet declaring the desired value).
adrapi-api-keys key add \
  --keyID prod-monitor --ip 10.0.0.6 --claims isMonitor --secret "my-secret"

# List keys (metadata only — never the secret).
adrapi-api-keys key list

# Rotate a secret — the previous one stops working immediately.
adrapi-api-keys key rotate --keyID prod-admin --yes

# Verify a secret against the stored hash.
adrapi-api-keys key verify --keyID prod-admin --secret "my-secret"

# Remove a key.
adrapi-api-keys key remove --keyID prod-admin --yes

# Import legacy plaintext keys from a security.json (they get hashed on import).
adrapi-api-keys key import --from security.json
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
adrapi-api-keys secret set --name "ldap:bindCredentials" --value "s3cr3t"

# Decrypt and print a secret.
adrapi-api-keys secret get --name "ldap:bindCredentials"

# List secret names (values stay encrypted).
adrapi-api-keys secret list

# Remove a secret.
adrapi-api-keys secret remove --name "ldap:bindCredentials" --yes

# One-shot import of ldap:bindDn, ldap:bindCredentials and certificate:password
# out of a JSON file (e.g. appsettings.Development.json or user-secrets.json).
adrapi-api-keys secret import-ldap --from appsettings.Development.json
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

## Installing on your PATH (optional)

After [building the standalone client](#building-the-standalone-client), copy the
single-file executable somewhere on your `PATH` so you can call it from anywhere:

```bash
./build-api-keys.sh
sudo cp artifacts/api-keys/osx-arm64/adrapi-api-keys /usr/local/bin/
adrapi-api-keys key list
```

The published binary is fully self-contained — the target machine does **not**
need the .NET SDK or runtime installed.
