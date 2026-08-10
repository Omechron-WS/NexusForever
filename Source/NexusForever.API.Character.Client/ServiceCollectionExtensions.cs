using Microsoft.Extensions.DependencyInjection;
using NexusForever.API.Authentication;
using NexusForever.API.Configuration.Model;

namespace NexusForever.API.Character.Client
{
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Registers the authenticated Character API client.
        /// </summary>
        public static IServiceCollection AddCharacterAPIClient(this IServiceCollection sc, APIConfig configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);

            string credential = configuration.Credential;
            if (!ServiceAuthentication.IsCredentialValid(credential))
            {
                throw new ArgumentException(
                    $"The Character API credential must contain between {ServiceAuthentication.MinimumCredentialLength} and {ServiceAuthentication.MaximumCredentialLength} base64url characters.",
                    nameof(configuration));
            }

            sc.AddHttpClient<CharacterAPIClient>(c => c.BaseAddress = new Uri(configuration.Host))
                .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
                {
                    AllowAutoRedirect = false
                })
                .AddHttpMessageHandler(() => new ServiceAuthenticationHandler(credential));
            return sc;
        }
    }
}
