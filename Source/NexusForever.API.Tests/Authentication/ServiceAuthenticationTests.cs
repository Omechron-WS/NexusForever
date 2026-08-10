using NexusForever.API.Authentication;

namespace NexusForever.API.Tests.Authentication
{
    public class ServiceAuthenticationTests
    {
        private const string Credential = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ_abcdefghijklmnopqrstuvwxyz-";

        [Fact]
        public void CredentialsMatch_WithMatchingCredentials_ReturnsTrue()
        {
            Assert.True(ServiceAuthentication.CredentialsMatch(Credential, Credential));
        }

        [Fact]
        public void CredentialsMatch_WithDifferentCredentials_ReturnsFalse()
        {
            string differentCredential = new('A', Credential.Length);

            Assert.False(ServiceAuthentication.CredentialsMatch(Credential, differentCredential));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("too-short")]
        [InlineData("0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ secret")]
        public void IsCredentialValid_WithInvalidCredential_ReturnsFalse(string credential)
        {
            Assert.False(ServiceAuthentication.IsCredentialValid(credential));
        }

        [Fact]
        public void IsCredentialValid_WithOversizedCredential_ReturnsFalse()
        {
            string credential = new('A', ServiceAuthentication.MaximumCredentialLength + 1);

            Assert.False(ServiceAuthentication.IsCredentialValid(credential));
        }
    }
}
