using NexusForever.Game.Abstract.Matching.Queue;
using NexusForever.Game.Static.Matching;
using NexusForever.Network;
using NexusForever.Network.Message;
using NexusForever.Network.World.Message.Model;

namespace NexusForever.WorldServer.Network.Message.Handler.Matching
{
    public class ClientMatchingQueueLeaveHandler : IMessageHandler<IWorldSession, ClientMatchingQueueLeave>
    {
        #region Dependency Injection

        private readonly IMatchingManager matchingManager;

        public ClientMatchingQueueLeaveHandler(
            IMatchingManager matchingManager)
        {
            this.matchingManager = matchingManager;
        }

        #endregion

        public void HandleMessage(IWorldSession session, ClientMatchingQueueLeave matchingQueueLeave)
        {
            if ((uint)matchingQueueLeave.MatchType > (uint)MatchType.ScaledPrimeLevelExpedition)
                throw new InvalidPacketValueException();

            matchingManager.LeaveQueue(session.Player, matchingQueueLeave.MatchType);
        }
    }
}
