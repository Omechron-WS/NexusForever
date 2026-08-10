using System;
using NexusForever.Network.Sts;

namespace NexusForever.StsServer.Network.Message
{
    [AttributeUsage(AttributeTargets.Method)]
    public class MessageHandlerAttribute : Attribute
    {
        public string Uri { get; }
        public SessionState[] States { get; }

        /// <summary>
        /// Initialise a handler for the supplied URI and optional permitted session states.
        /// </summary>
        /// <remarks>
        /// Omitting <paramref name="states"/> makes the handler state-agnostic.
        /// </remarks>
        public MessageHandlerAttribute(string uri, params SessionState[] states)
        {
            Uri    = uri;
            States = states;
        }
    }
}
