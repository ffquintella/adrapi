using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using adrapi.domain.Security;

namespace adrapi.Tools.ApiKeys
{
    /// <summary>
    /// adrapi-store — friendly CLI for the SQLite store used by adrapi.
    ///
    /// Two command groups:
    ///   key     — API key CRUD (Argon2id hashed)
    ///   secret  — encrypted application secrets (ChaCha20-Poly1305)
    ///
    /// Aliases preserved for backward compatibility: `add`, `list`, `rotate`,
    /// `remove`, `verify`, `import` behave as `key <subcommand>`.
    /// </summary>
    public static class Program
    {
        private const string DefaultDb = "cfg/api-keys.db";
        private const string DefaultSeed = "cfg/.seed";

        public static int Main(string[] args)
        {
            if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
            {
                PrintHelp();
                return args.Length == 0 ? 1 : 0;
            }

            try
            {
                // ---- key group ----
                if (args[0] == "key")
                {
                    if (args.Length < 2) { PrintHelp(); return 1; }
                    return RunKey(args[1], args.Skip(2).ToArray());
                }

                // ---- secret group ----
                if (args[0] == "secret")
                {
                    if (args.Length < 2) { PrintHelp(); return 1; }
                    return RunSecret(args[1], args.Skip(2).ToArray());
                }

                // ---- legacy flat aliases (key sub-commands) ----
                return args[0] switch
                {
                    "add" or "list" or "rotate" or "remove" or "verify" or "import"
                        => RunKey(args[0], args.Skip(1).ToArray()),
                    _ => Unknown(args[0]),
                };
            }
            catch (System.Security.Cryptography.CryptographicException)
            {
                Err("decryption failed — the seed file does not match the one used to encrypt these secrets, or you're on a different machine.");
                Err("If you rotated the seed or migrated hosts, re-run `secret set` to re-create them. The data is unrecoverable without the original seed + host.");
                return 3;
            }
            catch (Exception ex)
            {
                Err(ex.Message);
                return 2;
            }
        }

        // ===== KEY commands =====================================================

        private static int RunKey(string sub, string[] rest)
        {
            var o = ParseOptions(rest);
            var store = new ApiKeyStore(o.Db ?? DefaultDb);
            return sub switch
            {
                "add"    => KeyAdd(store, o),
                "list"   => KeyList(store),
                "rotate" => KeyRotate(store, o),
                "remove" => KeyRemove(store, o),
                "verify" => KeyVerify(store, o),
                "import" => KeyImport(store, o),
                _ => Unknown($"key {sub}"),
            };
        }

        private static int KeyAdd(ApiKeyStore store, Opts o)
        {
            o.KeyId ??= Prompt("key ID (short identifier, e.g. \"prod-admin\")");
            o.Ip ??= Prompt("authorized client IP (use 0.0.0.0 for any — discouraged)");
            // When the caller supplies --secret (e.g. config management like puppet
            // declaring the desired value), we want idempotent behaviour: if a key with
            // that ID already holds the supplied secret, exit 0 with no change; if it
            // holds a different secret, refuse and point to `rotate`.
            if (store.FindByKeyId(o.KeyId) != null)
            {
                if (o.Secret != null && store.VerifyAndLoad(o.KeyId, o.Secret) != null)
                {
                    Ok($"key '{o.KeyId}' already present with matching secret — no change.");
                    return 0;
                }
                Err($"key ID '{o.KeyId}' already exists. Use `key rotate` to replace the secret.");
                return 1;
            }
            var claims = o.Claims ?? PromptClaims();
            var callerSuppliedSecret = o.Secret != null;
            var secret = o.Secret ?? ApiKeyHasher.GenerateSecret();
            store.Insert(new ApiKey
            {
                keyID = o.KeyId,
                authorizedIP = o.Ip,
                claims = claims,
            }, plaintextSecret: secret);

            Ok($"key '{o.KeyId}' created.");
            if (!callerSuppliedSecret)
            {
                Console.WriteLine();
                Box("Record this secret now — it CANNOT be recovered from the store:",
                    $"api-key: {o.KeyId}:{secret}");
            }
            return 0;
        }

