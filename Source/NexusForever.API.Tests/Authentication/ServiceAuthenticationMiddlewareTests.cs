using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NexusForever.API.Authentication;
using NexusForever.API.Server.Authentication;

namespace NexusForever.API.Tests.Authentication
{
    public class ServiceAuthenticationMiddlewareTests
    {
        private const string Credential = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ_abcdefghijklmnopqrstuvwxyz-";

        [Fact]
        public async Task Request_WithoutConfiguredCredential_ReturnsUnauthorised()
        {
            (int statusCode, bool nextInvoked) = await InvokeAsync(null, Credential);

            Assert.Equal(StatusCodes.Status401Unauthorized, statusCode);
            Assert.False(nextInvoked);
        }

        [Fact]
        public async Task Request_WithoutHeader_ReturnsUnauthorised()
        {
            (int statusCode, bool nextInvoked) = await InvokeAsync(Credential, null);

            Assert.Equal(StatusCodes.Status401Unauthorized, statusCode);
            Assert.False(nextInvoked);
        }

        [Fact]
        public async Task Request_WithIncorrectCredential_ReturnsUnauthorised()
        {
            (int statusCode, bool nextInvoked) = await InvokeAsync(Credential, new string('A', Credential.Length));

            Assert.Equal(StatusCodes.Status401Unauthorized, statusCode);
            Assert.False(nextInvoked);
        }

        [Fact]
        public async Task Request_WithMalformedCredentialHeader_ReturnsUnauthorised()
        {
            (int statusCode, bool nextInvoked) = await InvokeAsync(Credential, "too-short");

            Assert.Equal(StatusCodes.Status401Unauthorized, statusCode);
            Assert.False(nextInvoked);
        }

        [Fact]
        public async Task Request_WithDuplicateCredentialHeaders_ReturnsUnauthorised()
        {
            (int statusCode, bool nextInvoked) = await InvokeWithHeadersAsync(Credential, new[] { Credential, Credential });

            Assert.Equal(StatusCodes.Status401Unauthorized, statusCode);
            Assert.False(nextInvoked);
        }

        [Fact]
        public async Task Request_WithInvalidServerConfiguration_ReturnsUnauthorised()
        {
            (int statusCode, bool nextInvoked) = await InvokeAsync("too-short", Credential);

            Assert.Equal(StatusCodes.Status401Unauthorized, statusCode);
            Assert.False(nextInvoked);
        }

        [Fact]
        public async Task Request_WithCorrectCredential_InvokesEndpoint()
        {
            (int statusCode, bool nextInvoked) = await InvokeAsync(Credential, Credential);

            Assert.Equal(StatusCodes.Status204NoContent, statusCode);
            Assert.True(nextInvoked);
        }

        private static Task<(int StatusCode, bool NextInvoked)> InvokeAsync(string configuredCredential, string suppliedCredential)
        {
            return InvokeWithHeadersAsync(configuredCredential, suppliedCredential == null ? null : new[] { suppliedCredential });
        }

        private static async Task<(int StatusCode, bool NextInvoked)> InvokeWithHeadersAsync(string configuredCredential, string[] suppliedCredentials)
        {
            var configurationValues = new Dictionary<string, string>();
            if (configuredCredential != null)
                configurationValues[ServiceAuthentication.ConfigurationKey] = configuredCredential;

            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(configurationValues)
                .Build();

            await using ServiceProvider services = new ServiceCollection()
                .AddSingleton(configuration)
                .AddLogging()
                .BuildServiceProvider();

            bool nextInvoked = false;
            var builder = new ApplicationBuilder(services);
            builder.UseServiceAuthentication();
            builder.Run(context =>
            {
                nextInvoked = true;
                context.Response.StatusCode = StatusCodes.Status204NoContent;
                return Task.CompletedTask;
            });

            var context = new DefaultHttpContext
            {
                RequestServices = services
            };
            if (suppliedCredentials != null)
                context.Request.Headers[ServiceAuthentication.HeaderName] = suppliedCredentials;

            await builder.Build()(context);
            return (context.Response.StatusCode, nextInvoked);
        }
    }
}
