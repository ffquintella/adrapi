using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NLog;
using NLog.Web;

namespace adrapi
{
    public class Program
    {
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
            


            /*var configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: false)
                .AddCommandLine(args)
                .Build();*/


            /*var hostUrl = configuration["hosturl"];
            if (string.IsNullOrEmpty(hostUrl))
                hostUrl = "http://0.0.0.0:5000";*/

            // NLog: setup the logger first to catch all errors
            var logger = NLog.Web.NLogBuilder.ConfigureNLog("nlog.config").GetCurrentClassLogger();
            try
            {
                logger.Debug("init main");
                CreateWebHostBuilder(args, configuration)
                    .Build().Run();
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

        public static IWebHostBuilder CreateWebHostBuilder(string[] args, IConfigurationRoot configuration)
        {

            string allowedHosts = configuration["AllowedHosts"] ?? "127.0.0.1";
            if (allowedHosts == "*") allowedHosts = "0.0.0.0";
            string certificateFile = configuration["certificate:file"] ?? "adrapi-dev.p12";
            string certificatePassword = configuration["certificate:password"] ?? "adrapi-dev";

            return WebHost.CreateDefaultBuilder(args)
                .UseKestrel(options =>
                    {
                        options.Listen(IPAddress.Parse(allowedHosts), 5000);
                        options.Listen(IPAddress.Parse(allowedHosts), 5001, listenOptions =>
                            {
                                listenOptions.UseHttps(certificateFile, certificatePassword);
                            } );
                    }
                )
                .ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                    logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Trace);
                })
                .UseNLog()
                .UseStartup<Startup>();
        }
    }
}
