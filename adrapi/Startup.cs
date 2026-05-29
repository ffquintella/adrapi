using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Mvc.Versioning;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Hosting;
using Swashbuckle.AspNetCore.Swagger;
using Microsoft.OpenApi;
using Microsoft.AspNetCore.Mvc.ApiExplorer;


namespace adrapi
{
    /// <summary>
    /// Configures dependency injection and the HTTP middleware pipeline.
    /// </summary>
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;

            var conf = ConfigurationManager.Instance;
            conf.Config = configuration;

            // Initialize the SQLite-backed API key store and import any legacy
            // security.json on first run.
            Security.ApiKeyManager.InitializeFromConfiguration(configuration);
            Security.ApiKeyMigration.ImportLegacyJsonIfPresent(configuration);
        }

        public IConfiguration Configuration { get; }

        /// <summary>
        /// Registers MVC, versioning, authorization, authentication, and Swagger services.
        /// </summary>
        public void ConfigureServices(IServiceCollection services)
        {
            ValidateLdapConfiguration();

            //services.AddMvc();

            services.AddMvc(options => options.EnableEndpointRouting = false);

            services.AddApiVersioning(o =>
            {   
                o.ReportApiVersions = true;
                o.ApiVersionReader = new HeaderApiVersionReader("api-version");
                o.AssumeDefaultVersionWhenUnspecified = true;
                o.DefaultApiVersion = new ApiVersion(2, 0);
            });
            
            services.AddVersionedApiExplorer(options =>
            {
                // Agrupar por número de versão
                options.GroupNameFormat = "'v'VVV";

                // Necessário para o correto funcionamento das rotas
                options.SubstituteApiVersionInUrl = true;
            } );

            services.AddAuthorization(options =>
            {
                options.AddPolicy(
                    "Writting",
                    policyBuilder => policyBuilder.RequireClaim("isAdministrator"));
                options.AddPolicy(
                    "Reading",
                    policyBuilder => policyBuilder.RequireAssertion(
                        context => context.User.HasClaim(claim =>
                                       claim.Type == "isAdministrator"
                                       || claim.Type == "isMonitor"))
                    );
            });

            // configure basic authentication
            services.AddAuthentication("BasicAuthentication")
                .AddScheme<AuthenticationSchemeOptions, Security.BasicAuthenticationHandler>("BasicAuthentication", null);

            ConfigureRateLimiting(services);



            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo { Title = "ADRAPI", Version = "v1" });
                c.SwaggerDoc("v2", new OpenApiInfo { Title = "ADRAPI", Version = "v2" });
                c.ResolveConflictingActions(apiDescriptions => apiDescriptions.First());
            });
         
        }

        /// <summary>
        /// Registers rate-limiting policies. The "AuthEndpoint" policy throttles the
        /// LDAP authentication endpoints with a sliding window partitioned by a
        /// composite (client-IP, authenticated keyID) key. Each unique (ip, keyID)
        /// pair gets its own bucket, so an attacker has to vary both the source IP
        /// AND the API key they hold to evade the limit.
        ///
        /// Configurable via the "rateLimit:auth" section:
        ///   permitLimit       — requests allowed per window per (ip, keyID) (default 5)
        ///   windowSeconds     — sliding window length in seconds (default 60)
        ///   segmentsPerWindow — granularity of the sliding window (default 6)
        /// </summary>
        private void ConfigureRateLimiting(IServiceCollection services)
        {
            var section = Configuration.GetSection("rateLimit:auth");
            int permit = section.GetValue<int?>("permitLimit") ?? 5;
            int windowSeconds = section.GetValue<int?>("windowSeconds") ?? 60;
            int segments = section.GetValue<int?>("segmentsPerWindow") ?? 6;
            var window = TimeSpan.FromSeconds(windowSeconds);

            services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                options.OnRejected = (context, _) =>
                {
                    context.HttpContext.Response.Headers["Retry-After"] = windowSeconds.ToString();
                    var logger = context.HttpContext.RequestServices
                        .GetRequiredService<ILoggerFactory>()
                        .CreateLogger("RateLimiter");
                    var ip = context.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                    var keyId = context.HttpContext.User?.Identity?.Name ?? "anonymous";
                    logger.LogWarning(
                        "Rate limit hit on {path} (ip={ip}, keyID={keyId}).",
                        context.HttpContext.Request.Path, ip, keyId);
                    return new System.Threading.Tasks.ValueTask();
                };

                options.AddPolicy("AuthEndpoint", httpContext =>
                {
                    var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                    var keyId = httpContext.User?.Identity?.Name ?? "anonymous";
                    return RateLimitPartition.GetSlidingWindowLimiter(
                        partitionKey: $"auth|ip={ip}|key={keyId}",
                        factory: _ => new SlidingWindowRateLimiterOptions
                        {
                            PermitLimit = permit,
                            Window = window,
                            SegmentsPerWindow = segments,
                            QueueLimit = 0,
                            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                            AutoReplenishment = true,
                        });
                });
            });
        }

        private void ValidateLdapConfiguration()
        {
            var errors = new List<string>();
            var ldap = Configuration.GetSection("ldap");

            if (!ldap.Exists())
            {
                throw new InvalidOperationException("Invalid configuration: missing 'ldap' section.");
            }

            // The top-level ldap section is the default domain.
            ValidateLdapSection("ldap", ldap, errors);

            // Each named domain under ldap:domains is validated the same way.
            var reservedNames = new[] { "users", "groups", "ous", "infos" };
            var domains = ldap.GetSection("domains");
            if (domains.Exists())
            {
                foreach (var domain in domains.GetChildren())
                {
                    if (reservedNames.Contains(domain.Key, StringComparer.OrdinalIgnoreCase))
                    {
                        errors.Add($"ldap.domains.{domain.Key} uses a reserved resource name. Domains may not be named users/groups/ous/infos.");
                    }

                    var kind = domain.GetValue<string>("kind");
                    if (string.Equals(kind, Ldap.LdapDomainRegistry.KindEntraId, StringComparison.OrdinalIgnoreCase))
                    {
                        // Entra ID-backed domain: validate the app registration, not LDAP servers.
                        var entra = Entra.EntraConfig.FromSection(domain.GetSection("entra"), domain.Key);
                        foreach (var e in entra.Validate())
                        {
                            errors.Add($"ldap.domains.{domain.Key}.{e}");
                        }
                    }
                    else
                    {
                        ValidateLdapSection($"ldap.domains.{domain.Key}", domain, errors);
                    }
                }
            }

            var defaultDomain = ldap.GetValue<string>("defaultDomain");
            if (!string.IsNullOrWhiteSpace(defaultDomain) && reservedNames.Contains(defaultDomain.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                errors.Add($"ldap.defaultDomain '{defaultDomain}' uses a reserved resource name.");
            }

            if (errors.Count > 0)
            {
                throw new InvalidOperationException("Invalid LDAP configuration: " + string.Join(" ", errors));
            }
        }

        private static void ValidateLdapSection(string label, IConfigurationSection section, List<string> errors)
        {
            var servers = section.GetSection("servers").Get<string[]>();
            var sslEnabled = section.GetValue<bool>("ssl");
            if (servers == null || servers.Length == 0)
            {
                errors.Add($"{label}.servers must contain at least one entry in the format 'host:port'.");
            }
            else
            {
                for (var i = 0; i < servers.Length; i++)
                {
                    var server = servers[i];
                    if (string.IsNullOrWhiteSpace(server))
                    {
                        errors.Add($"{label}.servers[{i}] is empty.");
                        continue;
                    }

                    var parts = server.Split(':');
                    if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || !int.TryParse(parts[1], out var port))
                    {
                        errors.Add($"{label}.servers[{i}]='{server}' is invalid. Expected 'host:port'.");
                        continue;
                    }

                    if (port < 1 || port > 65535)
                    {
                        errors.Add($"{label}.servers[{i}]='{server}' has invalid port {port}. Expected 1-65535.");
                    }

                    if (sslEnabled && port == 389)
                    {
                        errors.Add($"{label}.servers[{i}]='{server}' is incompatible with {label}.ssl=true. Use LDAPS port 636 (or set {label}.ssl=false for 389).");
                    }
                }
            }

            var poolSize = section.GetValue<short>("poolSize");
            if (poolSize <= 0)
            {
                errors.Add($"{label}.poolSize must be greater than 0. Current value: {poolSize}.");
            }
        }

        /// <summary>
        /// Configures middleware order for security, docs, and MVC endpoints.
        /// </summary>
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, IApiVersionDescriptionProvider provider)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseHsts();
            }

            app.UseHttpsRedirection();

            app.UseFileServer();
            app.UseAuthentication();
            // Rate limiter sits after authentication so policies can read the keyID claim.
            app.UseRateLimiter();

            //app.UseMiddleware<Security.KeyAuthenticationMiddleware>();

            app.UseSwagger();
            
            

            app.UseSwaggerUI(c =>
            {
                //c.SwaggerEndpoint("/swagger/v1/swagger.json", "ADRAPI V1");
                foreach (var description in provider.ApiVersionDescriptions)
                {
                    c.SwaggerEndpoint($"/swagger/{description.GroupName}/swagger.json",
                        description.GroupName.ToUpperInvariant());
                }
            });

            app.UseMvc();
        }
    }
}
