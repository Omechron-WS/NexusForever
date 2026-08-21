using NexusForever.Game;
using NexusForever.Game.Static.Info;
using NexusForever.Network;
using NexusForever.Network.Internal;
using NexusForever.Network.Internal.Message.Player;
using NexusForever.Network.Message;
using NexusForever.Network.World.Message.Model.Info;
using NexusForever.Shared;
using NexusForever.WorldServer.Network.Internal;

namespace NexusForever.WorldServer.Network.Message.Handler.Info
{
    public class ClientPlayerInfoRequestHandler : IMessageHandler<IWorldSession, ClientPlayerInfoRequest>
    {
        #region Dependency Injection

        private readonly IInternalMessagePublisher messagePublisher;

        public ClientPlayerInfoRequestHandler(
            IInternalMessagePublisher messagePublisher)
        {
            this.messagePublisher = messagePublisher;
        }

        #endregion

        /// <summary>
        /// Handled responses to Player Info Requests.
        /// </summary>
        public void HandleMessage(IWorldSession session, ClientPlayerInfoRequest request)
        {
            if (request.Type is not (PlayerInfoRequestType.Default
                or PlayerInfoRequestType.Social
                or PlayerInfoRequestType.Friend
                or PlayerInfoRequestType.Rival
                or PlayerInfoRequestType.Neighbour
                or PlayerInfoRequestType.Loot
                or PlayerInfoRequestType.BankLog
                or PlayerInfoRequestType.Maker
                or PlayerInfoRequestType.Pvp))
                throw new InvalidPacketValueException();

            messagePublisher.PublishAsync(new PlayerInfoRequestMessage
            {
                Source = session.Player.Identity.ToInternalIdentity(),
                Type   = request.Type,
                Target = request.Identity.ToInternalIdentity()
            }).FireAndForgetAsync();
        }
    }
}
