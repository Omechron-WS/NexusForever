using System.Security.Cryptography;
using System.Text;

namespace NexusForever.API.Authentication
{
    /// <summary>
    /// Defines and validates the credential used for authenticated service API calls.
    /// </summary>
    public static class ServiceAuthentication
    {
        public const string HeaderName = "X-NexusForever-Service-Credential";
        public const string ConfigurationKey = "Authentication:Credential";
        public const int MinimumCredentialLength = 32;
        public const int MaximumCredentialLength = 128;

        /// <summary>
        /// Returns whether a credential is safe to place in the service authentication header.
        /// </summary>
        public static bool IsCredentialValid(string credential)
        {
            if (string.IsNullOrEmpty(credential)
                || credential.Length < MinimumCredentialLength
                || credential.Length > MaximumCredentialLength)
                return false;

            foreach (char value in credential)
            {
                if (!IsCredentialCharacter(value))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Compares two valid credentials using a fixed-time digest comparison.
        /// </summary>
        public static bool CredentialsMatch(string expectedCredential, string suppliedCredential)
        {
            if (!IsCredentialValid(expectedCredential) || !IsCredentialValid(suppliedCredential))
                return false;

            byte[] expectedDigest = SHA256.HashData(Encoding.ASCII.GetBytes(expectedCredential));
            byte[] suppliedDigest = SHA256.HashData(Encoding.ASCII.GetBytes(suppliedCredential));
            return CryptographicOperations.FixedTimeEquals(expectedDigest, suppliedDigest);
        }

        private static bool IsCredentialCharacter(char value)
        {
            return value is >= 'A' and <= 'Z'
                or >= 'a' and <= 'z'
                or >= '0' and <= '9'
                or '-'
                or '_';
        }
    }
}
