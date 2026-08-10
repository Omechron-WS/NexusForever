using NexusForever.Network.Configuration;
using NexusForever.Network.Configuration.Model;

namespace NexusForever.Network.Tests.Session
{
    public class NetworkConfigValidatorTests
    {
        [Fact]
        public void Validate_DefaultConfiguration_Succeeds()
        {
            var validator = new NetworkConfigValidator();

            Assert.True(validator.Validate(null, new NetworkConfig()).Succeeded);
        }

        [Theory]
        [InlineData(ushort.MaxValue - 1, 1, 1d)]
        [InlineData(16 * 1_048_576 + 1, 1, 1d)]
        [InlineData(ushort.MaxValue, 0, 1d)]
        [InlineData(ushort.MaxValue, 16_385, 1d)]
        [InlineData(ushort.MaxValue, 1, 0d)]
        [InlineData(ushort.MaxValue, 1, 30.1d)]
        [InlineData(ushort.MaxValue, 1, double.NaN)]
        [InlineData(ushort.MaxValue, 1, double.PositiveInfinity)]
        public void Validate_UnsafeSendBounds_Fails(int bytes, int frames, double drainSeconds)
        {
            var validator = new NetworkConfigValidator();
            var config = new NetworkConfig
            {
                MaximumPendingSendBytes        = bytes,
                MaximumPendingSendFrames       = frames,
                PendingSendDrainTimeoutSeconds = drainSeconds
            };

            Assert.True(validator.Validate(null, config).Failed);
        }
    }
}
