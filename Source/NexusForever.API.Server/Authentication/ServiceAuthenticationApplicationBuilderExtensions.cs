using Microsoft.AspNetCore.Builder;

namespace NexusForever.API.Server.Authentication
{
    /// <summary>
    /// Provides service authentication registration for API request pipelines.
    /// </summary>
    public static class ServiceAuthenticationApplicationBuilderExtensions
    {
        /// <summary>
        /// Requires the configured service credential for subsequent request handlers.
        /// </summary>
        public static IApplicationBuilder UseServiceAuthentication(this IApplicationBuilder app)
        {
            return app.UseMiddleware<ServiceAuthenticationMiddleware>();
        }
    }
}
