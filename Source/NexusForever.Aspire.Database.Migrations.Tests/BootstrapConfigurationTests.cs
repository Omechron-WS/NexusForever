using System.Data.Common;
using NexusForever.Aspire.Database.Migrations.Configuration.Model;
using NexusForever.Aspire.Database.Migrations.Service;
using NexusForever.Aspire.Database.Migrations.WorldDatabase;
using NexusForever.Database.Auth.Model;

namespace NexusForever.Aspire.Database.Migrations.Tests
{
    public sealed class BootstrapConfigurationTests
    {
        [Fact]
        public void RequiredWorldDatabasePathCannotBeOmitted()
        {
            var options = new WorldDatabaseOptions
            {
                Required = true
            };

            InvalidDataException exception = Assert.Throws<InvalidDataException>(() => WorldDatabaseBootstrap.Prepare(options));

            Assert.Contains("Path is required", exception.Message);
        }

        [Fact]
        public void WorldConnectionForcesUserVariablesAndTransactionalCommands()
        {
            string prepared = WorldDatabaseBootstrap.PrepareConnectionString(
                "Server=localhost;Database=worlddb;AllowUserVariables=false;IgnoreCommandTransaction=true");
            var builder = new DbConnectionStringBuilder
            {
                ConnectionString = prepared
            };

            Dictionary<string, object> values = builder.Keys.Cast<string>()
                .ToDictionary(NormalizeKey, key => builder[key], StringComparer.Ordinal);
            Assert.True(Convert.ToBoolean(values["allowuservariables"]));
            Assert.False(Convert.ToBoolean(values["ignorecommandtransaction"]));
        }

        [Fact]
        public void RealmRegistrationIsIdempotentAndPreservesRealmType()
        {
            var options = new RealmRegistrationOptions
            {
                Id = 1,
                Name = "Local Test",
                Host = "127.0.0.1",
                Port = 24_000
            };
            var realm = new ServerModel
            {
                Id = 1,
                Name = "Old",
                Host = "10.0.0.5",
                Port = 12_345,
                Type = 1
            };

            Assert.True(RealmRegistration.Apply(options, realm));
            Assert.False(RealmRegistration.Apply(options, realm));
            Assert.Equal((byte)1, realm.Type);
            Assert.Equal("Local Test", realm.Name);
            Assert.Equal("127.0.0.1", realm.Host);
            Assert.Equal((ushort)24_000, realm.Port);
        }

        [Theory]
        [InlineData("")]
        [InlineData("localhost")]
        [InlineData("::1")]
        [InlineData("127.1")]
        public void RealmRegistrationRejectsNonIpv4AdvertisedHost(string host)
        {
            var options = new RealmRegistrationOptions
            {
                Host = host
            };

            Assert.Throws<InvalidDataException>(() => RealmRegistration.Validate(options));
        }

        [Theory]
        [InlineData("0.0.0.0")]
        [InlineData("0.1.2.3")]
        [InlineData("224.0.0.0")]
        [InlineData("239.255.255.255")]
        [InlineData("240.0.0.1")]
        [InlineData("255.255.255.255")]
        public void RealmRegistrationRejectsNonUnicastIpv4Address(string host)
        {
            var options = new RealmRegistrationOptions
            {
                Host = host
            };

            InvalidDataException exception = Assert.Throws<InvalidDataException>(() => RealmRegistration.Validate(options));

            Assert.Contains("usable unicast IPv4", exception.Message);
        }

        private static string NormalizeKey(string key)
        {
            return new string(key.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        }
    }
}
