﻿using System;
using System.Net.Http;
using Allocations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Steeltoe.CloudFoundry.Connector.MySql.EFCore;
using Pivotal.Discovery.Client;
using Steeltoe.Common.Discovery;
using Steeltoe.CircuitBreaker.Hystrix;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Steeltoe.Security.Authentication.CloudFoundry;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;

namespace AllocationsServer
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            // Add framework services.
            services.AddMvc(mvcOptions =>
            {
                if (!Configuration.GetValue("DISABLE_AUTH", false))
                {
                    // Set Authorized as default policy
                    var policy = new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
                    .RequireAuthenticatedUser()
                    .RequireClaim("scope", "uaa.resource")
                    .Build();
                    mvcOptions.Filters.Add(new AuthorizeFilter(policy));
                }
            });

            services.AddDbContext<AllocationContext>(options => options.UseMySql(Configuration));
            services.AddScoped<IAllocationDataGateway, AllocationDataGateway>();
            services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
            services.AddSingleton<IProjectClient>(sp =>
            {
                var handler = new DiscoveryHttpClientHandler(sp.GetService<IDiscoveryClient>());
                var httpClient = new HttpClient(handler, false)
                {
                    BaseAddress = new Uri(Configuration.GetValue<string>("REGISTRATION_SERVER_ENDPOINT"))
                };
                var logger = sp.GetService<ILogger<ProjectClient>>();
                var contextAccessor = sp.GetService<IHttpContextAccessor>();
                return new ProjectClient(
                    httpClient, logger,
                    () => contextAccessor.HttpContext.GetTokenAsync("access_token")
                );
            });
            services.AddDiscoveryClient(Configuration);
            services.AddHystrixMetricsStream(Configuration);
            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddCloudFoundryJwtBearer(Configuration);
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env, ILoggerFactory loggerFactory)
        {
            loggerFactory.AddConsole(Configuration.GetSection("Logging"));
            loggerFactory.AddDebug();

            app.UseMvc();
            app.UseDiscoveryClient();
            app.UseHystrixMetricsStream();
            app.UseHystrixRequestContext();
        }
    }
}