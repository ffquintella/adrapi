using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using adrapi.domain.Security;

namespace adrapi.Tools.ApiKeys
{
    /// <summary>
    /// CLI for managing the SQLite-backed API key store used by adrapi.
    ///
    /// Usage:
    ///   adrapi-api-keys add --keyID id --ip 10.0.0.5 [--claims isAdministrator,isMonitor] [--db path]
    ///   adrapi-api-keys list [--db path]
    ///   adrapi-api-keys rotate --keyID id [--db path]
    ///   adrapi-api-keys remove --keyID id [--db path]
    ///   adrapi-api-keys verify --keyID id --secret s [--db path]
    ///   adrapi-api-keys import --from path/to/security.json [--db path]
    /// </summary>
    public static class Program
    {
        private const string DefaultDb = "cfg/api-keys.db";

        public static int Main(string[] args)
        {
            if (args.Length == 0 || args[0] is "-h" or "--help")
            {
                PrintHelp();
                return args.Length == 0 ? 1 : 0;
            }

            try
            {
                var cmd = args[0];
                var opts = ParseOptions(args.Skip(1).ToArray());
                var store = new ApiKeyStore(opts.Db ?? DefaultDb);
                Console.WriteLine($"db: {Path.GetFullPath(store.DatabasePath)}");

                return cmd switch
                {
                    "add"    => Add(store, opts),
                    "list"   => List(store),
                    "rotate" => Rotate(store, opts),
                    "remove" => Remove(store, opts),
                    "verify" => Verify(store, opts),
                    "import" => Import(store, opts),
                    _ => Unknown(cmd),
                };
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"error: {ex.Message}");
                return 2;
            }
        }

        private static int Add(ApiKeyStore store, Opts o)
        {
            Require(o.KeyId, "--keyID");
            Require(o.Ip, "--ip");
            if (store.FindByKeyId(o.KeyId) != null)
            {
                Console.Error.WriteLine($"keyID '{o.KeyId}' already exists. Use rotate to replace the secret.");
                return 1;
            }
            var secret = ApiKeyHasher.GenerateSecret();
            store.Insert(new ApiKey
            {
                keyID = o.KeyId,
                authorizedIP = o.Ip,
                claims = o.Claims ?? new List<string>(),
            }, plaintextSecret: secret);

            Console.WriteLine();
            Console.WriteLine("Key created. Record this secret now — it cannot be recovered from the store:");
            Console.WriteLine($"  api-key: {o.KeyId}:{secret}");
            return 0;
        }

        private static int List(ApiKeyStore store)
        {
            var keys = store.List();
            if (keys.Count == 0) { Console.WriteLine("(no keys)"); return 0; }
            foreach (var k in keys)
            {
                Console.WriteLine();
                Console.WriteLine($"keyID:        {k.keyID}");
                Console.WriteLine($"authorizedIP: {k.authorizedIP}");
                Console.WriteLine($"claims:       {string.Join(", ", k.claims ?? new List<string>())}");
                Console.WriteLine($"createdAt:    {k.createdAt:u}");
                Console.WriteLine($"lastUsedAt:   {(k.lastUsedAt.HasValue ? k.lastUsedAt.Value.ToString("u") : "(never)")}");
            }
            return 0;
        }

        private static int Rotate(ApiKeyStore store, Opts o)
        {
            Require(o.KeyId, "--keyID");
            if (store.FindByKeyId(o.KeyId) == null)
            {
                Console.Error.WriteLine($"keyID '{o.KeyId}' not found.");
                return 1;
            }
            var secret = ApiKeyHasher.GenerateSecret();
            store.Rotate(o.KeyId, secret);
            Console.WriteLine();
            Console.WriteLine("Secret rotated. Record this value — the previous one is invalidated:");
            Console.WriteLine($"  api-key: {o.KeyId}:{secret}");
            return 0;
        }

        private static int Remove(ApiKeyStore store, Opts o)
        {
            Require(o.KeyId, "--keyID");
            Console.WriteLine(store.Remove(o.KeyId) ? "Removed." : "Not found.");
            return 0;
        }

        private static int Verify(ApiKeyStore store, Opts o)
        {
            Require(o.KeyId, "--keyID");
            Require(o.Secret, "--secret");
            var ok = store.VerifyAndLoad(o.KeyId, o.Secret) != null;
            Console.WriteLine(ok ? "OK" : "FAIL");
            return ok ? 0 : 1;
        }

        private static int Import(ApiKeyStore store, Opts o)
        {
            Require(o.From, "--from");
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
            Console.WriteLine($"Imported {imported}, skipped {skipped} (already present).");
            return 0;
        }

        private static int Unknown(string cmd)
        {
            Console.Error.WriteLine($"unknown command: {cmd}");
            PrintHelp();
            return 1;
        }

        private static void Require(string val, string name)
        {
            if (string.IsNullOrWhiteSpace(val))
                throw new ArgumentException($"{name} is required");
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
                    case "--db":     o.Db = args[++i]; break;
                    case "--from":   o.From = args[++i]; break;
                    default: throw new ArgumentException($"unknown option: {args[i]}");
                }
            }
            return o;
        }

        private static void PrintHelp()
        {
            Console.WriteLine(@"adrapi-api-keys — manage the SQLite API key store.

Commands:
  add     --keyID <id> --ip <ip> [--claims a,b]   create a new key (prints the secret once)
  list                                            list all keys
  rotate  --keyID <id>                            generate a new secret for an existing key
  remove  --keyID <id>                            delete a key
  verify  --keyID <id> --secret <s>               test a secret against the stored hash
  import  --from <path-to-security.json>          import legacy plaintext keys (hashes them)

Common options:
  --db <path>    SQLite database path (default: cfg/api-keys.db)
  -h, --help     Show this help");
        }

        private class Opts
        {
            public string KeyId;
            public string Ip;
            public List<string> Claims;
            public string Secret;
            public string Db;
            public string From;
        }
    }
}