        private static int KeyList(ApiKeyStore store)
        {
            var keys = store.List();
            if (keys.Count == 0) { Console.WriteLine("(no keys)"); return 0; }
            foreach (var k in keys)
            {
                Console.WriteLine();
                Console.WriteLine($"  keyID         {k.keyID}");
                Console.WriteLine($"  authorizedIP  {k.authorizedIP}");
                Console.WriteLine($"  claims        {string.Join(", ", k.claims ?? new List<string>())}");
                Console.WriteLine($"  created       {Fmt(k.createdAt)}");
                Console.WriteLine($"  lastUsed      {Fmt(k.lastUsedAt) ?? "(never)"}");
            }
            return 0;
        }

        private static int KeyRotate(ApiKeyStore store, Opts o)
        {
            o.KeyId ??= Prompt("key ID to rotate");
            if (store.FindByKeyId(o.KeyId) == null) { Err($"key ID '{o.KeyId}' not found."); return 1; }
            // Idempotency for caller-supplied secret: if already matches, no rotation needed.
            if (o.Secret != null && store.VerifyAndLoad(o.KeyId, o.Secret) != null)
            {
                Ok($"key '{o.KeyId}' already holds the supplied secret — no rotation.");
                return 0;
            }
            if (!o.AssumeYes && !Confirm($"Rotate secret for '{o.KeyId}'? The previous secret will stop working immediately"))
            { Console.WriteLine("aborted."); return 1; }
            var callerSuppliedSecret = o.Secret != null;
            var secret = o.Secret ?? ApiKeyHasher.GenerateSecret();
            store.Rotate(o.KeyId, secret);
            Ok($"secret rotated for '{o.KeyId}'.");
            if (!callerSuppliedSecret)
            {
                Console.WriteLine();
                Box("New secret — record it now:",
                    $"api-key: {o.KeyId}:{secret}");
            }
            return 0;
        }

        private static int KeyRemove(ApiKeyStore store, Opts o)
        {
            o.KeyId ??= Prompt("key ID to remove");
            if (!o.AssumeYes && !Confirm($"Permanently delete key '{o.KeyId}'?"))
            { Console.WriteLine("aborted."); return 1; }
            Console.WriteLine(store.Remove(o.KeyId) ? $"removed '{o.KeyId}'." : "not found.");
            return 0;
        }

        private static int KeyVerify(ApiKeyStore store, Opts o)
        {
            o.KeyId ??= Prompt("key ID");
            o.Secret ??= PromptHidden("secret");
            var ok = store.VerifyAndLoad(o.KeyId, o.Secret) != null;
            Console.WriteLine(ok ? "OK — secret matches the stored hash." : "FAIL — no match.");
            return ok ? 0 : 1;
        }

        private static int KeyImport(ApiKeyStore store, Opts o)
        {
            o.From ??= Prompt("path to legacy security.json");
            var legacy = JsonConvert.DeserializeObject<List<ApiKey>>(File.ReadAllText(o.From));
            int imported = 0, skipped = 0;
            foreach (var k in legacy ?? new List<ApiKey>())
            {
                if (store.FindByKeyId(k.keyID) != null) { skipped++; continue; }
                store.Insert(new ApiKey
                {
                    keyID = k.keyID,
                    authorizedIP = k.authorizedIP,
                    claims = k.claims,
                }, plaintextSecret: k.secretKey);
                imported++;
            }
            Ok($"imported {imported} key(s), skipped {skipped} already present.");
            return 0;
        }

        // ===== SECRET commands ==================================================

        private static int RunSecret(string sub, string[] rest)
        {
            var o = ParseOptions(rest);
            var (keyProv, store, firstRun) = OpenSecretsStore(o);
            if (firstRun)
            {
                Console.WriteLine();
                Box("First-run setup",
                    $"Generated encryption seed at {Path.GetFullPath(keyProv.SeedPath)}.",
                    "The key is derived from THIS machine's identity + that seed.",
                    "Back the seed file up out-of-band — without it (or this host)",
                    "the encrypted secrets cannot be recovered.");
                Console.WriteLine();
            }
            return sub switch
            {
                "set"         => SecretSet(store, o),
                "get"         => SecretGet(store, o),
                "list"        => SecretList(store),
                "remove"      => SecretRemove(store, o),
                "import-ldap" => SecretImportLdap(store, o),
                _ => Unknown($"secret {sub}"),
            };
        }

        private static (MachineKeyProvider, AppSecretsStore, bool firstRun) OpenSecretsStore(Opts o)
        {
            var seedPath = o.Seed ?? DefaultSeed;
            var prov = new MachineKeyProvider(seedPath);
            var first = !prov.SeedExists();
            var key = prov.GetOrCreateKey();
            var store = new AppSecretsStore(o.Db ?? DefaultDb, key);
            return (prov, store, first);
        }

