using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Mvc;
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



            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo { Title = "ADRAPI", Version = "v1" });
                c.SwaggerDoc("v2", new OpenApiInfo { Title = "ADRAPI", Version = "v2" });
                c.ResolveConflictingActions(apiDescriptions => apiDescriptions.First());
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

            var servers = ldap.GetSection("servers").Get<string[]>();
            if (servers == null || servers.Length == 0)
            {
                errors.Add("ldap.servers must contain at least one entry in the format 'host:port'.");
            }
            else
            {
                for (var i = 0; i < servers.Length; i++)
                {
                    var server = servers[i];
                    if (string.IsNullOrWhiteSpace(server))
                    {
                        errors.Add($"ldap.servers[{i}] is empty.");
                        continue;
                    }

                    var parts = server.Split(':');
                    if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || !int.TryParse(parts[1], out var port))
                    {
                        errors.Add($"ldap.servers[{i}]='{server}' is invalid. Expected 'host:port'.");
                        continue;
                    }

                    if (port < 1 || port > 65535)
                    {
                        errors.Add($"ldap.servers[{i}]='{server}' has invalid port {port}. Expected 1-65535.");
                    }
                }
            }

            var poolSize = ldap.GetValue<short>("poolSize");
            if (poolSize <= 0)
            {
                errors.Add($"ldap.poolSize must be greater than 0. Current value: {poolSize}.");
            }

            if (errors.Count > 0)
            {
                throw new InvalidOperationException("Invalid LDAP configuration: " + string.Join(" ", errors));
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
