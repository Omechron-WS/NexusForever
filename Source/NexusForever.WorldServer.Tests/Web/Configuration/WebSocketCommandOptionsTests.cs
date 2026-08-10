using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NexusForever.WorldServer.Web.Configuration;

namespace NexusForever.WorldServer.Tests.Web.Configuration
{
    public class WebSocketCommandOptionsTests
    {
        [Fact]
        public void EnabledEndpoint_WithoutCredentialDigest_FailsValidation()
        {
            var configurationValues = new Dictionary<string, string>
            {
                ["WebSocketCommands:Enabled"] = "true",
                ["WebSocketCommands:AllowedOrigins:0"] = "https://admin.example.test"
            };

            Assert.Throws<OptionsValidationException>(() => ResolveOptions(configurationValues));
        }

        [Fact]
        public void EnabledEndpoint_WithNonHexadecimalCredentialDigest_FailsValidation()
        {
            var configurationValues = CreateValidConfiguration();
            configurationValues["WebSocketCommands:CredentialSha256"] = new string('G', 64);

            Assert.Throws<OptionsValidationException>(() => ResolveOptions(configurationValues));
        }

        [Fact]
        public void EnabledEndpoint_WithoutAllowedOrigins_FailsValidation()
        {
            var configurationValues = CreateValidConfiguration();
            configurationValues.Remove("WebSocketCommands:AllowedOrigins:0");

            Assert.Throws<OptionsValidationException>(() => ResolveOptions(configurationValues));
        }

        [Theory]
        [InlineData("ftp://admin.example.test")]
        [InlineData("https://admin.example.test/path")]
        [InlineData("https://admin.example.test?credential=secret")]
        public void EnabledEndpoint_WithInvalidOrigin_FailsValidation(string origin)
        {
            var configurationValues = CreateValidConfiguration();
            configurationValues["WebSocketCommands:AllowedOrigins:0"] = origin;

            Assert.Throws<OptionsValidationException>(() => ResolveOptions(configurationValues));
        }

        [Theory]
        [InlineData("0")]
        [InlineData("65536")]
        public void MessageSizeOutsideBoundedRange_FailsValidation(string maximumMessageSize)
        {
            var configurationValues = CreateValidConfiguration();
            configurationValues["WebSocketCommands:MaximumMessageSize"] = maximumMessageSize;

            Assert.Throws<OptionsValidationException>(() => ResolveOptions(configurationValues));
        }

        [Fact]
        public void ValidEnabledEndpoint_PassesValidation()
        {
            WebSocketCommandOptions options = ResolveOptions(CreateValidConfiguration());

            Assert.True(options.Enabled);
            Assert.Equal(1024, options.MaximumMessageSize);
        }

        private static Dictionary<string, string> CreateValidConfiguration()
        {
            return new Dictionary<string, string>
            {
                ["WebSocketCommands:Enabled"] = "true",
                ["WebSocketCommands:CredentialSha256"] = Convert.ToHexString(
                    SHA256.HashData(Encoding.UTF8.GetBytes("unit-test-command-credential"))),
                ["WebSocketCommands:AllowedOrigins:0"] = "https://admin.example.test",
                ["WebSocketCommands:MaximumMessageSize"] = "1024"
            };
        }

        private static WebSocketCommandOptions ResolveOptions(Dictionary<string, string> configurationValues)
        {
            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(configurationValues)
                .Build();
            var services = new ServiceCollection();
            new WorldServerStartup(configuration).ConfigureServices(services);

            using ServiceProvider provider = services.BuildServiceProvider();
            return provider.GetRequiredService<IOptions<WebSocketCommandOptions>>().Value;
        }
    }
}
