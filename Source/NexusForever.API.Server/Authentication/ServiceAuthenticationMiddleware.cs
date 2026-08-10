using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using NexusForever.API.Authentication;

namespace NexusForever.API.Server.Authentication
{
    /// <summary>
    /// Rejects API requests that do not contain the configured service credential.
    /// </summary>
    public sealed class ServiceAuthenticationMiddleware
    {
        private readonly RequestDelegate next;
        private readonly string credential;
        private readonly bool isConfigured;

        /// <summary>
        /// Creates a new service authentication middleware instance.
        /// </summary>
        public ServiceAuthenticationMiddleware(
            RequestDelegate next,
            IConfiguration configuration,
            ILogger<ServiceAuthenticationMiddleware> log)
        {
            this.next = next;
            credential = configuration[ServiceAuthentication.ConfigurationKey];
            isConfigured = ServiceAuthentication.IsCredentialValid(credential);

            if (!isConfigured)
                log.LogCritical("Service API authentication is not configured correctly; all requests will be rejected.");
        }

        /// <summary>
        /// Authenticates the current request before invoking the remaining pipeline.
        /// </summary>
        public async Task InvokeAsync(HttpContext context)
        {
            bool isAuthenticated = false;
            if (isConfigured
                && context.Request.Headers.TryGetValue(ServiceAuthentication.HeaderName, out StringValues suppliedCredentials)
                && suppliedCredentials.Count == 1)
            {
                isAuthenticated = ServiceAuthentication.CredentialsMatch(credential, suppliedCredentials[0]);
            }

            if (!isAuthenticated)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            await next(context);
        }
    }
}
