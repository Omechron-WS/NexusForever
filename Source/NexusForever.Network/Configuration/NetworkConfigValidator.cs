using Microsoft.Extensions.Options;
using NexusForever.Network.Configuration.Model;

namespace NexusForever.Network.Configuration
{
    /// <summary>
    /// Validates bounded network-send configuration.
    /// </summary>
    public sealed class NetworkConfigValidator : IValidateOptions<NetworkConfig>
    {
        private const int MinimumPendingSendBytes = ushort.MaxValue;
        private const int MaximumPendingSendBytes = 16 * 1_048_576;
        private const int MaximumPendingSendFrames = 16_384;
        private const double MaximumPendingSendDrainTimeoutSeconds = 30d;

        /// <summary>
        /// Validate bounded network-send configuration.
        /// </summary>
        public ValidateOptionsResult Validate(string name, NetworkConfig options)
        {
            if (options == null)
                return ValidateOptionsResult.Fail("Network configuration is required.");

            if (options.MaximumPendingSendBytes is < MinimumPendingSendBytes or > MaximumPendingSendBytes)
            {
                return ValidateOptionsResult.Fail(
                    $"Network maximum pending send bytes must be between {MinimumPendingSendBytes} and {MaximumPendingSendBytes}.");
            }

            if (options.MaximumPendingSendFrames is < 1 or > MaximumPendingSendFrames)
            {
                return ValidateOptionsResult.Fail(
                    $"Network maximum pending send frames must be between 1 and {MaximumPendingSendFrames}.");
            }

            if (!double.IsFinite(options.PendingSendDrainTimeoutSeconds)
                || options.PendingSendDrainTimeoutSeconds is <= 0d or > MaximumPendingSendDrainTimeoutSeconds)
            {
                return ValidateOptionsResult.Fail(
                    $"Network pending send drain timeout must be greater than zero and no more than {MaximumPendingSendDrainTimeoutSeconds} seconds.");
            }

            return ValidateOptionsResult.Success;
        }
    }
}
