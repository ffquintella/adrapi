using System;
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
#if DEBUG
            var configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.Development.json", optional: false)
                .AddCommandLine(args)
                .Build();
#else
            var configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: false)
                .AddCommandLine(args)
                .Build();
#endif

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
