using NexusForever.Game.Abstract.Matching.Queue;
using NexusForever.Game.Static.Matching;
using NexusForever.Network;
using NexusForever.Network.Message;
using NexusForever.Network.World.Message.Model;

namespace NexusForever.WorldServer.Network.Message.Handler.Matching
{
    public class ClientMatchingQueueRandomHandler : IMessageHandler<IWorldSession, ClientMatchingQueueRandom>
    {
        #region Dependency Injection

        private readonly IMatchingManager matchingManager;

        public ClientMatchingQueueRandomHandler(
            IMatchingManager matchingManager)
        {
            this.matchingManager = matchingManager;
        }

        #endregion

        public void HandleMessage(IWorldSession session, ClientMatchingQueueRandom matchingQueueRandom)
        {
            if ((uint)matchingQueueRandom.MatchType > (uint)MatchType.ScaledPrimeLevelExpedition)
                throw new InvalidPacketValueException();

            matchingManager.JoinRandomQueue(session.Player, matchingQueueRandom.Roles, matchingQueueRandom.MatchType);
        }
    }
}