        private static int SecretSet(AppSecretsStore store, Opts o)
        {
            o.Name ??= Prompt("secret name (e.g. \"ldap:bindCredentials\")");
            o.Value ??= PromptHidden($"value for {o.Name}");
            store.Set(o.Name, o.Value);
            Ok($"stored encrypted secret '{o.Name}'.");
            return 0;
        }

        private static int SecretGet(AppSecretsStore store, Opts o)
        {
            o.Name ??= Prompt("secret name");
            var v = store.Get(o.Name);
            if (v == null) { Err("not found"); return 1; }
            Console.WriteLine(v);
            return 0;
        }

        private static int SecretList(AppSecretsStore store)
        {
            var items = store.List();
            if (items.Count == 0) { Console.WriteLine("(no secrets)"); return 0; }
            foreach (var s in items)
            {
                Console.WriteLine();
                Console.WriteLine($"  name       {s.Name}");
                Console.WriteLine($"  created    {Fmt(s.CreatedAt)}");
                Console.WriteLine($"  updated    {Fmt(s.UpdatedAt)}");
            }
            return 0;
        }

        private static int SecretRemove(AppSecretsStore store, Opts o)
        {
            o.Name ??= Prompt("secret name to remove");
            if (!o.AssumeYes && !Confirm($"Permanently delete secret '{o.Name}'?"))
            { Console.WriteLine("aborted."); return 1; }
            Console.WriteLine(store.Remove(o.Name) ? $"removed '{o.Name}'." : "not found.");
            return 0;
        }

        private static int SecretImportLdap(AppSecretsStore store, Opts o)
        {
            o.From ??= Prompt("path to JSON file (e.g. appsettings.Development.json or user-secrets.json)");
            var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(o.From));
            int imported = 0;

            void TryImport(string targetName, params string[] jsonPath)
            {
                if (!TryGetNested(json.RootElement, jsonPath, out var element)) return;
                var value = element.ValueKind == System.Text.Json.JsonValueKind.String ? element.GetString() : null;
                if (string.IsNullOrEmpty(value)) return;
                store.Set(targetName, value);
                Console.WriteLine($"  imported {targetName}");
                imported++;
            }

            TryImport("ldap:bindDn", "ldap", "bindDn");
            TryImport("ldap:bindCredentials", "ldap", "bindCredentials");
            TryImport("certificate:password", "certificate", "password");

            Ok($"imported {imported} secret(s).");
            return 0;
        }

        private static bool TryGetNested(System.Text.Json.JsonElement root, string[] path, out System.Text.Json.JsonElement found)
        {
            var current = root;
            foreach (var p in path)
            {
                if (current.ValueKind != System.Text.Json.JsonValueKind.Object
                    || !current.TryGetProperty(p, out current))
                {
                    found = default;
                    return false;
                }
            }
            found = current;
            return true;
        }

        // ===== UI helpers =======================================================

        private static int Unknown(string cmd)
        {
            Err($"unknown command: {cmd}");
            PrintHelp();
            return 1;
        }

        private static string Prompt(string label)
        {
            Console.Write($"  {label}: ");
            var v = Console.ReadLine();
            return string.IsNullOrWhiteSpace(v) ? throw new ArgumentException($"{label} cannot be empty") : v.Trim();
        }

        private static string PromptHidden(string label)
        {
            Console.Write($"  {label} (input hidden): ");
            var sb = new StringBuilder();
            while (true)
            {
                var k = Console.ReadKey(intercept: true);
                if (k.Key == ConsoleKey.Enter) break;
                if (k.Key == ConsoleKey.Backspace && sb.Length > 0) { sb.Length--; continue; }
                if (!char.IsControl(k.KeyChar)) sb.Append(k.KeyChar);
            }
            Console.WriteLine();
            if (sb.Length == 0) throw new ArgumentException($"{label} cannot be empty");
            return sb.ToString();
        }

        private static bool Confirm(string question)
        {
            Console.Write($"  {question} [y/N] ");
            var v = Console.ReadLine();
            return !string.IsNullOrEmpty(v) && (v.Trim().Equals("y", StringComparison.OrdinalIgnoreCase) || v.Trim().Equals("yes", StringComparison.OrdinalIgnoreCase));
        }

