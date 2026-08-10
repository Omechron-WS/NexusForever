using System.Collections.Generic;
using NexusForever.Network.Sts;

namespace NexusForever.StsServer.Network.Message
{
    public class MessageHandlerInfo
    {
        public MessageHandlerDelegate Delegate { get; }
        public IReadOnlyList<SessionState> States { get; }

        /// <summary>
        /// Initialise handler metadata with its delegate and permitted session states.
        /// </summary>
        public MessageHandlerInfo(MessageHandlerDelegate @delegate, params SessionState[] states)
        {
            Delegate = @delegate;
            States   = states;
        }
    }
}
