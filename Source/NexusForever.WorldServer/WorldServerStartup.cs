using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NexusForever.WorldServer.Web;
using NexusForever.WorldServer.Web.Configuration;
using NexusForever.WorldServer.Web.Middleware;

namespace NexusForever.WorldServer
{
    public class WorldServerStartup
    {
        public IConfiguration Configuration { get; }

        public WorldServerStartup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton<IWebSocketCommandContextFactory, WebSocketCommandContextFactory>();
            services.AddOptions<WebSocketCommandOptions>()
                .Bind(Configuration.GetSection(WebSocketCommandOptions.SectionName))
                .Validate(options => !options.Enabled || options.HasValidCredentialHash(),
                    "An enabled command WebSocket requires a hexadecimal SHA-256 credential digest.")
                .Validate(options => !options.Enabled || options.AllowedOrigins?.Count > 0,
                    "An enabled command WebSocket requires at least one allowed origin.")
                .Validate(options => options.AllowedOrigins?.TrueForAll(origin => WebSocketCommandOrigin.TryNormalise(origin, out _)) == true,
                    "Command WebSocket allowed origins must be absolute HTTP or HTTPS origins without paths, queries, or fragments.")
                .Validate(options => options.MaximumMessageSize is > 0 and <= WebSocketCommandOptions.MaximumAllowedMessageSize,
                    $"Command WebSocket maximum message size must be between 1 and {WebSocketCommandOptions.MaximumAllowedMessageSize} bytes.")
                .ValidateOnStart();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, IServiceProvider serviceProvider)
        {
            WebSocketCommandOptions commandOptions = serviceProvider.GetRequiredService<IOptions<WebSocketCommandOptions>>().Value;
            var webSocketOptions = new WebSocketOptions
            {
                KeepAliveInterval = TimeSpan.FromSeconds(30)
            };

            foreach (string origin in commandOptions.AllowedOrigins ?? [])
            {
                if (WebSocketCommandOrigin.TryNormalise(origin, out string normalisedOrigin))
                    webSocketOptions.AllowedOrigins.Add(normalisedOrigin);
            }

            if (commandOptions.Enabled)
            {
                app.UseWebSockets(webSocketOptions);
                app.UseMiddleware<WebSocketMiddleware>();
            }
        }
    }
}