        private static List<string> PromptClaims()
        {
            Console.WriteLine("  claims (one per line, blank to finish):");
            Console.WriteLine("    1) isAdministrator   (read/write)");
            Console.WriteLine("    2) isMonitor         (read-only)");
            var list = new List<string>();
            while (true)
            {
                Console.Write("    > ");
                var v = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(v)) break;
                v = v.Trim();
                if (v == "1") v = "isAdministrator";
                else if (v == "2") v = "isMonitor";
                list.Add(v);
            }
            return list;
        }

        private static void Ok(string msg)
        {
            var prev = Console.ForegroundColor;
            try { Console.ForegroundColor = ConsoleColor.Green; Console.WriteLine($"✓ {msg}"); }
            finally { Console.ForegroundColor = prev; }
        }

        private static void Err(string msg)
        {
            var prev = Console.ForegroundColor;
            try { Console.ForegroundColor = ConsoleColor.Red; Console.Error.WriteLine($"✗ {msg}"); }
            finally { Console.ForegroundColor = prev; }
        }

        private static void Box(params string[] lines)
        {
            var width = lines.Max(l => l.Length) + 4;
            var bar = new string('─', width);
            Console.WriteLine($"┌{bar}┐");
            foreach (var l in lines)
                Console.WriteLine($"│  {l}{new string(' ', width - l.Length - 2)}│");
            Console.WriteLine($"└{bar}┘");
        }

        private static string Fmt(DateTime? d) => d?.ToString("u") ?? "(unknown)";

        private static void PrintHelp()
        {
            Console.WriteLine(@"
adrapi-api-keys — manage the SQLite store used by the adrapi service.

USAGE
  adrapi-api-keys <group> <command> [options]
  adrapi-api-keys help

GROUPS
  key      manage API keys (Argon2id-hashed)
  secret   manage encrypted application secrets (ChaCha20-Poly1305)

KEY COMMANDS
  key add     [--keyID <id>] [--ip <ip>] [--claims a,b] [--secret <s>]
              create a new key. If --secret is supplied, that value is stored
              (idempotent: same secret on an existing key exits 0); otherwise
              a secret is generated and printed once.
  key list    list all keys (metadata only — never the secret)
  key rotate  --keyID <id> [--secret <s>] [--yes]
              issue a new secret; the previous one stops working immediately.
              If --secret is supplied, rotate to that value (no-op when it
              already matches); otherwise a new secret is generated.
  key remove  --keyID <id> [--yes]
              delete a key
  key verify  [--keyID <id>] [--secret <s>]
              test a secret against the stored hash
  key import  --from <security.json>
              import a legacy plaintext keys file (hashes them)

SECRET COMMANDS
  secret set     [--name k] [--value v]
                 store a secret; value is encrypted with a key derived from
                 the machine ID + a seed file (created on first run)
  secret get     [--name k]
                 decrypt and print a secret
  secret list    list secret names (values stay encrypted)
  secret remove  [--name k] [--yes]
  secret import-ldap --from <appsettings-or-secrets.json>
                 one-shot: pulls ldap:bindDn, ldap:bindCredentials, and
                 certificate:password out of a JSON file into the store

COMMON OPTIONS
  --db <path>     SQLite database (default: cfg/api-keys.db)
  --seed <path>   encryption seed file (default: cfg/.seed)
  -h, --help      this help

NOTES
  • The seed file is created on the first `secret` command. Without it AND
    this host, the encrypted secrets cannot be recovered.
  • API key secrets are NEVER printed except on `add` and `rotate`. Once you
    close the terminal, the only recovery is to rotate.
");
        }

        private static Opts ParseOptions(string[] args)
        {
            var o = new Opts();
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--keyID":  o.KeyId = args[++i]; break;
                    case "--ip":     o.Ip = args[++i]; break;
                    case "--claims": o.Claims = args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries).ToList(); break;
                    case "--secret": o.Secret = args[++i]; break;
                    case "--name":   o.Name = args[++i]; break;
                    case "--value":  o.Value = args[++i]; break;
                    case "--db":     o.Db = args[++i]; break;
                    case "--seed":   o.Seed = args[++i]; break;
                    case "--from":   o.From = args[++i]; break;
                    case "--yes":
                    case "-y":       o.AssumeYes = true; break;
                    default: throw new ArgumentException($"unknown option: {args[i]}");
                }
            }
            return o;
        }

        private class Opts
        {
            public string KeyId;
            public string Ip;
            public List<string> Claims;
            public string Secret;
            public string Name;
            public string Value;
            public string Db;
            public string Seed;
            public string From;
            public bool AssumeYes;
        }
    }
}
