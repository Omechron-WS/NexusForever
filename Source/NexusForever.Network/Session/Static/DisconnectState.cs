using System.Net.Sockets;

namespace NexusForever.Network.Session.Static
{
    public enum DisconnectState
    {
        /// <summary>
        /// Session is connected and accepting work.
        /// </summary>
        Connected,

        /// <summary>
        /// Session is draining previously queued outbound frames before disconnecting.
        /// </summary>
        Draining,

        /// <summary>
        /// Disconnection of <see cref="NetworkSession"/> has been requested.
        /// </summary>
        Pending,

        /// <summary>
        /// Disconnection of <see cref="NetworkSession"/> is current being processed.
        /// </summary>
        Processing,

        /// <summary>
        /// Disconnected of <see cref="NetworkSession"/> has been completed.
        /// Underlying <see cref="Socket"/> resources have been released.
        /// </summary>
        Complete
    }
}
