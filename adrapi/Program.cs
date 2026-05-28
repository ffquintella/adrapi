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
        /// <summary>
        /// Configures logging, loads configuration, and starts the host.
        /// </summary>
        public static void Main(string[] args)
        {
            var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
#if DEBUG
                ?? "Development";
#else
                ?? "Production";
#endif

            var configBuilder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddJsonFile($"appsettings.{environment}.json", optional: true, reloadOnChange: true);

            // In Development, layer secrets from `dotnet user-secrets` on top of the JSON files.
            // These are stored outside the repo (~/.microsoft/usersecrets/<UserSecretsId>/secrets.json on Linux/macOS,
            // %APPDATA%\Microsoft\UserSecrets\<UserSecretsId>\secrets.json on Windows) and never get committed.
            if (string.Equals(environment, "Development", StringComparison.OrdinalIgnoreCase))
            {
                configBuilder.AddUserSecrets<Program>(optional: true);
            }

            // SQLite-backed encrypted secrets (LDAP bind credentials etc.). Reads
            // from the same DB used by the API key store. Optional — a missing DB
            // or seed simply means earlier sources win.
            var bootstrap = configBuilder.Build();
            var dbPath = bootstrap.GetSection("security").GetValue<string>("databaseFile")
                ?? "cfg/api-keys.db";
            var seedPath = bootstrap.GetSection("security").GetValue<string>("seedFile")
                ?? "cfg/.seed";
            configBuilder.AddSqliteSecrets(dbPath, seedPath, optional: true);

            configBuilder.AddEnvironmentVariables();
            configBuilder.AddCommandLine(args);

            var configuration = configBuilder.Build();

            // NLog: setup the logger first to catch all errors
            var logger = LogManager.Setup().LoadConfigurationFromAppSettings().GetCurrentClassLogger();

            try
            {
                logger.Debug("init main");
                CreateHostBuilder(args, configuration).Build().Run();
            }
            catch (Exception ex)
            {
                //NLog: catch setup errors
                logger.Error(ex, "Stopped program because of exception");
                throw;
            }
            finally
            {
                // Ensure to flush and stop internal timers/threads before application-exit (Avoid segmentation fault on Linux)
                NLog.LogManager.Shutdown();
            }
        }

        /// <summary>
        /// Builds the generic host and configures the ASP.NET Core web server.
        /// </summary>
        public static IHostBuilder CreateHostBuilder(string[] args, IConfigurationRoot configuration)
        {
            string allowedHosts = configuration["AllowedHosts"] ?? "127.0.0.1";
            if (allowedHosts == "*") allowedHosts = "0.0.0.0";
            string certificateFile = configuration["certificate:file"] ?? "adrapi-dev.p12";
            string certificatePassword = configuration["certificate:password"] ?? "adrapi-dev";

            return Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseKestrel(options =>
                    {
                        options.Listen(IPAddress.Parse(allowedHosts), 6000);
                        options.Listen(IPAddress.Parse(allowedHosts), 6001, listenOptions =>
                        {
                            listenOptions.UseHttps(certificateFile, certificatePassword);
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
