using System.Net;
using Microsoft.Extensions.DependencyInjection;
using NexusForever.API.Account.Client;
using NexusForever.API.Authentication;
using NexusForever.API.Character.Client;
using NexusForever.API.Configuration.Model;
using NexusForever.API.Model;

namespace NexusForever.API.Tests.Authentication
{
    public class ServiceAuthenticationHandlerTests
    {
        private const string Credential = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ_abcdefghijklmnopqrstuvwxyz-";

        [Fact]
        public async Task AccountClient_AttachesConfiguredCredential()
        {
            var recordingHandler = new RecordingHandler();
            var services = new ServiceCollection();
            services.AddAccountAPIClient(new APIConfig
            {
                Host       = "http://account-api",
                Credential = Credential
            });
            services.AddHttpClient<AccountAPIClient>()
                .ConfigurePrimaryHttpMessageHandler(() => recordingHandler);

            await using ServiceProvider provider = services.BuildServiceProvider();
            AccountAPIClient client = provider.GetRequiredService<AccountAPIClient>();
            await client.GetAccountAsync(1u);

            AssertCredential(recordingHandler.Request);
        }

        [Fact]
        public async Task CharacterClient_AttachesConfiguredCredential()
        {
            var recordingHandler = new RecordingHandler();
            var services = new ServiceCollection();
            services.AddCharacterAPIClient(new APIConfig
            {
                Host       = "http://character-api",
                Credential = Credential
            });
            services.AddHttpClient<CharacterAPIClient>()
                .ConfigurePrimaryHttpMessageHandler(() => recordingHandler);

            await using ServiceProvider provider = services.BuildServiceProvider();
            CharacterAPIClient client = provider.GetRequiredService<CharacterAPIClient>();
            await client.GetCharacterAsync(new Identity
            {
                Id      = 1uL,
                RealmId = 1
            });

            AssertCredential(recordingHandler.Request);
        }

        [Fact]
        public void ClientRegistration_WithoutCredential_FailsClosed()
        {
            var configuration = new APIConfig
            {
                Host       = "http://account-api",
                Credential = string.Empty
            };

            Assert.Throws<ArgumentException>(() =>
                new ServiceCollection().AddAccountAPIClient(configuration));
        }

        [Fact]
        public void ClientRegistration_WithInvalidCredential_DoesNotExposeCredential()
        {
            var configuration = new APIConfig
            {
                Host       = "http://account-api",
                Credential = "not-a-valid-credential"
            };

            ArgumentException exception = Assert.Throws<ArgumentException>(() =>
                new ServiceCollection().AddAccountAPIClient(configuration));

            Assert.DoesNotContain(configuration.Credential, exception.Message);
        }

        private static void AssertCredential(HttpRequestMessage request)
        {
            Assert.NotNull(request);
            Assert.True(request.Headers.TryGetValues(ServiceAuthentication.HeaderName, out IEnumerable<string> values));
            Assert.Equal(new[] { Credential }, values);
        }

        private sealed class RecordingHandler : HttpMessageHandler
        {
            public HttpRequestMessage Request { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Request = request;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }
        }
    }
}
