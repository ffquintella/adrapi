using System;
using System.IO;
using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NLog;
using NLog.Web;

namespace adrapi
{
    /// <summary>
    /// Application entry point and web host bootstrapper.
    /// </summary>
    public class Program
    {
        public static void Main(string[] args)
        {
            // NLog: set up the logger first to catch all errors.
            var logger = LogManager.Setup().LoadConfigurationFromAppSettings().GetCurrentClassLogger();
            try
            {
                logger.Debug("init main");
                CreateHostBuilder(args).Build().Run();
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Stopped program because of exception");
                throw;
            }
            finally
            {
                NLog.LogManager.Shutdown();
            }
        }

        /// <summary>
        /// Builds the generic host. Single signature so <c>WebApplicationFactory&lt;Program&gt;</c>
        /// in the test suite can discover and call it via reflection.
        ///
        /// Configuration sources, in precedence order (later wins):
        ///   1. appsettings.json
        ///   2. appsettings.{Environment}.json
        ///   3. User Secrets (Development only)
        ///   4. SQLite-backed encrypted secrets (cfg/api-keys.db + cfg/.seed)
        ///   5. Environment variables
        ///   6. Command-line args
        /// </summary>
        public static IHostBuilder CreateHostBuilder(string[] args)
        {
            return Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration((ctx, builder) =>
                {
                    // CreateDefaultBuilder already chained JSON + user-secrets + env vars +
                    // command line into `builder`. Layer our encrypted SQLite secrets in
                    // before env vars/cli override (env vars have the last word so an
                    // operator can still inject an override at run time).
                    var snapshot = builder.Build();
                    var dbPath = snapshot.GetSection("security").GetValue<string>("databaseFile")
                        ?? "cfg/api-keys.db";
                    var seedPath = snapshot.GetSection("security").GetValue<string>("seedFile")
                        ?? "cfg/.seed";
                    builder.AddSqliteSecrets(dbPath, seedPath, optional: true);
                })
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseKestrel((ctx, options) =>
                    {
                        var cfg = ctx.Configuration;
                        var allowedHosts = cfg["AllowedHosts"] ?? "127.0.0.1";
                        if (allowedHosts == "*") allowedHosts = "0.0.0.0";
                        var certificateFile = cfg["certificate:file"] ?? "adrapi-dev.p12";
                        var certificatePassword = cfg["certificate:password"] ?? "adrapi-dev";

                        options.Listen(IPAddress.Parse(allowedHosts), 6000);
                        options.Listen(IPAddress.Parse(allowedHosts), 6001, listenOptions =>
                        {
                            try
                            {
                                listenOptions.UseHttps(certificateFile, certificatePassword);
                            }
                            catch (Exception ex)
                            {
                                // A broken certificate must NOT silently downgrade the HTTPS
                                // listener on 6001 to cleartext. In Development (and the test
                                // host, which runs as Development) the dev cert may be absent,
                                // so we log and carry on. Anywhere else we fail fast - refusing
                                // to start beats serving plaintext on a port operators trust as
                                // TLS.
                                var logger = LogManager.GetCurrentClassLogger();
                                if (ctx.HostingEnvironment.IsDevelopment())
                                {
                                    logger.Warn(ex,
                                        "HTTPS certificate '{0}' could not be loaded; port 6001 will not serve TLS (Development).",
                                        certificateFile);
                                }
                                else
                                {
                                    logger.Fatal(ex,
                                        "HTTPS certificate '{0}' could not be loaded for the listener on port 6001. " +
                                        "Refusing to start rather than expose an unsecured HTTPS port.",
                                        certificateFile);
                                    throw;
                                }
                            }
                        });
                    });
                    webBuilder.UseStartup<Startup>();
                })
                .ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                    logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Trace);
                })
                .UseNLog();
        }
    }
}
