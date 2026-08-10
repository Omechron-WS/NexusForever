using NexusForever.Shared.Configuration;

namespace NexusForever.Network.Configuration.Model
{
    [ConfigurationBind]
    public class NetworkConfig
    {
        public string Host { get; set; }
        public ushort Port { get; set; }

        /// <summary>
        /// Maximum number of bytes queued or being sent to a single client.
        /// </summary>
        public int MaximumPendingSendBytes { get; set; } = 1_048_576;

        /// <summary>
        /// Maximum number of frames queued or being sent to a single client.
        /// </summary>
        public int MaximumPendingSendFrames { get; set; } = 1_024;

        /// <summary>
        /// Maximum time allowed to drain pending frames before disconnecting a client.
        /// </summary>
        public double PendingSendDrainTimeoutSeconds { get; set; } = 5d;
    }
}
